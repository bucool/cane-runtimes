import { sampleAnimationTracksV1, applyEndpointLayerV1 } from "./endpoint-pose.js";
import { sampleSequenceIndex } from "./sequence-clock.js";
import type {
  BoneTransformModeV1,
  RuntimeAnimationV1,
  RuntimeAttachmentTimelineV1,
  RuntimeAttachmentV1,
  RuntimeBoneTimelineV1,
  RuntimeBoneV1,
  RuntimeCurvePropertyNameV1,
  RuntimeCurveV1,
  RuntimeConstraintTimelineV1,
  RuntimeConstraintDiagnosticV1,
  RuntimeConstraintV1,
  RuntimeRegionAttachmentV1,
  RuntimeSlotColorKeyV1,
  RuntimeSkinV1,
  RuntimeSlotTimelineV1,
  RuntimeSlotV1,
  RuntimeTrackBlendV1,
} from "../contracts.js";
import type { RuntimeDataV1 } from "../data.js";
import { f32, f32Add, f32Mul, f32Sub } from "../math/f32.js";
import {
  copyRuntimeSlotTintBytesV1,
  mutableRuntimeSlotTintBytesV1,
  resetRuntimeSlotTintBytesV1,
} from "../render/tint.js";
import {
  NO_CONTRIBUTION_V1,
  sampleContinuousArrayKeysV1,
  sampleContinuousComponentArrayKeysV1,
  sampleContinuousKeysV1,
  sampleDiscreteKeysV1,
  selectPropertyCurveV1,
  wrapDegreesV1,
} from "./curves.js";

export interface RuntimeAnimationLayerV1 {
  readonly animation: RuntimeAnimationV1;
  readonly sampleTime: number;
  readonly alpha: number;
  readonly blend: RuntimeTrackBlendV1;
  readonly forceStepped: boolean;
  readonly attachmentsAllowed: boolean;
  readonly drawOrderAllowed: boolean;
  readonly unboundedAlpha?: boolean | undefined;
  readonly propertyAlpha?: ((propertyId: string, baseAlpha: number) => number) | undefined;
}

export interface RuntimeSampledPoseV1 {
  readonly bones: readonly RuntimeBoneV1[];
  readonly slots: readonly RuntimeSlotV1[];
  readonly regions: ReadonlyMap<string, RuntimeRegionAttachmentV1>;
  readonly deforms: ReadonlyMap<string, RuntimeSampledDeformV1>;
  readonly sequenceIndices: ReadonlyMap<string, number>;
  readonly slotAttachmentKeys: ReadonlyMap<string, string | null>;
  readonly sampledSkinIds: readonly string[];
  readonly constraints: readonly RuntimeConstraintV1[];
  readonly drawOrderSlotIds: readonly string[];
  readonly drawOrderSampled: boolean;
  readonly sampledSliderTimes: ReadonlyMap<string, number>;
  readonly constraintDiagnostics: ReadonlyMap<string, RuntimeConstraintDiagnosticV1>;
}

export interface RuntimeSampledDeformV1 {
  readonly weighted: boolean;
  readonly values: readonly number[];
}

type Mutable<T> = { -readonly [Property in keyof T]: T[Property] };
type MutableConstraintV1 = Mutable<RuntimeConstraintV1>;
type MutableSampledPoseV1 = { -readonly [Property in keyof RuntimeSampledPoseV1]: RuntimeSampledPoseV1[Property] };

interface MutableSamplingStateV1 {
  readonly setupBones: ReadonlyMap<string, RuntimeBoneV1>;
  readonly bones: Map<string, Mutable<RuntimeBoneV1>>;
  readonly setupSlots: ReadonlyMap<string, RuntimeSlotV1>;
  readonly slots: Map<string, Mutable<RuntimeSlotV1>>;
  readonly setupRegions: ReadonlyMap<string, RuntimeRegionAttachmentV1>;
  readonly regions: Map<string, Mutable<RuntimeRegionAttachmentV1>>;
  readonly attachmentById: ReadonlyMap<string, RuntimeAttachmentV1>;
  readonly deforms: Map<string, RuntimeSampledDeformV1>;
  readonly deformPool: Map<string, MutableSampledDeformV1>;
  readonly deformScratchByTimeline: WeakMap<object, RuntimeDeformSamplingScratchV1>;
  readonly deformEpochByOwner: Map<string, number>;
  deformEpoch: number;
  readonly sequenceIndices: Map<string, number>;
  readonly setupConstraints: ReadonlyMap<string, RuntimeConstraintV1>;
  readonly constraints: Map<string, MutableConstraintV1>;
  readonly setupOrder: readonly string[];
  drawOrder: string[];
  drawOrderSampled: boolean;
  sampledSkinIds: string[];
  readonly sampledSliderTimes: Map<string, number>;
  readonly constraintDiagnostics: Map<string, RuntimeConstraintDiagnosticV1>;
}

interface SamplingDataCacheV1 {
  readonly setupBones: ReadonlyMap<string, RuntimeBoneV1>;
  readonly setupSlots: ReadonlyMap<string, RuntimeSlotV1>;
  readonly regionAttachments: readonly RuntimeRegionAttachmentV1[];
  readonly setupRegions: ReadonlyMap<string, RuntimeRegionAttachmentV1>;
  readonly setupConstraints: ReadonlyMap<string, RuntimeConstraintV1>;
  readonly setupOrder: readonly string[];
  readonly setupSequenceIndices: ReadonlyMap<string, number>;
  readonly setupSequenceIds: readonly string[];
  readonly setupSequenceValues: readonly number[];
  readonly attachmentById: ReadonlyMap<string, RuntimeAttachmentV1>;
}

interface MutableSampledDeformV1 {
  weighted: boolean;
  readonly values: number[];
}

interface RuntimeDeformSamplingScratchV1 {
  readonly sampledValues: number[];
  readonly canonicalValues: number[];
}

export interface RuntimeSamplingScratchV1 {
  readonly runtimeSamplingScratchV1: true;
}

interface SamplingScratchStorageV1 {
  state: MutableSamplingStateV1 | null;
  pose: MutableSampledPoseV1 | null;
  readonly skinAttachmentResolution: SkinAttachmentResolutionScratchV1;
}

interface SkinAttachmentResolutionScratchV1 {
  readonly skinIds: string[];
  readonly bySlotId: Map<string, Map<string, string | null>>;
}

const SAMPLING_DATA_CACHE = new WeakMap<RuntimeDataV1, SamplingDataCacheV1>();
const PRESENT_NUMBER_KEY_CACHE = new WeakMap<object, Map<PropertyKey, readonly unknown[]>>();
const PRESENT_BOOLEAN_KEY_CACHE = new WeakMap<object, Map<PropertyKey, readonly unknown[]>>();
const SAMPLING_SCRATCH_STORAGE = new WeakMap<RuntimeSamplingScratchV1, SamplingScratchStorageV1>();
const FOLDER_CONTROLLED_SET_CACHE = new WeakMap<object, ReadonlySet<string>>();
const CONTINUOUS_OPTIONS = Object.freeze({});
const CONTINUOUS_STEPPED_OPTIONS = Object.freeze({ forceStepped: true });
const ANGULAR_OPTIONS = Object.freeze({ angular: true });
const ANGULAR_STEPPED_OPTIONS = Object.freeze({ angular: true, forceStepped: true });
const EMPTY_TIMELINE_KEYS_V1 = Object.freeze([] as const);
const PHYSICS_TIMELINE_FIELDS = Object.freeze([
  "mix",
  "inertia",
  "strength",
  "damping",
  "mass",
  "wind",
  "gravity",
] as const);
const TRANSFORM_TIMELINE_FIELDS = Object.freeze([
  ["mixRotate", "mix_rotate"],
  ["mixX", "mix_x"],
  ["mixY", "mix_y"],
  ["mixScaleX", "mix_scale_x"],
  ["mixScaleY", "mix_scale_y"],
  ["mixShearY", "mix_shear_y"],
] as const);
const FIELD_VALUE_GETTERS = new Map<PropertyKey, unknown>();
const SLOT_COLOR_COMPONENT_CACHE = new WeakMap<object, readonly number[]>();
const SLOT_COLOR_SAMPLE_SCRATCH = new WeakMap<object, number[]>();
const SLOT_COLOR_PROPERTIES = Object.freeze([
  "color_r",
  "color_g",
  "color_b",
  "alpha",
  "dark_r",
  "dark_g",
  "dark_b",
] as const);
type RegionTimelineSampleKeyV1 = {
  readonly time: number;
  readonly curve: RuntimeCurveV1;
  readonly x: number;
  readonly y: number;
  readonly rotation: number;
  readonly scaleX: number;
  readonly scaleY: number;
};

const REGION_CHANNELS_V1: ReadonlyArray<readonly [
  keyof Pick<RuntimeRegionAttachmentV1, "x" | "y" | "rotation" | "scaleX" | "scaleY">,
  RuntimeCurvePropertyNameV1,
  boolean,
  (key: RegionTimelineSampleKeyV1) => number,
]> = [
  ["x", "x", false, (key) => key.x],
  ["y", "y", false, (key) => key.y],
  ["rotation", "rotation", true, (key) => key.rotation],
  ["scaleX", "scale_x", false, (key) => key.scaleX],
  ["scaleY", "scale_y", false, (key) => key.scaleY],
];

export function createRuntimeSamplingScratchV1(): RuntimeSamplingScratchV1 {
  const scratch = Object.freeze({ runtimeSamplingScratchV1: true as const });
  SAMPLING_SCRATCH_STORAGE.set(scratch, {
    state: null,
    pose: null,
    skinAttachmentResolution: { skinIds: [], bySlotId: new Map() },
  });
  return scratch;
}


/**
 * Applies one non-recursive animation layer over an already sampled pose.
 * Slider constraints use this entry point while walking declaration order.
 */

export function createSamplingState(
  data: RuntimeDataV1,
  configuredSkinIds: readonly string[],
  base: RuntimeSampledPoseV1 | null = null,
  scratch: SamplingScratchStorageV1 | null = null,
): MutableSamplingStateV1 {
  const cached = samplingDataCacheV1(data);
  if (base === null && scratch?.state?.setupBones === cached.setupBones) {
    resetSamplingStateV1(scratch.state, data, configuredSkinIds, cached);
    return scratch.state;
  }
  const sourceBones = base?.bones ?? data.document.bones;
  const bones = new Map<string, Mutable<RuntimeBoneV1>>();
  for (const bone of sourceBones) bones.set(bone.id, { ...bone });

  const sourceSlots = base?.slots ?? data.document.slots;
  const slots = new Map<string, Mutable<RuntimeSlotV1>>();
  for (const slot of sourceSlots) {
    const logicalAttachment = base?.slotAttachmentKeys.has(slot.id)
      ? base.slotAttachmentKeys.get(slot.id) ?? null
      : slot.attachmentId;
    const created = { ...slot, attachmentId: logicalAttachment };
    copyRuntimeSlotTintBytesV1(slot, created);
    slots.set(slot.id, created);
  }

  const regions = new Map<string, Mutable<RuntimeRegionAttachmentV1>>();
  for (const attachment of cached.regionAttachments) {
    const sampled = base?.regions.get(attachment.id);
    regions.set(attachment.id, sampled === undefined
      ? { ...attachment }
      : { ...attachment, ...sampled });
  }

  const deforms = new Map<string, RuntimeSampledDeformV1>(base?.deforms ?? []);
  const deformEpoch = 1;
  const deformEpochByOwner = new Map<string, number>();
  for (const ownerId of deforms.keys()) deformEpochByOwner.set(ownerId, deformEpoch);
  const sequenceIndices = new Map<string, number>(base?.sequenceIndices ?? cached.setupSequenceIndices);
  const sourceConstraints = base?.constraints ?? data.document.constraints;
  const constraints = new Map<string, MutableConstraintV1>();
  for (const constraint of sourceConstraints) constraints.set(constraint.id, cloneConstraint(constraint));

  const created: MutableSamplingStateV1 = {
    setupBones: cached.setupBones,
    bones,
    setupSlots: cached.setupSlots,
    slots,
    setupRegions: cached.setupRegions,
    regions,
    attachmentById: cached.attachmentById,
    deforms,
    deformPool: new Map(),
    deformScratchByTimeline: new WeakMap(),
    deformEpochByOwner,
    deformEpoch,
    sequenceIndices,
    setupConstraints: cached.setupConstraints,
    constraints,
    setupOrder: cached.setupOrder,
    drawOrder: [...(base?.drawOrderSlotIds ?? cached.setupOrder)],
    drawOrderSampled: base?.drawOrderSampled ?? false,
    sampledSkinIds: [...configuredSkinIds],
    sampledSliderTimes: new Map(base?.sampledSliderTimes ?? []),
    constraintDiagnostics: new Map(base?.constraintDiagnostics ?? []),
  };
  if (base === null && scratch !== null) scratch.state = created;
  return created;
}


export function snapshotSamplingState(
  data: RuntimeDataV1,
  state: MutableSamplingStateV1,
  scratch: SamplingScratchStorageV1 | null = null,
): RuntimeSampledPoseV1 {
  const reusable = scratch?.pose ?? null;
  const skinAttachmentResolution = scratch?.skinAttachmentResolution ?? null;
  if (skinAttachmentResolution !== null) {
    prepareSkinAttachmentResolutionScratchV1(skinAttachmentResolution, state.sampledSkinIds);
  }
  const slotAttachmentKeys = reusable?.slotAttachmentKeys as Map<string, string | null> | undefined
    ?? new Map<string, string | null>();
  let slotKeyShapeMatches = slotAttachmentKeys.size === data.document.slots.length;
  if (slotKeyShapeMatches) {
    for (let index = 0; index < data.document.slots.length; index += 1) {
      const slot = data.document.slots[index];
      if (slot === undefined) continue;
      if (!slotAttachmentKeys.has(slot.id)) {
        slotKeyShapeMatches = false;
        break;
      }
    }
  }
  if (!slotKeyShapeMatches) slotAttachmentKeys.clear();
  const resolvedSlots = reusable?.slots as Mutable<RuntimeSlotV1>[] | undefined
    ?? new Array<Mutable<RuntimeSlotV1>>(data.document.slots.length);
  resolvedSlots.length = data.document.slots.length;
  for (let index = 0; index < data.document.slots.length; index += 1) {
    const setupSlot = data.document.slots[index];
    if (setupSlot === undefined) continue;
    const sampledSlot = state.slots.get(setupSlot.id) ?? setupSlot;
    slotAttachmentKeys.set(sampledSlot.id, sampledSlot.attachmentId);
    let resolved = resolvedSlots[index];
    if (resolved === undefined) {
      resolved = { ...sampledSlot };
      resolvedSlots[index] = resolved;
    } else {
      copySlotV1(resolved, sampledSlot);
    }
    copyRuntimeSlotTintBytesV1(sampledSlot, resolved);
    resolved.attachmentId = resolveSkinAttachmentV1(
      data,
      state.sampledSkinIds,
      sampledSlot.id,
      sampledSlot.attachmentId,
      skinAttachmentResolution,
    );
  }

  const bones = reusable?.bones as RuntimeBoneV1[] | undefined
    ?? new Array<RuntimeBoneV1>(data.document.bones.length);
  bones.length = data.document.bones.length;
  for (let index = 0; index < data.document.bones.length; index += 1) {
    const bone = data.document.bones[index];
    if (bone !== undefined) bones[index] = state.bones.get(bone.id) ?? bone;
  }
  const constraints = reusable?.constraints as RuntimeConstraintV1[] | undefined
    ?? new Array<RuntimeConstraintV1>(data.document.constraints.length);
  constraints.length = data.document.constraints.length;
  for (let index = 0; index < data.document.constraints.length; index += 1) {
    const constraint = data.document.constraints[index];
    if (constraint !== undefined) constraints[index] = state.constraints.get(constraint.id) ?? constraint;
  }

  if (reusable !== null) {
    reusable.bones = bones;
    reusable.slots = resolvedSlots;
    reusable.regions = state.regions;
    reusable.deforms = state.deforms;
    reusable.sequenceIndices = state.sequenceIndices;
    reusable.slotAttachmentKeys = slotAttachmentKeys;
    reusable.sampledSkinIds = state.sampledSkinIds;
    reusable.constraints = constraints;
    reusable.drawOrderSlotIds = state.drawOrder;
    reusable.drawOrderSampled = state.drawOrderSampled;
    reusable.sampledSliderTimes = state.sampledSliderTimes;
    reusable.constraintDiagnostics = state.constraintDiagnostics;
    return reusable;
  }
  const created: MutableSampledPoseV1 = {
    bones,
    slots: resolvedSlots,
    regions: state.regions,
    deforms: state.deforms,
    sequenceIndices: state.sequenceIndices,
    slotAttachmentKeys,
    sampledSkinIds: state.sampledSkinIds,
    constraints,
    drawOrderSlotIds: state.drawOrder,
    drawOrderSampled: state.drawOrderSampled,
    sampledSliderTimes: state.sampledSliderTimes,
    constraintDiagnostics: state.constraintDiagnostics,
  };
  if (scratch !== null) scratch.pose = created;
  return created;
}

function resetSamplingStateV1(
  state: MutableSamplingStateV1,
  data: RuntimeDataV1,
  configuredSkinIds: readonly string[],
  cached: SamplingDataCacheV1,
): void {
  for (let index = 0; index < data.document.bones.length; index += 1) {
    const bone = data.document.bones[index];
    if (bone === undefined) continue;
    const current = state.bones.get(bone.id);
    if (current !== undefined) copyBoneV1(current, bone);
  }
  for (let index = 0; index < data.document.slots.length; index += 1) {
    const slot = data.document.slots[index];
    if (slot === undefined) continue;
    const current = state.slots.get(slot.id);
    if (current !== undefined) {
      copySlotV1(current, slot);
      resetRuntimeSlotTintBytesV1(current);
    }
  }
  for (let index = 0; index < cached.regionAttachments.length; index += 1) {
    const attachment = cached.regionAttachments[index];
    if (attachment === undefined) continue;
    const current = state.regions.get(attachment.id);
    if (current !== undefined) copyRegionV1(current, attachment);
  }
  state.deformEpoch += 1;
  if (!Number.isSafeInteger(state.deformEpoch)) {
    state.deformEpoch = 1;
    state.deformEpochByOwner.clear();
  }
  syncSequenceIndicesV1(state.sequenceIndices, cached.setupSequenceIds, cached.setupSequenceValues);
  for (let index = 0; index < data.document.constraints.length; index += 1) {
    const constraint = data.document.constraints[index];
    if (constraint === undefined) continue;
    const current = state.constraints.get(constraint.id);
    if (current === undefined) continue;
    copyConstraintV1(current, constraint);
  }
  copyArrayV1(state.drawOrder, cached.setupOrder);
  state.drawOrderSampled = false;
  copyArrayV1(state.sampledSkinIds, configuredSkinIds);
  state.sampledSliderTimes.clear();
  state.constraintDiagnostics.clear();
}

function copyBoneV1(target: Mutable<RuntimeBoneV1>, source: RuntimeBoneV1): void {
  target.id = source.id;
  target.name = source.name;
  target.parentId = source.parentId;
  target.x = source.x;
  target.y = source.y;
  target.rotation = source.rotation;
  target.shearX = source.shearX;
  target.shearY = source.shearY;
  target.scaleX = source.scaleX;
  target.scaleY = source.scaleY;
  target.length = source.length;
  target.transformMode = source.transformMode;
}

function copySlotV1(target: Mutable<RuntimeSlotV1>, source: RuntimeSlotV1): void {
  target.id = source.id;
  target.name = source.name;
  target.boneId = source.boneId;
  target.attachmentId = source.attachmentId;
  target.zIndex = source.zIndex;
  target.blendMode = source.blendMode;
  target.color = source.color;
  target.alpha = source.alpha;
  target.darkColor = source.darkColor;
}

function copyRegionV1(
  target: Mutable<RuntimeRegionAttachmentV1>,
  source: RuntimeRegionAttachmentV1,
): void {
  target.type = source.type;
  target.id = source.id;
  target.name = source.name;
  target.slotId = source.slotId;
  target.imageId = source.imageId;
  target.color = source.color;
  target.alpha = source.alpha;
  target.sequence = source.sequence;
  target.x = source.x;
  target.y = source.y;
  target.rotation = source.rotation;
  target.scaleX = source.scaleX;
  target.scaleY = source.scaleY;
}

function copyConstraintV1(target: MutableConstraintV1, source: RuntimeConstraintV1): void {
  if (target.type !== source.type) {
    throw new Error(`Constraint '${source.id}' changed type inside immutable Runtime data.`);
  }
  target.id = source.id;
  target.name = source.name;
  switch (source.type) {
    case "ik": {
      if (target.type !== "ik") return;
      target.chainBoneIds = source.chainBoneIds;
      target.targetBoneId = source.targetBoneId;
      const mutableTarget = target.target as Mutable<typeof target.target>;
      mutableTarget.x = source.target.x;
      mutableTarget.y = source.target.y;
      target.mix = source.mix;
      target.bendPositive = source.bendPositive;
      target.compress = source.compress;
      target.stretch = source.stretch;
      target.uniform = source.uniform;
      target.softness = source.softness;
      target.iterations = source.iterations;
      target.threshold = source.threshold;
      return;
    }
    case "transform":
      if (target.type !== "transform") return;
      target.boneIds = source.boneIds;
      target.targetBoneId = source.targetBoneId;
      target.local = source.local;
      target.relative = source.relative;
      target.rotation = source.rotation;
      target.x = source.x;
      target.y = source.y;
      target.scaleX = source.scaleX;
      target.scaleY = source.scaleY;
      target.shearY = source.shearY;
      target.mixRotate = source.mixRotate;
      target.mixX = source.mixX;
      target.mixY = source.mixY;
      target.mixScaleX = source.mixScaleX;
      target.mixScaleY = source.mixScaleY;
      target.mixShearY = source.mixShearY;
      target.mapping = source.mapping;
      return;
    case "path":
      if (target.type !== "path") return;
      target.boneIds = source.boneIds;
      target.targetSlotId = source.targetSlotId;
      target.positionMode = source.positionMode;
      target.spacingMode = source.spacingMode;
      target.rotateMode = source.rotateMode;
      target.rotation = source.rotation;
      target.position = source.position;
      target.spacing = source.spacing;
      target.mixRotate = source.mixRotate;
      target.mixX = source.mixX;
      target.mixY = source.mixY;
      return;
    case "physics":
      if (target.type !== "physics") return;
      target.boneId = source.boneId;
      target.x = source.x;
      target.y = source.y;
      target.rotate = source.rotate;
      target.scaleX = source.scaleX;
      target.scaleYMode = source.scaleYMode;
      target.shearX = source.shearX;
      target.limit = source.limit;
      target.fps = source.fps;
      target.inertia = source.inertia;
      target.strength = source.strength;
      target.damping = source.damping;
      target.mass = source.mass;
      target.wind = source.wind;
      target.gravity = source.gravity;
      target.mix = source.mix;
      target.inertiaGlobal = source.inertiaGlobal;
      target.strengthGlobal = source.strengthGlobal;
      target.dampingGlobal = source.dampingGlobal;
      target.massGlobal = source.massGlobal;
      target.windGlobal = source.windGlobal;
      target.gravityGlobal = source.gravityGlobal;
      target.mixGlobal = source.mixGlobal;
      return;
    case "slider":
      if (target.type !== "slider") return;
      target.animationId = source.animationId;
      target.looping = source.looping;
      target.additive = source.additive;
      target.sourceBoneId = source.sourceBoneId;
      target.sourceProperty = source.sourceProperty;
      target.sourceOffset = source.sourceOffset;
      target.timeOffset = source.timeOffset;
      target.timeScale = source.timeScale;
      target.rangeMax = source.rangeMax;
      target.local = source.local;
      target.time = source.time;
      target.mix = source.mix;
      return;
  }
}

function samplingScratchStorageV1(scratch: RuntimeSamplingScratchV1): SamplingScratchStorageV1 {
  let storage = SAMPLING_SCRATCH_STORAGE.get(scratch);
  if (storage === undefined) {
    storage = {
      state: null,
      pose: null,
      skinAttachmentResolution: { skinIds: [], bySlotId: new Map() },
    };
    SAMPLING_SCRATCH_STORAGE.set(scratch, storage);
  }
  return storage;
}

function copyArrayV1<T>(target: T[], source: readonly T[]): void {
  target.length = source.length;
  for (let index = 0; index < source.length; index += 1) {
    const value = source[index];
    if (value !== undefined) target[index] = value;
  }
}

function syncSequenceIndicesV1(
  target: Map<string, number>,
  sourceIds: readonly string[],
  sourceValues: readonly number[],
): void {
  let shapeMatches = target.size === sourceIds.length;
  if (shapeMatches) {
    for (let index = 0; index < sourceIds.length; index += 1) {
      const id = sourceIds[index];
      if (id === undefined || !target.has(id)) {
        shapeMatches = false;
        break;
      }
    }
  }
  if (!shapeMatches) target.clear();
  for (let index = 0; index < sourceIds.length; index += 1) {
    const id = sourceIds[index];
    const value = sourceValues[index];
    if (id !== undefined && value !== undefined) target.set(id, value);
  }
}

function finalizeSamplingStateV1(state: MutableSamplingStateV1): void {
  for (const ownerId of state.deforms.keys()) {
    if (state.deformEpochByOwner.get(ownerId) !== state.deformEpoch) state.deforms.delete(ownerId);
  }
}

function samplingDataCacheV1(data: RuntimeDataV1): SamplingDataCacheV1 {
  const existing = SAMPLING_DATA_CACHE.get(data);
  if (existing !== undefined) return existing;

  const setupBones = new Map<string, RuntimeBoneV1>();
  for (const bone of data.document.bones) setupBones.set(bone.id, bone);
  const setupSlots = new Map<string, RuntimeSlotV1>();
  for (const slot of data.document.slots) setupSlots.set(slot.id, slot);
  const regionAttachments: RuntimeRegionAttachmentV1[] = [];
  const setupRegions = new Map<string, RuntimeRegionAttachmentV1>();
  const setupSequenceIndices = new Map<string, number>();
  const setupSequenceIds: string[] = [];
  const setupSequenceValues: number[] = [];
  const attachmentById = new Map<string, RuntimeAttachmentV1>();
  for (const attachment of data.document.attachments) {
    attachmentById.set(attachment.id, attachment);
    if (attachment.type === "region") {
      regionAttachments.push(attachment);
      setupRegions.set(attachment.id, attachment);
    }
    if ((attachment.type === "region" || attachment.type === "mesh") && attachment.sequence !== null) {
      setupSequenceIndices.set(attachment.id, attachment.sequence.setupIndex);
      setupSequenceIds.push(attachment.id);
      setupSequenceValues.push(attachment.sequence.setupIndex);
    }
  }
  const setupConstraints = new Map<string, RuntimeConstraintV1>();
  for (const constraint of data.document.constraints) setupConstraints.set(constraint.id, constraint);

  const sortedSlots = [...data.document.slots];
  // ECMAScript requires stable Array sorting; equal zIndex values retain declaration order.
  sortedSlots.sort((left, right) => left.zIndex - right.zIndex);
  const setupOrder = new Array<string>(sortedSlots.length);
  for (let index = 0; index < sortedSlots.length; index += 1) {
    const slot = sortedSlots[index];
    if (slot !== undefined) setupOrder[index] = slot.id;
  }

  const created: SamplingDataCacheV1 = {
    setupBones,
    setupSlots,
    regionAttachments,
    setupRegions,
    setupConstraints,
    setupOrder,
    setupSequenceIndices,
    setupSequenceIds,
    setupSequenceValues,
    attachmentById,
  };
  SAMPLING_DATA_CACHE.set(data, created);
  return created;
}

function cloneConstraint(constraint: RuntimeConstraintV1): MutableConstraintV1 {
  if (constraint.type === "ik") return { ...constraint, target: { ...constraint.target } };
  return { ...constraint };
}










function resolveSkinAttachmentV1(
  data: RuntimeDataV1,
  skinIds: readonly string[],
  slotId: string,
  attachmentKey: string | null,
  scratch: SkinAttachmentResolutionScratchV1 | null = null,
): string | null {
  if (attachmentKey === null) return null;
  let cachedByKey: Map<string, string | null> | null = null;
  if (scratch !== null) {
    cachedByKey = scratch.bySlotId.get(slotId) ?? null;
    if (cachedByKey?.has(attachmentKey) === true) return cachedByKey.get(attachmentKey) ?? null;
    if (cachedByKey === null) {
      cachedByKey = new Map();
      scratch.bySlotId.set(slotId, cachedByKey);
    }
  }
  const directAttachment = samplingDataCacheV1(data).attachmentById.get(attachmentKey);
  let resolved: string | null = directAttachment?.slotId === slotId ? attachmentKey : null;
  const defaultSkin = data.document.skins[0];
  if (skinIds.length > 0
    && defaultSkin !== undefined
    && !skinIds.includes(defaultSkin.id)) {
    resolved = applySkinAttachmentMappingsV1(defaultSkin, slotId, attachmentKey, resolved);
  }
  for (let skinIndex = 0; skinIndex < skinIds.length; skinIndex += 1) {
    const skinId = skinIds[skinIndex];
    if (skinId === undefined) continue;
    const skin = data.skin(skinId);
    resolved = applySkinAttachmentMappingsV1(skin, slotId, attachmentKey, resolved);
  }
  cachedByKey?.set(attachmentKey, resolved);
  return resolved;
}

function applySkinAttachmentMappingsV1(
  skin: RuntimeSkinV1,
  slotId: string,
  attachmentKey: string,
  initial: string | null,
): string | null {
  let resolved = initial;
  for (let mappingIndex = 0; mappingIndex < skin.attachments.length; mappingIndex += 1) {
    const mapping = skin.attachments[mappingIndex];
    if (mapping === undefined || mapping.slotId !== slotId) continue;
    if (mapping.name !== null && mapping.name !== attachmentKey) continue;
    resolved = mapping.attachmentId;
  }
  return resolved;
}

function prepareSkinAttachmentResolutionScratchV1(
  scratch: SkinAttachmentResolutionScratchV1,
  skinIds: readonly string[],
): void {
  let matches = scratch.skinIds.length === skinIds.length;
  if (matches) {
    for (let index = 0; index < skinIds.length; index += 1) {
      if (scratch.skinIds[index] !== skinIds[index]) {
        matches = false;
        break;
      }
    }
  }
  if (matches) return;
  copyArrayV1(scratch.skinIds, skinIds);
  scratch.bySlotId.clear();
}



interface AxisKeyV1 {
  readonly time: number;
  readonly curve: RuntimeCurveV1;
  readonly value: number;
  readonly source: 0 | 1;
  readonly declaration: number;
}

type MergedAxisKeyCacheV1 = Partial<Record<
  "x" | "y" | "scaleX" | "scaleY" | "shearX" | "shearY",
  readonly AxisKeyV1[]
>>;

const MERGED_AXIS_KEY_CACHE = new WeakMap<RuntimeBoneTimelineV1, MergedAxisKeyCacheV1>();

export function mergedAxisKeys(
  timeline: RuntimeBoneTimelineV1,
  target: keyof MergedAxisKeyCacheV1,
  paired: readonly { readonly time: number; readonly curve: RuntimeCurveV1; readonly x: number; readonly y: number }[] | null,
  scalar: readonly { readonly time: number; readonly curve: RuntimeCurveV1; readonly value: number }[] | null,
  axis: "x" | "y",
  property: RuntimeCurvePropertyNameV1,
): readonly AxisKeyV1[] {
  let cached = MERGED_AXIS_KEY_CACHE.get(timeline);
  if (cached === undefined) {
    cached = {};
    MERGED_AXIS_KEY_CACHE.set(timeline, cached);
  }
  const existing = cached[target];
  if (existing !== undefined) return existing;
  const created = mergeAxisKeys(paired, scalar, axis, property);
  cached[target] = created;
  return created;
}

function mergeAxisKeys(
  paired: readonly { readonly time: number; readonly curve: RuntimeCurveV1; readonly x: number; readonly y: number }[] | null,
  scalar: readonly { readonly time: number; readonly curve: RuntimeCurveV1; readonly value: number }[] | null,
  axis: "x" | "y",
  property: RuntimeCurvePropertyNameV1,
): AxisKeyV1[] {
  const pairKeys = collapseWithinSource((paired ?? []).map((key, declaration): AxisKeyV1 => ({
    time: key.time,
    curve: selectPropertyCurveV1(key.curve, axis),
    value: key[axis],
    source: 0,
    declaration,
  })));
  const scalarKeys = collapseWithinSource((scalar ?? []).map((key, declaration): AxisKeyV1 => ({
    time: key.time,
    curve: selectPropertyCurveV1(key.curve, key.curve !== null && typeof key.curve === "object" && key.curve.type === "properties" && !Object.prototype.hasOwnProperty.call(key.curve.properties, property) ? axis : property),
    value: key.value,
    source: 1,
    declaration,
  })));
  return [...pairKeys, ...scalarKeys].sort((left, right) =>
    left.time === right.time
      ? left.source - right.source || left.declaration - right.declaration
      : left.time - right.time,
  );
}

function collapseWithinSource(keys: readonly AxisKeyV1[]): AxisKeyV1[] {
  const result: AxisKeyV1[] = [];
  for (const key of keys) {
    const previous = result[result.length - 1];
    if (previous !== undefined && previous.time === key.time) result[result.length - 1] = key;
    else result.push(key);
  }
  return result;
}

export function presentNumberKeys<
  Key extends { readonly time: number; readonly curve: RuntimeCurveV1 },
  Field extends keyof Key,
>(keys: readonly Key[], field: Field): readonly (Key & Record<Field, number>)[] {
  return presentTypedKeys(PRESENT_NUMBER_KEY_CACHE, keys, field, "number") as readonly (Key & Record<Field, number>)[];
}

export function presentBooleanKeys<
  Key extends { readonly time: number; readonly curve: RuntimeCurveV1 },
  Field extends keyof Key,
>(keys: readonly Key[], field: Field): readonly (Key & Record<Field, boolean>)[] {
  return presentTypedKeys(PRESENT_BOOLEAN_KEY_CACHE, keys, field, "boolean") as readonly (Key & Record<Field, boolean>)[];
}

function presentTypedKeys<Key extends object>(
  cache: WeakMap<object, Map<PropertyKey, readonly unknown[]>>,
  keys: readonly Key[],
  field: keyof Key,
  expectedType: "number" | "boolean",
): readonly Key[] {
  let byField = cache.get(keys);
  if (byField === undefined) {
    byField = new Map<PropertyKey, readonly unknown[]>();
    cache.set(keys, byField);
  }
  const cached = byField.get(field);
  if (cached !== undefined) return cached as readonly Key[];
  const present: Key[] = [];
  for (const key of keys) {
    if (typeof key[field] === expectedType) present.push(key);
  }
  byField.set(field, present);
  return present;
}




export function slotColorComponentsV1(key: RuntimeSlotColorKeyV1): readonly number[] {
  const cached = SLOT_COLOR_COMPONENT_CACHE.get(key);
  if (cached !== undefined) return cached;
  const created = [
    colorUnitComponent(key.color, 0),
    colorUnitComponent(key.color, 1),
    colorUnitComponent(key.color, 2),
    key.alpha,
    key.darkColor === null ? 0 : colorUnitComponent(key.darkColor, 0),
    key.darkColor === null ? 0 : colorUnitComponent(key.darkColor, 1),
    key.darkColor === null ? 0 : colorUnitComponent(key.darkColor, 2),
  ];
  SLOT_COLOR_COMPONENT_CACHE.set(key, created);
  return created;
}

export function sampledOptionalDarkColorExistsV1(keys: readonly RuntimeSlotColorKeyV1[], time: number): boolean {
  const sampleTime = f32(time);
  let low = 0;
  let high = keys.length;
  while (low < high) {
    const middle = (low + high) >>> 1;
    const key = keys[middle];
    if (key !== undefined && key.time <= sampleTime) low = middle + 1;
    else high = middle;
  }
  const currentIndex = low - 1;
  if (currentIndex < 0) return false;
  const current = keys[currentIndex];
  if (current === undefined) return false;
  if (sampleTime === current.time) return current.darkColor !== null;
  const next = keys[currentIndex + 1];
  return current.darkColor !== null || (next !== undefined && next.darkColor !== null);
}





type VertexAttachmentV1 = Extract<RuntimeAttachmentV1, { readonly vertices: readonly number[] }>;

export function resolveVertexSource(
  attachment: VertexAttachmentV1,
  attachments: ReadonlyMap<string, RuntimeAttachmentV1>,
): VertexAttachmentV1 {
  let current = attachment;
  while (current.type === "mesh" && current.link !== null) {
    const parent = attachments.get(current.link.parentMeshId);
    if (parent === undefined || parent.type !== "mesh") return current;
    current = parent;
  }
  return current;
}

export function resolveDeformOwnerId(
  attachment: Extract<RuntimeAttachmentV1, { readonly type: "mesh" }>,
  attachments: ReadonlyMap<string, RuntimeAttachmentV1>,
): string {
  let current = attachment;
  while (current.link !== null && current.link.inheritDeform) {
    const parent = attachments.get(current.link.parentMeshId);
    if (parent === undefined || parent.type !== "mesh") break;
    current = parent;
  }
  return current.id;
}

export function canonicalizeDeformV1(
  source: VertexAttachmentV1,
  space: RuntimeAttachmentTimelineV1["deformSpace"],
  values: readonly number[],
  output: number[],
): boolean {
  if (source.weights.length === 0 || space === "weightedInfluenceOffsets") {
    copyArrayV1(output, values);
    return source.weights.length > 0;
  }
  let cursor = 0;
  for (let point = 0; point < source.weights.length; point += 1) {
    const dx = f32Sub(values[point * 2] ?? 0, source.vertices[point * 2] ?? 0);
    const dy = f32Sub(values[point * 2 + 1] ?? 0, source.vertices[point * 2 + 1] ?? 0);
    const influences = source.weights[point];
    if (influences === undefined) continue;
    for (const influence of influences) {
      if (influence.x !== null) {
        output[cursor] = dx;
        output[cursor + 1] = dy;
      } else {
        const inverse = source.bindInverses?.[influence.boneId];
        output[cursor] = inverse === undefined
          ? dx
          : f32Add(f32Mul(inverse.a, dx), f32Mul(inverse.c, dy));
        output[cursor + 1] = inverse === undefined
          ? dy
          : f32Add(f32Mul(inverse.b, dx), f32Mul(inverse.d, dy));
      }
      cursor += 2;
    }
  }
  output.length = cursor;
  return true;
}



















function clampUnit(value: number): number {
  return f32(Math.min(Math.max(value, 0), 1));
}

export function colorUnitByteV1(component: number): number {
  return Math.floor(f32Add(f32Mul(clampUnit(component), 255), 0.5));
}

function colorUnitComponent(value: string, component: number): number {
  const offset = 1 + component * 2;
  return f32((hexNibble(value.charCodeAt(offset)) * 16 + hexNibble(value.charCodeAt(offset + 1))) / 255);
}

function hexNibble(code: number): number {
  return code <= 57 ? code - 48 : (code & 0xdf) - 55;
}


/** Independent layers use the same endpoint projection as player tracks. */
export function sampleAnimationLayersV1(data: RuntimeDataV1, layers: readonly RuntimeAnimationLayerV1[],
  skins: readonly string[] = data.document.skins[0] ? [data.document.skins[0].id] : [],
  _scratch: RuntimeSamplingScratchV1 | null = null): RuntimeSampledPoseV1 {
  return sampleAnimationTracksV1(data, layers.map(layer => [{ layer, progress: 1, holdPreviousPose: false }]), skins);
}
export function applyAnimationLayerToSampledPoseV1(data: RuntimeDataV1, pose: RuntimeSampledPoseV1,
  layer: RuntimeAnimationLayerV1): RuntimeSampledPoseV1 {
  return applyEndpointLayerV1(data, pose, layer);
}
