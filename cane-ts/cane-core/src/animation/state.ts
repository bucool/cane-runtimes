import { eventIntegerBounds, eventRelativePosition, type EventBounds } from "./event-lattice.js";
import type {
  RuntimeAnimationV1,
  RuntimeEventKeyV1,
  RuntimeEventV1,
  RuntimeBlendDurationV1,
  RuntimeQueueFadeOutV1,
  RuntimeQueueAnimationV1,
  RuntimeQueuedEntryOptionsV1,
  RuntimeQueuedAnimationPlaybackV1,
  RuntimeSamplingV1,
  RuntimePlayAnimationV1,
  RuntimeFadeOutTrackV1,
  RuntimeSetTrackAnimationRangeV1,
  RuntimeSetTrackEndV1,
  RuntimeSetTrackBlendDurationV1,
  RuntimeTrackOptionsV1,
  RuntimeTrackStateV1,
} from "../contracts.js";
import type { RuntimeDataV1 } from "../data.js";
import { RuntimeErrorV1 } from "../errors.js";
import { finiteF32, f32, f32Add, f32Mul } from "../math/f32.js";
import { quantizeSampleTimeV1 } from "./curves.js";
import type { RuntimeAnimationLayerV1 } from "./sampling.js";
import type { RuntimePoseTrackV1, RuntimePoseEndpointV1 } from "./endpoint-pose.js";

const MAX_TRACK_INDEX = 4095;
const MAX_EVENTS_PER_OPERATION = 1_000_000;
const F32_MAX = Math.fround(3.4028234663852886e38);
const AUTHORED_SAMPLING_V1: RuntimeSamplingV1 = Object.freeze({ mode: "authored" });
const AUTHORED_PLAN_V1: RuntimeSamplingPlanV1 = Object.freeze({ forceStepped: false, frameStepSeconds: null });
const FORCE_STEPPED_PLAN_V1: RuntimeSamplingPlanV1 = Object.freeze({ forceStepped: true, frameStepSeconds: null });
const FIXED_SAMPLING_PLAN_CACHE = new WeakMap<object, RuntimeSamplingPlanV1>();

interface AnimationPlaybackV1 {
  animation: RuntimeAnimationV1 | null;
  animationRangeStart: number;
  animationRangeEnd: number;
  playbackTime: number;
  startDelay: number;
  playbackEnd: number;
  playbackRate: number;
  looping: boolean;
  blendWeight: number;
  blend: "replace" | "additive";
  blendDuration: number;
  blendElapsed: number;
  eventBlendThreshold: number;
  attachmentBlendThreshold: number;
  drawOrderBlendThreshold: number;
  holdPreviousPose: boolean;
  eventCursorInitialized: boolean;
  blendSource: AnimationPlaybackV1 | null;
}

interface QueuedEntryV1 {
  animation: RuntimeAnimationV1 | null;
  looping: boolean;
  startDelay: number;
  blendDuration: number;
}

interface TrackV1 {
  current: AnimationPlaybackV1 | null;
  queue: QueuedEntryV1[];
}

interface PendingEventV1 {
  readonly wallOffset: number;
  readonly trackIndex: number;
  readonly ordinal: number;
  readonly event: RuntimeEventV1;
}

interface OutgoingControllerV1 {
  readonly entry: AnimationPlaybackV1;
  readonly blendElapsedAtStart: number;
}

export interface RuntimeSamplingPlanV1 {
  readonly forceStepped: boolean;
  readonly frameStepSeconds: number | null;
}

/** Internal mutable projection used to avoid allocating a public track snapshot per frame. */
export interface RuntimePrimaryClockV1 {
  animationId: string | null;
  playbackTime: number;
  duration: number;
  looping: boolean;
  rangeStart: number;
  rangeEnd: number;
  resetWeight: number;
}

export class RuntimeAnimationPlayerV1 {
  readonly #data: RuntimeDataV1;
  readonly #tracks = new Map<number, TrackV1>();
  readonly #sortedTrackIndicesCache: number[] = [];
  readonly #eventCollectorScratch = new EventCollectorV1(MAX_EVENTS_PER_OPERATION);
  #defaultMix = 0;
  #detachedTime = 0;
  readonly #pairMixes = new Map<string, number>();
  readonly #pairMixKeys: string[] = [];

  constructor(data: RuntimeDataV1) {
    this.#data = data;
  }

  clone(): RuntimeAnimationPlayerV1 {
    const result = new RuntimeAnimationPlayerV1(this.#data);
    result.#defaultMix = this.#defaultMix;
    result.#detachedTime = this.#detachedTime;
    for (let index = 0; index < this.#pairMixKeys.length; index += 1) {
      const key = this.#pairMixKeys[index];
      if (key === undefined) continue;
      const duration = this.#pairMixes.get(key);
      if (duration !== undefined) result.#setPairMix(key, duration);
    }
    for (const [index, track] of this.#tracks) {
      result.#tracks.set(index, {
        current: cloneEntry(track.current),
        queue: track.queue.map((queued) => ({ ...queued })),
      });
    }
    return result;
  }

  /**
   * Copies logical playback/configuration state into retained storage.
   * Performance-mode transactions alternate two instances, so a stable
   * track/queue shape performs no object or array allocation after warm-up.
   */
  copyFrom(source: RuntimeAnimationPlayerV1): this {
    if (source.#data !== this.#data) {
      throw new RuntimeErrorV1("invalidArgument", "copyAnimationPlayer", "Animation states use different Runtime data.");
    }
    this.#defaultMix = source.#defaultMix;
    this.#detachedTime = source.#detachedTime;
    const sourcePairMixKeys = source.#pairMixKeys;
    let samePairMixShape = this.#pairMixKeys.length === sourcePairMixKeys.length;
    if (samePairMixShape) {
      for (let index = 0; index < sourcePairMixKeys.length; index += 1) {
        if (this.#pairMixKeys[index] !== sourcePairMixKeys[index]) {
          samePairMixShape = false;
          break;
        }
      }
    }
    if (!samePairMixShape) {
      for (let index = 0; index < this.#pairMixKeys.length; index += 1) {
        const key = this.#pairMixKeys[index];
        if (key !== undefined && !source.#pairMixes.has(key)) this.#pairMixes.delete(key);
      }
      this.#pairMixKeys.length = sourcePairMixKeys.length;
      for (let index = 0; index < sourcePairMixKeys.length; index += 1) {
        const key = sourcePairMixKeys[index];
        if (key !== undefined) this.#pairMixKeys[index] = key;
      }
    }
    for (let index = 0; index < sourcePairMixKeys.length; index += 1) {
      const key = sourcePairMixKeys[index];
      if (key === undefined) continue;
      const duration = source.#pairMixes.get(key);
      if (duration !== undefined) this.#pairMixes.set(key, duration);
    }

    const sourceTrackIndices = source.#sortedTrackIndices();
    const targetTrackIndices = this.#sortedTrackIndices();
    let sameTrackShape = sourceTrackIndices.length === targetTrackIndices.length;
    if (sameTrackShape) {
      for (let index = 0; index < sourceTrackIndices.length; index += 1) {
        if (sourceTrackIndices[index] !== targetTrackIndices[index]) {
          sameTrackShape = false;
          break;
        }
      }
    }
    if (!sameTrackShape) {
      for (let index = 0; index < targetTrackIndices.length; index += 1) {
        const trackIndex = targetTrackIndices[index];
        if (trackIndex !== undefined && !source.#tracks.has(trackIndex)) this.#tracks.delete(trackIndex);
      }
    }
    for (let position = 0; position < sourceTrackIndices.length; position += 1) {
      const index = sourceTrackIndices[position];
      if (index === undefined) continue;
      const sourceTrack = source.#tracks.get(index);
      if (sourceTrack === undefined) continue;
      let targetTrack = this.#tracks.get(index);
      if (targetTrack === undefined) {
        targetTrack = { current: null, queue: [] };
        this.#tracks.set(index, targetTrack);
      }
      targetTrack.current = copyEntryIntoV1(targetTrack.current, sourceTrack.current);
      targetTrack.queue.length = sourceTrack.queue.length;
      for (let queueIndex = 0; queueIndex < sourceTrack.queue.length; queueIndex += 1) {
        const sourceQueued = sourceTrack.queue[queueIndex];
        if (sourceQueued === undefined) continue;
        let targetQueued = targetTrack.queue[queueIndex];
        if (targetQueued === undefined) {
          targetQueued = { animation: null, looping: false, startDelay: 0, blendDuration: 0 };
          targetTrack.queue[queueIndex] = targetQueued;
        }
        targetQueued.animation = sourceQueued.animation;
        targetQueued.looping = sourceQueued.looping;
        targetQueued.startDelay = sourceQueued.startDelay;
        targetQueued.blendDuration = sourceQueued.blendDuration;
      }
    }
    this.#sortedTrackIndicesCache.length = sourceTrackIndices.length;
    for (let index = 0; index < sourceTrackIndices.length; index += 1) {
      const trackIndex = sourceTrackIndices[index];
      if (trackIndex !== undefined) this.#sortedTrackIndicesCache[index] = trackIndex;
    }
    return this;
  }

  /** Rebinds still-valid animation entries and mixes to a replacement immutable data set. */
  reconciled(data: RuntimeDataV1): RuntimeAnimationPlayerV1 {
    const result = new RuntimeAnimationPlayerV1(data);
    result.#defaultMix = this.#defaultMix;
    result.#detachedTime = this.#detachedTime;
    for (let index = 0; index < this.#pairMixKeys.length; index += 1) {
      const key = this.#pairMixKeys[index];
      if (key === undefined) continue;
      const duration = this.#pairMixes.get(key);
      if (duration === undefined) continue;
      const [from, to] = parsePairKeyV1(key);
      if (hasAnimationV1(data, from) && hasAnimationV1(data, to)) result.#setPairMix(key, duration);
    }
    for (const [index, track] of this.#tracks) {
      const current = reconcileEntryV1(track.current, data);
      result.#tracks.set(index, {
        current,
        queue: current === null
          ? []
          : track.queue.flatMap((queued) => {
              if (queued.animation === null) return [{ ...queued }];
              const animation = findAnimationV1(data, queued.animation.id);
              return animation === null ? [] : [{ ...queued, animation }];
            }),
      });
    }
    return result;
  }

  /** Copies only default/pair mix configuration into a fresh trackless state. */
  configurationClone(data: RuntimeDataV1): RuntimeAnimationPlayerV1 {
    const result = new RuntimeAnimationPlayerV1(data);
    result.#defaultMix = this.#defaultMix;
    for (let index = 0; index < this.#pairMixKeys.length; index += 1) {
      const key = this.#pairMixKeys[index];
      if (key === undefined) continue;
      const duration = this.#pairMixes.get(key);
      if (duration === undefined) continue;
      const [from, to] = parsePairKeyV1(key);
      if (hasAnimationV1(data, from) && hasAnimationV1(data, to)) result.#setPairMix(key, duration);
    }
    return result;
  }

  playAnimation(request: RuntimePlayAnimationV1): RuntimeEventV1[] {
    const trackIndex = validateTrackIndex(request.trackIndex, "playAnimation");
    const animation = this.#lookupAnimation(request.animationId, "playAnimation");
    if (typeof request.looping !== "boolean") invalid("playAnimation", "looping", "looping must be boolean.");
    const track = this.#track(trackIndex);
    const outgoing = track.current;
    const blendDuration = request.mixSeconds === undefined || request.mixSeconds === null
      ? (outgoing === null || outgoing.animation === null ? 0 : this.#blendDuration(outgoing.animation.id, animation.id))
      : nonNegative(request.mixSeconds, "playAnimation", "mixSeconds");
    const events: RuntimeEventV1[] = [];
    if (outgoing !== null) {
      events.push(lifecycle(trackIndex, animationIdV1(outgoing), "playbackInterrupted"));
    }
    const entry = createEntry(animation, request.looping, blendDuration);
    if (outgoing !== null) {
      if (blendDuration === 0) disposeStartedChain(outgoing, trackIndex, events);
      else entry.blendSource = outgoing;
    }
    for (const queued of track.queue) events.push(lifecycle(trackIndex, queued.animation?.id ?? null, "playbackRemoved"));
    track.queue.length = 0;
    track.current = entry;
    this.#detachedTime = 0;
    events.push(lifecycle(trackIndex, animation.id, "playbackStarted"));
    return events;
  }

  fadeOutTrack(request: RuntimeFadeOutTrackV1): RuntimeEventV1[] {
    const trackIndex = validateTrackIndex(request.trackIndex, "fadeOutTrack");
    const blendDuration = nonNegative(request.blendDurationSeconds, "fadeOutTrack", "blendDurationSeconds");
    const track = this.#track(trackIndex);
    const outgoing = track.current;
    const events: RuntimeEventV1[] = [];
    if (outgoing !== null) events.push(lifecycle(trackIndex, animationIdV1(outgoing), "playbackInterrupted"));
    const entry = createEmptyEntry(blendDuration);
    if (outgoing !== null) {
      if (blendDuration === 0) disposeStartedChain(outgoing, trackIndex, events);
      else entry.blendSource = outgoing;
    }
    for (const queued of track.queue) events.push(lifecycle(trackIndex, queued.animation?.id ?? null, "playbackRemoved"));
    track.queue.length = 0;
    track.current = entry;
    events.push(lifecycle(trackIndex, null, "playbackStarted"));
    return events;
  }

  queueAnimation(request: RuntimeQueueAnimationV1): RuntimeEventV1[] {
    const trackIndex = validateTrackIndex(request.trackIndex, "queueAnimation");
    const animation = this.#lookupAnimation(request.animationId, "queueAnimation");
    if (typeof request.looping !== "boolean") invalid("queueAnimation", "looping", "looping must be boolean.");
    const delayInput = finite(request.startDelaySeconds, "queueAnimation", "startDelaySeconds");
    const track = this.#track(trackIndex);
    if (track.current === null) {
      return this.playAnimation({ trackIndex, animationId: animation.id, looping: request.looping });
    }
    const current = track.current;
    const previous = track.queue[track.queue.length - 1];
    const previousAnimation = previous === undefined ? current.animation : previous.animation;
    const previousDuration = previous === undefined ? entryRangeDurationV1(current) : queuedRangeDurationV1(previous);
    const blendDuration = previousAnimation === null ? 0 : this.#blendDuration(previousAnimation.id, animation.id);
    const startDelay = delayInput > 0
      ? delayInput
      : f32(Math.max(0, f32Add(previousDuration, f32Add(-blendDuration, delayInput))));
    track.queue.push({ animation, looping: request.looping, startDelay, blendDuration });
    return [];
  }

  queueFadeOut(request: RuntimeQueueFadeOutV1): RuntimeEventV1[] {
    const trackIndex = validateTrackIndex(request.trackIndex, "queueFadeOut");
    const blendDuration = nonNegative(request.blendDurationSeconds, "queueFadeOut", "blendDurationSeconds");
    const delayInput = finite(request.startDelaySeconds, "queueFadeOut", "startDelaySeconds");
    const track = this.#track(trackIndex);
    if (track.current === null) return this.fadeOutTrack({ trackIndex, blendDurationSeconds: blendDuration });
    const current = track.current;
    const previous = track.queue[track.queue.length - 1];
    const previousDuration = previous === undefined ? entryRangeDurationV1(current) : queuedRangeDurationV1(previous);
    const startDelay = delayInput > 0
      ? delayInput
      : f32(Math.max(0, f32Add(previousDuration, f32Add(-blendDuration, delayInput))));
    track.queue.push({ animation: null, looping: false, startDelay, blendDuration });
    return [];
  }

  removeQueuedEntry(trackIndexValue: number, queueIndexValue: number): RuntimeEventV1[] {
    const trackIndex = validateTrackIndex(trackIndexValue, "removeQueuedEntry");
    const queueIndex = validateQueueIndex(queueIndexValue, "removeQueuedEntry");
    const track = this.#tracks.get(trackIndex);
    const removed = track?.queue.splice(queueIndex, 1)[0];
    if (removed === undefined) queuedNotFound("removeQueuedEntry", queueIndex);
    return [lifecycle(trackIndex, removed.animation?.id ?? null, "playbackRemoved")];
  }

  setQueuedEntryOptions(request: RuntimeQueuedEntryOptionsV1): void {
    const operation = "setQueuedEntryOptions";
    const trackIndex = validateTrackIndex(request.trackIndex, operation);
    const queueIndex = validateQueueIndex(request.queueIndex, operation);
    if ((request.startDelaySeconds === undefined || request.startDelaySeconds === null)
      && (request.blendDurationSeconds === undefined || request.blendDurationSeconds === null)
      && (request.looping === undefined || request.looping === null)) {
      invalid(operation, "options", "At least one queued entry option must be present.");
    }
    const queued = this.#tracks.get(trackIndex)?.queue[queueIndex];
    if (queued === undefined) queuedNotFound(operation, queueIndex);
    if (request.startDelaySeconds !== undefined && request.startDelaySeconds !== null) {
      queued.startDelay = nonNegative(request.startDelaySeconds, operation, "startDelaySeconds");
    }
    if (request.blendDurationSeconds !== undefined && request.blendDurationSeconds !== null) {
      queued.blendDuration = nonNegative(request.blendDurationSeconds, operation, "blendDurationSeconds");
    }
    if (request.looping !== undefined && request.looping !== null) {
      if (typeof request.looping !== "boolean") invalid(operation, "looping", "looping must be boolean.");
      queued.looping = request.looping;
    }
  }

  stopTrack(trackIndexValue: number): RuntimeEventV1[] {
    const trackIndex = validateTrackIndex(trackIndexValue, "stopTrack");
    const track = this.#tracks.get(trackIndex);
    if (track === undefined) return [];
    const events: RuntimeEventV1[] = [];
    if (track.current !== null) disposeStartedChain(track.current, trackIndex, events);
    for (const queued of track.queue) events.push(lifecycle(trackIndex, queued.animation?.id ?? null, "playbackRemoved"));
    track.current = null;
    track.queue.length = 0;
    return events;
  }

  stopAllTracks(): RuntimeEventV1[] {
    const events: RuntimeEventV1[] = [];
    for (const trackIndex of this.#sortedTrackIndices()) events.push(...this.stopTrack(trackIndex));
    return events;
  }

  setDefaultMix(durationSeconds: number): void {
    this.#defaultMix = nonNegative(durationSeconds, "setDefaultMix", "durationSeconds");
  }

  defaultMixSeconds(): number {
    return this.#defaultMix;
  }

  animationTime(): number {
    const primary = this.#tracks.get(0)?.current;
    return primary === undefined || primary === null ? this.#detachedTime : poseTime(primary);
  }

  /** Writes only the clock fields needed by Runtime physics; no snapshot object is created. */
  writePrimaryClock(detachedTime: number, output: RuntimePrimaryClockV1): void {
    const primary = this.#tracks.get(0)?.current;
    output.animationId = primary?.animation?.id ?? null;
    output.playbackTime = primary === undefined || primary === null ? detachedTime : primary.playbackTime;
    output.duration = primary === undefined || primary === null ? 0 : entryRangeDurationV1(primary);
    output.looping = primary?.looping ?? false;
    output.rangeStart = primary?.animationRangeStart ?? 0;
    output.rangeEnd = primary?.animationRangeEnd ?? 0;
    output.resetWeight = primary === undefined || primary === null ? 0 : primary.blendWeight * blendProgress(primary);
  }

  hasActiveEntries(): boolean {
    for (const track of this.#tracks.values()) {
      if (track.current !== null) return true;
    }
    return false;
  }

  setBlendDuration(request: RuntimeBlendDurationV1): void {
    this.#lookupAnimation(request.fromAnimationId, "setBlendDuration");
    this.#lookupAnimation(request.toAnimationId, "setBlendDuration");
    const duration = nonNegative(request.durationSeconds, "setBlendDuration", "durationSeconds");
    this.#setPairMix(pairKey(request.fromAnimationId, request.toAnimationId), duration);
  }

  setPlaybackTime(trackIndexValue: number, timeSeconds: number): void {
    const trackIndex = validateTrackIndex(trackIndexValue, "setPlaybackTime");
    const entry = this.#requireCurrent(trackIndex, "setPlaybackTime");
    entry.playbackTime = nonNegative(timeSeconds, "setPlaybackTime", "timeSeconds");
    entry.eventCursorInitialized = true;
  }

  setAnimationTime(timeSeconds: number): void {
    const time = nonNegative(timeSeconds, "setAnimationTime", "timeSeconds");
    this.#detachedTime = time;
    const entry = this.#tracks.get(0)?.current;
    if (entry === undefined || entry === null) return;
    entry.playbackTime = f32(Math.max(0, Math.min(Math.max(time, entry.animationRangeStart), entry.animationRangeEnd) - entry.animationRangeStart));
    entry.eventCursorInitialized = true;
  }

  setTrackBlendDuration(request: RuntimeSetTrackBlendDurationV1): void {
    const entry = this.#requireCurrent(validateTrackIndex(request.trackIndex, "setTrackBlendDuration"), "setTrackBlendDuration");
    entry.blendDuration = nonNegative(request.blendDurationSeconds, "setTrackBlendDuration", "blendDurationSeconds");
  }

  setPlaybackEnd(request: RuntimeSetTrackEndV1): void {
    const entry = this.#requireCurrent(validateTrackIndex(request.trackIndex, "setPlaybackEnd"), "setPlaybackEnd");
    entry.playbackEnd = request.playbackEndSeconds === null
      ? F32_MAX
      : nonNegative(request.playbackEndSeconds, "setPlaybackEnd", "playbackEndSeconds");
  }

  setTrackAnimationRange(request: RuntimeSetTrackAnimationRangeV1): void {
    const operation = "setTrackAnimationRange";
    const entry = this.#requireCurrent(validateTrackIndex(request.trackIndex, operation), operation);
    const current = poseTime(entry);
    if (request.range === null) {
      entry.animationRangeStart = 0;
      entry.animationRangeEnd = entry.animation?.duration ?? 0;
      entry.playbackTime = f32(Math.min(Math.max(current, 0), entry.animationRangeEnd));
      entry.eventCursorInitialized = true;
      return;
    }
    if (request.range === undefined || typeof request.range !== "object") {
      invalid(operation, "range", "range must be an object or null.");
    }
    const start = nonNegative(request.range.startSeconds, operation, "range.startSeconds");
    const end = nonNegative(request.range.endSeconds, operation, "range.endSeconds");
    const duration = entry.animation?.duration ?? 0;
    if (!(end > start) || end > duration) {
      invalid(operation, "range", "Animation range must be non-empty and fit the current animation.");
    }
    entry.animationRangeStart = start;
    entry.animationRangeEnd = end;
    entry.playbackTime = f32(Math.max(0, Math.min(Math.max(current, start), end) - start));
    entry.eventCursorInitialized = true;
  }

  setTrackOptions(request: RuntimeTrackOptionsV1): void {
    const trackIndex = validateTrackIndex(request.trackIndex, "setTrackOptions");
    const entry = this.#requireCurrent(trackIndex, "setTrackOptions");
    if (request.blendWeight !== undefined && request.blendWeight !== null) entry.blendWeight = unit(request.blendWeight, "setTrackOptions", "blendWeight");
    if (request.playbackRate !== undefined && request.playbackRate !== null) entry.playbackRate = finite(request.playbackRate, "setTrackOptions", "playbackRate");
    if (request.looping !== undefined && request.looping !== null) {
      if (typeof request.looping !== "boolean") invalid("setTrackOptions", "looping", "looping must be boolean.");
      entry.looping = request.looping;
    }
    if (request.blend !== undefined && request.blend !== null) {
      if (request.blend !== "replace" && request.blend !== "additive") invalid("setTrackOptions", "blend", "Unknown track blend mode.");
      entry.blend = request.blend;
    }
    if (request.eventBlendThreshold !== undefined && request.eventBlendThreshold !== null) {
      entry.eventBlendThreshold = unit(request.eventBlendThreshold, "setTrackOptions", "eventBlendThreshold");
    }
    if (request.attachmentBlendThreshold !== undefined && request.attachmentBlendThreshold !== null) {
      entry.attachmentBlendThreshold = unit(request.attachmentBlendThreshold, "setTrackOptions", "attachmentBlendThreshold");
    }
    if (request.drawOrderBlendThreshold !== undefined && request.drawOrderBlendThreshold !== null) {
      entry.drawOrderBlendThreshold = unit(request.drawOrderBlendThreshold, "setTrackOptions", "drawOrderBlendThreshold");
    }
    if (request.holdPreviousPose !== undefined && request.holdPreviousPose !== null) {
      if (typeof request.holdPreviousPose !== "boolean") invalid("setTrackOptions", "holdPreviousPose", "holdPreviousPose must be boolean.");
      entry.holdPreviousPose = request.holdPreviousPose;
    }
  }

  trackState(trackIndexValue: number): RuntimeTrackStateV1 | null {
    const trackIndex = validateTrackIndex(trackIndexValue, "queryTrackState");
    const track = this.#tracks.get(trackIndex);
    if (track === undefined || track.current === null) return null;
    const entry = track.current;
    return {
      trackIndex,
      animationId: entry.animation?.id ?? null,
      animationTimeSeconds: poseTime(entry),
      animationDurationSeconds: entryRangeDurationV1(entry),
      animationRangeStartSeconds: entry.animationRangeStart,
      animationRangeEndSeconds: entry.animationRangeEnd,
      playbackTimeSeconds: entry.playbackTime,
      startDelaySeconds: entry.startDelay,
      playbackEndSeconds: entry.playbackEnd,
      playbackRate: entry.playbackRate,
      blendWeight: entry.blendWeight,
      looping: entry.looping,
      blend: entry.blend,
      blendDurationSeconds: entry.blendDuration,
      blendElapsedSeconds: entry.blendElapsed,
      blendProgress: blendProgress(entry),
      eventBlendThreshold: entry.eventBlendThreshold,
      attachmentBlendThreshold: entry.attachmentBlendThreshold,
      drawOrderBlendThreshold: entry.drawOrderBlendThreshold,
      holdPreviousPose: entry.holdPreviousPose,
      queuedCount: track.queue.length,
    };
  }

  queuedEntries(trackIndexValue: number): RuntimeQueuedAnimationPlaybackV1[] {
    const trackIndex = validateTrackIndex(trackIndexValue, "queryQueuedEntries");
    return (this.#tracks.get(trackIndex)?.queue ?? []).map((entry, queueIndex) => ({
      trackIndex,
      queueIndex,
      animationId: entry.animation?.id ?? null,
      startDelaySeconds: entry.startDelay,
      blendDurationSeconds: entry.blendDuration,
      looping: entry.looping,
    }));
  }

  trackCount(): number {
    if (this.#tracks.size === 0) return 0;
    return Math.max(...this.#tracks.keys()) + 1;
  }

  advanceTime(
    deltaSeconds: number,
    maxEmittedEvents = MAX_EVENTS_PER_OPERATION,
    reuseOutput = false,
  ): RuntimeEventV1[] {
    const delta = nonNegative(deltaSeconds, "advanceTime", "deltaSeconds");
    if (!Number.isInteger(maxEmittedEvents) || maxEmittedEvents < 0 || maxEmittedEvents > MAX_EVENTS_PER_OPERATION) {
      throw new RuntimeErrorV1("invalidArgument", "advanceTime", "maxEmittedEvents is outside the supported range.", {
        field: "maxEmittedEvents",
      });
    }
    if (!this.hasActiveEntries()) {
      const nextTime = f32Add(this.#detachedTime, delta);
      if (!Number.isFinite(nextTime)) {
        throw new RuntimeErrorV1("nonFinite", "advanceTime", "The detached animation clock exceeded binary32 range.", {
          field: "deltaSeconds",
        });
      }
      this.#detachedTime = Math.max(0, nextTime);
      if (reuseOutput) return this.#eventCollectorScratch.reset(maxEmittedEvents).finish(true);
      return [];
    }
    const collector = reuseOutput
      ? this.#eventCollectorScratch.reset(maxEmittedEvents)
      : new EventCollectorV1(maxEmittedEvents);
    const trackIndices = this.#sortedTrackIndices();
    for (let index = 0; index < trackIndices.length; index += 1) {
      const trackIndex = trackIndices[index];
      if (trackIndex === undefined) continue;
      const track = this.#tracks.get(trackIndex);
      if (track !== undefined) this.#advanceTrack(trackIndex, track, delta, collector);
    }
    return collector.finish(reuseOutput);
  }

  poseTracks(sampling: RuntimeSamplingV1 = AUTHORED_SAMPLING_V1): RuntimePoseTrackV1[] {
    const plan = samplingPlanV1(sampling), result: RuntimePoseTrackV1[] = [];
    for (const index of this.#sortedTrackIndices()) {
      let entry = this.#tracks.get(index)?.current ?? null;
      const path: RuntimePoseEndpointV1[] = [];
      let attachmentsAllowed = true, drawOrderAllowed = true;
      while (entry !== null) {
        const progress = blendProgress(entry);
        let time = poseTime(entry);
        if (plan.frameStepSeconds !== null) time = quantizeSampleTimeV1(time, plan.frameStepSeconds);
        path.push({progress, holdPreviousPose:entry.holdPreviousPose, layer:entry.animation === null ? null : {
          animation:entry.animation, sampleTime:time, alpha:entry.blendWeight, blend:entry.blend,
          forceStepped:plan.forceStepped, attachmentsAllowed, drawOrderAllowed,
        }});
        if (progress >= 1 || entry.blendSource === null) break;
        entry = entry.blendSource;
        attachmentsAllowed = progress < entry.attachmentBlendThreshold;
        drawOrderAllowed = progress < entry.drawOrderBlendThreshold;
      }
      path.reverse(); result.push(path);
    }
    return result;
  }

  layers(sampling: RuntimeSamplingV1 = AUTHORED_SAMPLING_V1, _reuseOutput = false): RuntimeAnimationLayerV1[] {
    const result: RuntimeAnimationLayerV1[] = [];
    for (const track of this.poseTracks(sampling)) for (let i=0;i<track.length;i+=1) {
      const endpoint=track[i]!;
      if(endpoint.layer!==null && (i===0 || endpoint.progress!==0)) result.push(endpoint.layer);
    }
    return result;
  }

  #advanceTrack(
    trackIndex: number,
    track: TrackV1,
    delta: number,
    collector: EventCollectorV1,
  ): void {
    let remaining = delta;
    let wallBase = 0;
    let promotions = 0;
    while (track.current !== null) {
      const queued = track.queue[0];
      const current = track.current;
      let boundary: number | null = null;
      let boundaryKind: "promote" | "playbackEnded" | null = null;
      const promotion = queued === undefined ? null : secondsUntilTrackTimeV1(current, queued.startDelay);
      const end = secondsUntilTrackTimeV1(current, current.playbackEnd);
      if (promotion !== null && (end === null || promotion <= end)) {
        if (promotion <= remaining) {
          boundary = Math.max(0, promotion);
          boundaryKind = "promote";
        }
      } else if (end !== null && end <= remaining) {
        boundary = Math.max(0, end);
        boundaryKind = "playbackEnded";
      }
      if (boundary === null) {
        this.#advanceEntryTree(current, remaining, wallBase, trackIndex, collector, null);
        break;
      }
      if (boundary > 0) {
        this.#advanceEntryTree(current, f32(boundary), wallBase, trackIndex, collector, null);
        remaining = f32(Math.max(0, remaining - boundary));
        wallBase += boundary;
      }
      if (boundaryKind === "promote") this.#promoteQueue(trackIndex, track, collector, wallBase);
      else this.#endTrack(trackIndex, track, collector, wallBase);
      promotions += 1;
      if (promotions > MAX_EVENTS_PER_OPERATION) {
        throw new RuntimeErrorV1("resourceLimit", "advanceTime", "Queue promotion limit exceeded.");
      }
      if (remaining === 0 && track.current !== null) {
        const next = track.queue[0];
        const immediatePromotion = next !== undefined && next.startDelay <= track.current.playbackTime;
        const immediateEnd = track.current.playbackEnd <= track.current.playbackTime;
        if (!immediatePromotion && !immediateEnd) break;
      }
    }
  }

  #advanceEntryTree(
    entry: AnimationPlaybackV1,
    delta: number,
    wallBase: number,
    trackIndex: number,
    collector: EventCollectorV1,
    controller: OutgoingControllerV1 | null,
  ): void {
    const frames: {entry:AnimationPlaybackV1;delta:number;controller:OutgoingControllerV1|null;completion:number|null;hadSource:boolean}[]=[];
    let node=entry,step=delta,parent=controller;
    for(;;){
      const start=node.blendElapsed,hasSource=node.blendSource!==null;
      const remaining=Math.max(0,node.blendDuration-start),outgoing=Math.min(step,remaining);
      frames.push({entry:node,delta:step,controller:parent,completion:hasSource && remaining<=step?remaining:null,hadSource:hasSource});
      if(node.blendSource===null || !(outgoing>0 || remaining===0))break;
      parent={entry:node,blendElapsedAtStart:start};node=node.blendSource;step=f32(outgoing);
    }
    for(let i=frames.length;i-->0;){
      const frame=frames[i]!,current=frame.entry;
      if(current.blendSource!==null && frame.completion!==null){
        const removed=current.blendSource;current.blendSource=null;
        const notices:RuntimeEventV1[]=[];disposeStartedChain(removed,trackIndex,notices);
        for(const event of notices)collector.push(event,wallBase+frame.completion,trackIndex);
      }
      const rawStart=current.playbackTime,scaled=f32Mul(frame.delta,current.playbackRate);
      const rawEnd=current.looping?f32Add(rawStart,scaled):f32(Math.max(0,f32Add(rawStart,scaled)));
      collectCrossings(current,rawStart,rawEnd,frame.delta,wallBase,trackIndex,collector,frame.controller);
      current.playbackTime=rawEnd;
      current.blendElapsed=f32(Math.max(0,f32Add(current.blendElapsed,frame.delta)));
    }
  }

  #promoteQueue(trackIndex: number, track: TrackV1, collector: EventCollectorV1, wallOffset: number): void {
    const queued = track.queue.shift();
    const outgoing = track.current;
    if (queued === undefined || outgoing === null) return;
    collector.push(lifecycle(trackIndex, animationIdV1(outgoing), "playbackInterrupted"), wallOffset, trackIndex);
    const incoming = queued.animation === null
      ? createEmptyEntry(queued.blendDuration)
      : createEntry(queued.animation, queued.looping, queued.blendDuration);
    incoming.startDelay = queued.startDelay;
    if (queued.blendDuration === 0) {
      const lifecycleEvents: RuntimeEventV1[] = [];
      disposeStartedChain(outgoing, trackIndex, lifecycleEvents);
      for (const event of lifecycleEvents) collector.push(event, wallOffset, trackIndex);
    } else {
      incoming.blendSource = outgoing;
    }
    track.current = incoming;
    collector.push(lifecycle(trackIndex, animationIdV1(incoming), "playbackStarted"), wallOffset, trackIndex);
  }

  #endTrack(trackIndex: number, track: TrackV1, collector: EventCollectorV1, wallOffset: number): void {
    if (track.current !== null) {
      const lifecycleEvents: RuntimeEventV1[] = [];
      disposeStartedChain(track.current, trackIndex, lifecycleEvents);
      for (const event of lifecycleEvents) collector.push(event, wallOffset, trackIndex);
      track.current = null;
    }
    for (const queued of track.queue) {
      collector.push(lifecycle(trackIndex, queued.animation?.id ?? null, "playbackRemoved"), wallOffset, trackIndex);
    }
    track.queue.length = 0;
  }

  #track(index: number): TrackV1 {
    let track = this.#tracks.get(index);
    if (track === undefined) {
      track = { current: null, queue: [] };
      this.#tracks.set(index, track);
    }
    return track;
  }

  #requireCurrent(index: number, operation: string): AnimationPlaybackV1 {
    const entry = this.#tracks.get(index)?.current;
    if (entry === undefined || entry === null) {
      throw new RuntimeErrorV1("invalidState", operation, `Track ${index} has no current animation.`, {
        field: "trackIndex",
      });
    }
    return entry;
  }

  #lookupAnimation(animationId: string, operation: string): RuntimeAnimationV1 {
    if (typeof animationId !== "string" || animationId.length === 0) invalid(operation, "animationId", "animationId must be non-empty.");
    try {
      return this.#data.animation(animationId);
    } catch (error) {
      if (error instanceof RuntimeErrorV1) {
        throw new RuntimeErrorV1("notFound", operation, `Unknown animation '${animationId}'.`, {
          field: "animationId",
          entityId: animationId,
        });
      }
      throw error;
    }
  }

  #blendDuration(from: string, to: string): number {
    return this.#pairMixes.get(pairKey(from, to)) ?? this.#defaultMix;
  }

  #setPairMix(key: string, duration: number): void {
    if (!this.#pairMixes.has(key)) this.#pairMixKeys.push(key);
    this.#pairMixes.set(key, duration);
  }

  #sortedTrackIndices(): readonly number[] {
    if (this.#sortedTrackIndicesCache.length !== this.#tracks.size) {
      this.#sortedTrackIndicesCache.length = 0;
      for (const trackIndex of this.#tracks.keys()) this.#sortedTrackIndicesCache.push(trackIndex);
      this.#sortedTrackIndicesCache.sort((left, right) => left - right);
    }
    return this.#sortedTrackIndicesCache;
  }
}

function createEntry(animation: RuntimeAnimationV1, looping: boolean, blendDuration: number): AnimationPlaybackV1 {
  return {
    animation,
    animationRangeStart: 0,
    animationRangeEnd: animation.duration,
    playbackTime: 0,
    startDelay: 0,
    playbackEnd: F32_MAX,
    playbackRate: 1,
    looping,
    blendWeight: 1,
    blend: "replace",
    blendDuration,
    blendElapsed: 0,
    eventBlendThreshold: 0,
    attachmentBlendThreshold: 0,
    drawOrderBlendThreshold: 0,
    holdPreviousPose: false,
    eventCursorInitialized: false,
    blendSource: null,
  };
}

function createEmptyEntry(blendDuration: number): AnimationPlaybackV1 {
  return {
    animation: null,
    animationRangeStart: 0,
    animationRangeEnd: 0,
    playbackTime: 0,
    startDelay: 0,
    playbackEnd: blendDuration,
    playbackRate: 1,
    looping: false,
    blendWeight: 1,
    blend: "replace",
    blendDuration,
    blendElapsed: 0,
    eventBlendThreshold: 0,
    attachmentBlendThreshold: 0,
    drawOrderBlendThreshold: 0,
    holdPreviousPose: false,
    eventCursorInitialized: true,
    blendSource: null,
  };
}

function cloneEntry(entry: AnimationPlaybackV1 | null): AnimationPlaybackV1 | null {
  if (entry === null) return null;
  const result={...entry,blendSource:null};let output:AnimationPlaybackV1=result;
  for(let input=entry.blendSource;input!==null;input=input.blendSource){output.blendSource={...input,blendSource:null};output=output.blendSource;}
  return result;
}

function copyEntryIntoV1(target: AnimationPlaybackV1 | null, source: AnimationPlaybackV1 | null): AnimationPlaybackV1 | null {
  if(source===null)return null;
  const result=target ?? createEmptyEntry(0);let output=result;let input:AnimationPlaybackV1|null=source;
  while(input!==null){
    const next:AnimationPlaybackV1|null=output.blendSource;Object.assign(output,input);const previous:AnimationPlaybackV1|null=input.blendSource;
    output.blendSource=previous===null?null:next ?? createEmptyEntry(0);
    if(output.blendSource!==null)output=output.blendSource;
    input=previous;
  }
  return result;
}

function reconcileEntryV1(entry:AnimationPlaybackV1|null,data:RuntimeDataV1):AnimationPlaybackV1|null {
  let result:AnimationPlaybackV1|null=null,tail:AnimationPlaybackV1|null=null;
  for(let current=entry;current!==null;current=current.blendSource){
    const next:AnimationPlaybackV1={...current,blendSource:null};
    if(next.animation!==null){
      const animation=findAnimationV1(data,next.animation.id);if(animation===null)break;
      const currentTime=poseTime(next),full=next.animationRangeStart===0 && next.animationRangeEnd===next.animation.duration;
      next.animation=animation;const duration=Math.max(0,animation.duration);
      next.animationRangeStart=full?0:Math.min(Math.max(next.animationRangeStart,0),duration);
      next.animationRangeEnd=full?duration:Math.min(Math.max(next.animationRangeEnd,0),duration);
      if(!(next.animationRangeEnd>next.animationRangeStart)){next.animationRangeStart=0;next.animationRangeEnd=duration;}
      next.playbackTime=f32(Math.max(0,Math.min(Math.max(currentTime,next.animationRangeStart),next.animationRangeEnd)-next.animationRangeStart));
    }
    if(tail===null)result=next;else tail.blendSource=next;tail=next;
  }
  return result;
}

function blendProgress(entry: AnimationPlaybackV1): number {
  if (entry.blendSource === null) return 1;
  if (entry.blendDuration === 0) return 1;
  return f32(Math.min(Math.max(entry.blendElapsed / entry.blendDuration, 0), 1));
}

function animationIdV1(entry: AnimationPlaybackV1): string | null {
  return entry.animation?.id ?? null;
}

function entryRangeDurationV1(entry: AnimationPlaybackV1): number {
  return f32(Math.max(0, entry.animationRangeEnd - entry.animationRangeStart));
}

function queuedRangeDurationV1(entry: QueuedEntryV1): number {
  return entry.animation?.duration ?? 0;
}

function secondsUntilTrackTimeV1(entry: AnimationPlaybackV1, targetTrackTime: number): number | null {
  const remaining = targetTrackTime - entry.playbackTime;
  if (remaining <= 0) return 0;
  if (!(entry.playbackRate > 0)) return null;
  const seconds = remaining / entry.playbackRate;
  return Number.isFinite(seconds) ? seconds : null;
}

function poseTime(entry: AnimationPlaybackV1): number {
  const duration = entryRangeDurationV1(entry);
  if (duration === 0) return entry.animationRangeStart;
  const local = entry.looping
    ? (entry.playbackTime % duration < 0 ? f32Add(entry.playbackTime % duration, duration) : entry.playbackTime % duration)
    : Math.min(Math.max(entry.playbackTime, 0), duration);
  return f32Add(entry.animationRangeStart, local);
}

function collectCrossings(
  entry: AnimationPlaybackV1,
  rawStart: number,
  rawEnd: number,
  wallDelta: number,
  wallBase: number,
  trackIndex: number,
  collector: EventCollectorV1,
  controller: OutgoingControllerV1 | null,
): void {
  const animation = entry.animation;
  if (animation === null) return;
  if (rawStart === rawEnd || wallDelta === 0) return;
  const forward = rawEnd > rawStart;
  if (!entry.eventCursorInitialized) {
    entry.eventCursorInitialized = true;
    const initial=poseTime({...entry,playbackTime:rawStart});
    for (const key of animation.events) {
      if (key.time !== initial) continue;
      pushUserEvent(entry, key, wallBase, trackIndex, collector, controller, 0, wallDelta);
    }
  }
  const duration = entryRangeDurationV1(entry);
  if (duration === 0) return;
  if (entry.looping) collectLoopingCrossings(entry, rawStart, rawEnd, wallDelta, wallBase, trackIndex, collector, controller, forward);
  else collectNonLoopingCrossings(entry, rawStart, rawEnd, wallDelta, wallBase, trackIndex, collector, controller, forward);
}

interface CrossingCandidateV1 {
  readonly raw: number;
  readonly cycle?: number;
  readonly offset?: number;
  readonly period?: number;
  readonly phase: number;
  readonly declaration: number;
  readonly event: RuntimeEventKeyV1 | null;
}

function compareForwardCrossingV1(left: CrossingCandidateV1, right: CrossingCandidateV1): number {
  const rawOrder = (left.cycle ?? 0) - (right.cycle ?? 0) || (left.offset ?? left.raw) - (right.offset ?? right.raw);
  if (rawOrder !== 0) return rawOrder;
  if (left.phase !== right.phase) return left.phase - right.phase;
  return left.declaration - right.declaration;
}

function compareReverseCrossingV1(left: CrossingCandidateV1, right: CrossingCandidateV1): number {
  const rawOrder = (right.cycle ?? 0) - (left.cycle ?? 0) || (right.offset ?? right.raw) - (left.offset ?? left.raw);
  if (rawOrder !== 0) return rawOrder;
  if (left.phase !== right.phase) return left.phase - right.phase;
  return right.declaration - left.declaration;
}

function collectLoopingCrossings(
  entry: AnimationPlaybackV1,
  rawStart: number,
  rawEnd: number,
  wallDelta: number,
  wallBase: number,
  trackIndex: number,
  collector: EventCollectorV1,
  controller: OutgoingControllerV1 | null,
  forward: boolean,
): void {
  const animation = entry.animation;
  if (animation === null) return;
  const duration = entryRangeDurationV1(entry);
  const base:EventBounds={low:Math.min(rawStart,rawEnd),high:Math.max(rawStart,rawEnd),includeLow:!forward,includeHigh:forward};
  let timeline:EventBounds|null={...base};
  if(controller!==null){
    const remaining=entry.eventBlendThreshold*controller.entry.blendDuration-controller.blendElapsedAtStart;
    if(controller.entry.blendDuration===0 || remaining<=0)timeline=null;
    else if(remaining<=wallDelta){const threshold=rawStart+wallDelta*entry.playbackRate*(remaining/wallDelta);
      if(forward){if(threshold<=base.low)timeline=null;else if(threshold<=base.high){timeline.high=threshold;timeline.includeHigh=false;}}
      else {if(threshold>=base.high)timeline=null;else if(threshold>=base.low){timeline.low=threshold;timeline.includeLow=false;}}
    }
  }
  const candidates=collector.crossingCandidates();
  const add=(bounds:EventBounds|null,keyOffset:number,phase:number,declaration:number,event:RuntimeEventKeyV1|null)=>{
    if(bounds===null)return;const indices=eventIntegerBounds(bounds,keyOffset,duration);if(indices===null)return;
    const count=indices[1]-indices[0]+1;
    if(!Number.isFinite(count)||count+candidates.length>collector.capacity)throw new RuntimeErrorV1("resourceLimit","advanceTime","Event boundary limit exceeded.",{field:"deltaSeconds"});
    for(let n=0;n<count;n++){let cycle=indices[0]+n,offset=keyOffset;if(offset===duration){cycle++;offset=0;}
      candidates.push({cycle,offset,period:duration,raw:0,phase,declaration,event});}
  };
  add(base,0,1,0,null);
  for(let declaration=0;declaration<animation.events.length;declaration++){
    const key=animation.events[declaration]!;if(key.time<entry.animationRangeStart||key.time>entry.animationRangeEnd)continue;
    const atZero=key.time===entry.animationRangeStart,atEnd=key.time===entry.animationRangeEnd;
    add(timeline,f32(key.time-entry.animationRangeStart),forward?(atZero?2:0):(atEnd?2:0),declaration,key);
  }
  if (candidates.length > 1) candidates.sort(forward ? compareForwardCrossingV1 : compareReverseCrossingV1);
  emitCandidates(entry, candidates, rawStart, rawEnd, wallDelta, wallBase, trackIndex, collector, controller);
}

function collectNonLoopingCrossings(
  entry: AnimationPlaybackV1,
  rawStart: number,
  rawEnd: number,
  wallDelta: number,
  wallBase: number,
  trackIndex: number,
  collector: EventCollectorV1,
  controller: OutgoingControllerV1 | null,
  forward: boolean,
): void {
  const animation = entry.animation;
  if (animation === null) return;
  const candidates = collector.crossingCandidates();
  for (let declaration = 0; declaration < animation.events.length; declaration += 1) {
    const key = animation.events[declaration];
    if (key !== undefined && key.time >= entry.animationRangeStart && key.time <= entry.animationRangeEnd) {
      const crossing = f32(key.time - entry.animationRangeStart);
      if (ownsRawCrossing(crossing, rawStart, rawEnd, forward)) {
        candidates.push({ raw: crossing, phase: 0, declaration, event: key });
      }
    }
  }
  const duration = entryRangeDurationV1(entry);
  if (forward && rawStart < duration && rawEnd >= duration) {
    candidates.push({ raw: duration, phase: 1, declaration: 0, event: null });
  } else if (!forward && rawStart > 0 && rawEnd <= 0) {
    candidates.push({ raw: 0, phase: 1, declaration: 0, event: null });
  }
  if (candidates.length > 1) candidates.sort(forward ? compareForwardCrossingV1 : compareReverseCrossingV1);
  emitCandidates(entry, candidates, rawStart, rawEnd, wallDelta, wallBase, trackIndex, collector, controller);
}

function emitCandidates(
  entry: AnimationPlaybackV1,
  candidates: readonly CrossingCandidateV1[],
  rawStart: number,
  rawEnd: number,
  wallDelta: number,
  wallBase: number,
  trackIndex: number,
  collector: EventCollectorV1,
  controller: OutgoingControllerV1 | null,
): void {
  const animation = entry.animation;
  if (animation === null) return;
  for (const candidate of candidates) {
    const relative = candidate.period === undefined ? candidate.raw - rawStart : eventRelativePosition(candidate.cycle!,candidate.offset!,candidate.period,rawStart);
    const requested = wallDelta * entry.playbackRate;
    const wallOffset = wallBase + Math.min(1,Math.max(0,requested===0?0:relative/requested))*wallDelta;
    if (candidate.event === null) {
      collector.push(lifecycle(trackIndex, animation.id, "playbackCompleted"), wallOffset, trackIndex);
    } else {
      pushUserEvent(entry, candidate.event, wallOffset, trackIndex, collector, controller, wallOffset - wallBase, wallDelta);
    }
  }
}

function pushUserEvent(
  entry: AnimationPlaybackV1,
  key: RuntimeEventKeyV1,
  wallOffset: number,
  trackIndex: number,
  collector: EventCollectorV1,
  controller: OutgoingControllerV1 | null,
  localWallOffset: number,
  segmentDelta: number,
): void {
  const animation = entry.animation;
  if (animation === null) return;
  if (controller !== null) {
    const crossingMixTime = controller.blendElapsedAtStart + (segmentDelta === 0 ? 0 : localWallOffset);
    const progress = controller.entry.blendDuration === 0
      ? 1
      : Math.min(Math.max(crossingMixTime / controller.entry.blendDuration, 0), 1);
    if (!(progress < entry.eventBlendThreshold)) return;
  }
  collector.push({
    trackIndex,
    animationId: animation.id,
    kind: "customEvent",
    timelineTimeSeconds: key.time,
    eventId: key.eventId,
    name: key.name,
    integerValue: key.integerValue,
    stringValue: key.stringValue,
    numberValue: key.numberValue,
    audioId: key.audioId,
    volume: key.volume,
    balance: key.balance,
  }, wallOffset, trackIndex);
}

function ownsRawCrossing(crossing: number, start: number, end: number, forward: boolean): boolean {
  return forward ? crossing > start && crossing <= end : crossing >= end && crossing < start;
}

function comparePendingEventV1(left: PendingEventV1, right: PendingEventV1): number {
  return left.wallOffset - right.wallOffset || left.trackIndex - right.trackIndex || left.ordinal - right.ordinal;
}

class EventCollectorV1 {
  readonly #events: PendingEventV1[] = [];
  readonly #output: RuntimeEventV1[] = [];
  readonly #crossingCandidates: CrossingCandidateV1[] = [];
  #limit: number;
  #ordinal = 0;

  constructor(limit: number) {
    this.#limit = limit;
  }

  reset(limit: number): EventCollectorV1 {
    this.#events.length = 0;
    this.#output.length = 0;
    this.#crossingCandidates.length = 0;
    this.#limit = limit;
    this.#ordinal = 0;
    return this;
  }

  get capacity(): number {
    return this.#limit;
  }

  crossingCandidates(): CrossingCandidateV1[] {
    this.#crossingCandidates.length = 0;
    return this.#crossingCandidates;
  }

  push(event: RuntimeEventV1, wallOffset: number, trackIndex: number): void {
    if (this.#events.length >= this.#limit) {
      throw new RuntimeErrorV1("resourceLimit", "advanceTime", "One operation would emit more than 1,000,000 events.");
    }
    this.#events.push({ wallOffset, trackIndex, ordinal: this.#ordinal, event });
    this.#ordinal += 1;
  }

  finish(reuseOutput = false): RuntimeEventV1[] {
    if (this.#events.length > 1) this.#events.sort(comparePendingEventV1);
    if (!reuseOutput) return this.#events.map((item) => item.event);
    this.#output.length = this.#events.length;
    for (let index = 0; index < this.#events.length; index += 1) {
      const item = this.#events[index];
      if (item !== undefined) this.#output[index] = item.event;
    }
    return this.#output;
  }
}

function lifecycle(
  trackIndex: number,
  animationId: string | null,
  kind: "playbackStarted" | "playbackInterrupted" | "playbackEnded" | "playbackRemoved" | "playbackCompleted",
): RuntimeEventV1 {
  return {
    trackIndex,
    animationId,
    kind,
    timelineTimeSeconds: null,
    eventId: null,
    name: null,
    integerValue: null,
    stringValue: null,
    numberValue: null,
    audioId: null,
    volume: 1,
    balance: 0,
  };
}

function disposeStartedChain(entry: AnimationPlaybackV1, trackIndex: number, events: RuntimeEventV1[]): void {
  let cursor: AnimationPlaybackV1 | null = entry;
  while (cursor !== null) {
    events.push(lifecycle(trackIndex, animationIdV1(cursor), "playbackEnded"));
    events.push(lifecycle(trackIndex, animationIdV1(cursor), "playbackRemoved"));
    cursor = cursor.blendSource;
  }
}

const PROPERTY_CACHE = new WeakMap<RuntimeAnimationV1, ReadonlySet<string>>();



function hasTimelineKeysV1(value: readonly unknown[] | null): boolean {
  return value !== null && value.length > 0;
}



function pairKey(from: string, to: string): string {
  return `${from.length}:${from}${to}`;
}

function parsePairKeyV1(key: string): readonly [string, string] {
  const separator = key.indexOf(":");
  const length = Number(key.slice(0, separator));
  const start = separator + 1;
  return [key.slice(start, start + length), key.slice(start + length)];
}

function findAnimationV1(data: RuntimeDataV1, animationId: string): RuntimeAnimationV1 | null {
  return data.document.animations.find((animation) => animation.id === animationId) ?? null;
}

function hasAnimationV1(data: RuntimeDataV1, animationId: string): boolean {
  return findAnimationV1(data, animationId) !== null;
}

function validateTrackIndex(value: number, operation: string): number {
  if (!Number.isInteger(value) || value < 0 || value > MAX_TRACK_INDEX) {
    invalid(operation, "trackIndex", `trackIndex must be an integer in [0,${MAX_TRACK_INDEX}].`);
  }
  return value;
}

function validateQueueIndex(value: number, operation: string): number {
  if (!Number.isInteger(value) || value < 0 || value > 0xffff_ffff) {
    invalid(operation, "queueIndex", "queueIndex must be a u32.");
  }
  return value;
}

function queuedNotFound(operation: string, queueIndex: number): never {
  throw new RuntimeErrorV1("notFound", operation, `No queued entry exists at index ${queueIndex}.`, {
    field: "queueIndex",
  });
}

function finite(value: number, operation: string, field: string): number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new RuntimeErrorV1("nonFinite", operation, `${field} must be finite.`, { field });
  }
  try {
    return finiteF32(value);
  } catch (error) {
    throw new RuntimeErrorV1("nonFinite", operation, `${field} is outside binary32 range.`, { field, cause: error });
  }
}

function nonNegative(value: number, operation: string, field: string): number {
  const result = finite(value, operation, field);
  if (result < 0) invalid(operation, field, `${field} must be non-negative.`);
  return result;
}

function positive(value: number, operation: string, field: string): number {
  const result = finite(value, operation, field);
  if (result <= 0) invalid(operation, field, `${field} must be positive.`);
  return result;
}

function unit(value: number, operation: string, field: string): number {
  const result = finite(value, operation, field);
  if (result < 0 || result > 1) invalid(operation, field, `${field} must be in [0,1].`);
  return result;
}

function invalid(operation: string, field: string, message: string): never {
  throw new RuntimeErrorV1("invalidArgument", operation, message, { field });
}

export function samplingPlanV1(sampling: RuntimeSamplingV1): RuntimeSamplingPlanV1 {
  if (sampling === null || typeof sampling !== "object") invalid("apply", "sampling", "sampling must be an object.");
  if (sampling.mode === "authored") return AUTHORED_PLAN_V1;
  if (sampling.mode === "forceStepped") return FORCE_STEPPED_PLAN_V1;
  if (sampling.mode === "fixedFrame" || sampling.mode === "fixedFrameStepped") {
    const cached = FIXED_SAMPLING_PLAN_CACHE.get(sampling);
    if (cached !== undefined) return cached;
    const created: RuntimeSamplingPlanV1 = {
      forceStepped: sampling.mode === "fixedFrameStepped",
      frameStepSeconds: positive(sampling.frameStepSeconds, "apply", "frameStepSeconds"),
    };
    FIXED_SAMPLING_PLAN_CACHE.set(sampling, created);
    return created;
  }
  invalid("apply", "sampling.mode", "Unknown sampling mode.");
}
