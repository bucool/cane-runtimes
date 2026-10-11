import { advanceSpring } from "../math/damped-spring.js";
import type {
  AffineV1,
  RootTransformV1,
  RuntimeBoneV1,
  RuntimePhysicsConstraintV1,
  RuntimePhysicsConstraintDiagnosticV1,
  RuntimePhysicsEnvironmentV1,
} from "../contracts.js";
import { RuntimeErrorV1 } from "../errors.js";
import {
  F32_DEGREES_TO_RADIANS,
  f32,
  f32Add,
  f32Div,
  f32Mul,
  f32Sub,
} from "../math/f32.js";

export const MAX_PHYSICS_SUBSTEPS_PER_OPERATION_V1 = 1_000_000;

export const DEFAULT_PHYSICS_ENVIRONMENT_V1: RuntimePhysicsEnvironmentV1 = Object.freeze({
  windX: 1,
  windY: 0,
  gravityX: 0,
  gravityY: 1,
});

export interface RuntimePhysicsBudgetV1 {
  remaining: number;
}

export interface RuntimePhysicsStateV1 {
  reset: boolean;
  restX: number;
  restY: number;
  axisX: number;
  axisY: number;
  xOffset: number;
  yOffset: number;
  xVelocity: number;
  yVelocity: number;
  angleOffset: number;
  angleVelocity: number;
  logStretch: number;
  stretchVelocity: number;
}

export interface RuntimePhysicsClockInputV1 {
  readonly animationId: string | null;
  readonly playbackTime: number;
  readonly duration: number;
  readonly looping: boolean;
  rangeStart: number;
  rangeEnd: number;
  resetWeight: number;
}

export interface RuntimePhysicsClockStepV1 extends RuntimePhysicsClockInputV1 {
  readonly from: number | null;
  readonly delta: number;
  readonly discontinuity: boolean;
}

/** Caller-owned storage for allocation-free clock advancement. */
export interface RuntimePhysicsClockStepScratchV1 {
  animationId: string | null;
  playbackTime: number;
  duration: number;
  looping: boolean;
  from: number | null;
  delta: number;
  discontinuity: boolean;
  rangeStart: number;
  rangeEnd: number;
  resetWeight: number;
}

export interface RuntimePhysicsApplyResultV1 {
  readonly matrix: AffineV1;
  readonly diagnostic: RuntimePhysicsConstraintDiagnosticV1;
}

type Mutable<T> = { -readonly [Property in keyof T]: T[Property] };


/** Per-constraint mutable storage used by RuntimePlayer performance mode. */
export interface RuntimePhysicsApplyScratchV1 {
  readonly matrix: Mutable<AffineV1>;
  readonly diagnostic: Mutable<RuntimePhysicsConstraintDiagnosticV1>;
  readonly result: RuntimePhysicsApplyResultV1;
}

export function createRuntimePhysicsApplyScratchV1(): RuntimePhysicsApplyScratchV1 {
  const matrix: Mutable<AffineV1> = { a: 1, b: 0, c: 0, d: 1, tx: 0, ty: 0 };
  const diagnostic: Mutable<RuntimePhysicsConstraintDiagnosticV1> = {
    type: "physics",
    fixedSteps: 0,
    translationOffset: 0,
    translationSpeed: 0,
    rotationOffsetDegrees: 0,
    angularSpeedDegrees: 0,
    scaleOffset: 0,
    scaleSpeed: 0,
    configuredLimit: 0,
    requiredLimit: 0,
    limitSaturated: false,
  };
  return {
    matrix,
    diagnostic,
    result: { matrix, diagnostic },
  };
}

export class RuntimePhysicsStateStoreV1 {
  readonly #states: Map<string, RuntimePhysicsStateV1>;
  readonly #stateIds: string[];
  #animationId: string | null;
  #playbackTime: number | null;
  #rangeStart: number | null = null;
  #rangeEnd = 0;
  #looping = false;
  readonly #rootDeltaScratch: MutableRuntimeRootDeltaV1 = {
    a: 1,
    b: 0,
    c: 0,
    d: 1,
    tx: 0,
    ty: 0,
  };

  constructor(
    states: ReadonlyMap<string, RuntimePhysicsStateV1> = new Map(),
    animationId: string | null = null,
    playbackTime: number | null = null,
  ) {
    this.#states = new Map();
    this.#stateIds = [];
    for (const [id, state] of states) {
      this.#states.set(id, { ...state });
      this.#stateIds.push(id);
    }
    this.#animationId = animationId;
    this.#playbackTime = playbackTime;
  }

  clone(): RuntimePhysicsStateStoreV1 {
    const result = new RuntimePhysicsStateStoreV1(this.#states, this.#animationId, this.#playbackTime);
    result.#rangeStart=this.#rangeStart;result.#rangeEnd=this.#rangeEnd;result.#looping=this.#looping;
    return result;
  }

  /** Copies state into retained records for allocation-free transaction buffers. */
  copyFrom(source: RuntimePhysicsStateStoreV1): this {
    const sourceIds = source.#stateIds;
    let shapeMatches = this.#stateIds.length === sourceIds.length;
    if (shapeMatches) {
      for (let index = 0; index < sourceIds.length; index += 1) {
        if (this.#stateIds[index] !== sourceIds[index]) {
          shapeMatches = false;
          break;
        }
      }
    }

    if (!shapeMatches) {
      for (let index = 0; index < this.#stateIds.length; index += 1) {
        const constraintId = this.#stateIds[index];
        if (constraintId !== undefined && !source.#states.has(constraintId)) {
          this.#states.delete(constraintId);
        }
      }
      this.#stateIds.length = sourceIds.length;
      for (let index = 0; index < sourceIds.length; index += 1) {
        const constraintId = sourceIds[index];
        if (constraintId === undefined) continue;
        this.#stateIds[index] = constraintId;
        if (!this.#states.has(constraintId)) this.#states.set(constraintId, defaultState());
      }
    }

    for (let index = 0; index < sourceIds.length; index += 1) {
      const constraintId = sourceIds[index];
      if (constraintId === undefined) continue;
      const sourceState = source.#states.get(constraintId);
      if (sourceState === undefined) continue;
      let target = this.#states.get(constraintId);
      if (target === undefined) {
        target = defaultState();
        this.#states.set(constraintId, target);
      }
      copyPhysicsStateV1(target, sourceState);
    }
    this.#animationId = source.#animationId;
    this.#playbackTime = source.#playbackTime;
    this.#rangeStart=source.#rangeStart;this.#rangeEnd=source.#rangeEnd;this.#looping=source.#looping;
    return this;
  }

  state(constraintId: string): RuntimePhysicsStateV1 {
    let state = this.#states.get(constraintId);
    if (state === undefined) {
      state = defaultState();
      this.#states.set(constraintId, state);
      this.#stateIds.push(constraintId);
    }
    return state;
  }

  reset(constraintId?: string): boolean {
    if (constraintId === undefined) {
      const cleared = this.#states.size > 0;
      this.#states.clear();
      this.#stateIds.length = 0;
      this.#animationId = null;
      this.#playbackTime = null;
      this.#rangeStart = null;
      return cleared;
    } else {
      const state = this.#states.get(constraintId);
      if (state === undefined) return false;
      resetPhysicsStateV1(state);
      return true;
    }
  }

  /** Resets simulation records without discontinuing the animation clock. */
  resetSimulation(constraintId?: string): number {
    if (constraintId !== undefined) {
      const state = this.#states.get(constraintId);
      if (state === undefined) return 0;
      resetPhysicsStateV1(state);
      return 1;
    }
    for (let index = 0; index < this.#stateIds.length; index += 1) {
      const constraintIdAtIndex = this.#stateIds[index];
      const state = constraintIdAtIndex === undefined ? undefined : this.#states.get(constraintIdAtIndex);
      if (state !== undefined) resetPhysicsStateV1(state);
    }
    return this.#stateIds.length;
  }

  /**
   * Transports existing world-space histories through a host root delta.
   * The caller performs this on a transaction candidate before evaluation.
   */
  applyHostMotion(
    previous: RootTransformV1,
    next: RootTransformV1,
    mode: "move" | "teleport" | "preserveInertia" | "clearInertia",
    constraintId?: string,
  ): number {
    if (mode === "move") return 0;
    if (mode === "teleport") return this.resetSimulation(constraintId);
    if (mode !== "preserveInertia" && mode !== "clearInertia") {
      throw new RuntimeErrorV1("invalidArgument", "setRootTransform", "Unknown Physics host motion mode.", {
        field: "physicsMode",
        entityId: constraintId ?? null,
      });
    }
    const delta = rootDeltaV1(previous, next, this.#rootDeltaScratch);
    if (constraintId !== undefined) {
      const state = this.#states.get(constraintId);
      if (state === undefined) return 0;
      if (!transformedPhysicsStateFiniteV1(state, delta)) {
        throw new RuntimeErrorV1("nonFinite", "setRootTransform", "Root motion produced non-finite Physics state.", {
          field: "rootTransform",
          entityId: constraintId,
        });
      }
      transformPhysicsStateV1(state, delta);
      if (mode === "clearInertia") clearPhysicsInertiaV1(state);
      return 1;
    }
    for (let index = 0; index < this.#stateIds.length; index += 1) {
      const id = this.#stateIds[index];
      const state = id === undefined ? undefined : this.#states.get(id);
      if (state !== undefined && !transformedPhysicsStateFiniteV1(state, delta)) {
        throw new RuntimeErrorV1("nonFinite", "setRootTransform", "Root motion produced non-finite Physics state.", {
          field: "rootTransform",
          entityId: id ?? null,
        });
      }
    }
    for (let index = 0; index < this.#stateIds.length; index += 1) {
      const id = this.#stateIds[index];
      const state = id === undefined ? undefined : this.#states.get(id);
      if (state === undefined) continue;
      transformPhysicsStateV1(state, delta);
      if (mode === "clearInertia") clearPhysicsInertiaV1(state);
    }
    return this.#stateIds.length;
  }

  advanceClock(
    input: RuntimePhysicsClockInputV1,
    output: RuntimePhysicsClockStepScratchV1 | null = null,
  ): RuntimePhysicsClockStepV1 {
    const changedAnimation = this.#animationId !== input.animationId || this.#rangeStart !== input.rangeStart || this.#rangeEnd !== input.rangeEnd || this.#looping !== input.looping;
    const movedBackwards = this.#playbackTime !== null && input.playbackTime < this.#playbackTime;
    const discontinuity = changedAnimation || movedBackwards;
    const from = discontinuity ? null : this.#playbackTime;
    if (discontinuity) {
      this.#states.clear();
      this.#stateIds.length = 0;
    }
    const delta = discontinuity
      ? Math.max(input.playbackTime, 0)
      : Math.max(this.#playbackTime === null ? input.playbackTime : input.playbackTime - this.#playbackTime, 0);
    this.#animationId = input.animationId;
    this.#playbackTime = input.playbackTime;
    this.#rangeStart=input.rangeStart;this.#rangeEnd=input.rangeEnd;this.#looping=input.looping;
    const result = output ?? {
      animationId: input.animationId,
      playbackTime: input.playbackTime,
      duration: input.duration,
      looping: input.looping,
      rangeStart:input.rangeStart,rangeEnd:input.rangeEnd,resetWeight:input.resetWeight,
      from,
      delta: f32(delta),
      discontinuity,
    };
    result.animationId = input.animationId;
    result.playbackTime = input.playbackTime;
    result.duration = input.duration;
    result.looping = input.looping;
    result.rangeStart=input.rangeStart;result.rangeEnd=input.rangeEnd;result.resetWeight=input.resetWeight;
    result.from = from;
    result.delta = f32(delta);
    result.discontinuity = discontinuity;
    return result;
  }
}

export function applyPhysicsConstraintV1(
  constraint: RuntimePhysicsConstraintV1,
  deltaSeconds: number,
  referenceScale: number,
  environment: RuntimePhysicsEnvironmentV1,
  state: RuntimePhysicsStateV1,
  bone: RuntimeBoneV1,
  current: AffineV1,
  rootScaleX: number,
  rootScaleY: number,
  budget: RuntimePhysicsBudgetV1,
  scratch: RuntimePhysicsApplyScratchV1 | null = null,
): RuntimePhysicsApplyResultV1 | null {
  if (constraint.mix === 0 || constraint.fps === 0 || !(referenceScale > 0)
    || !Number.isFinite(referenceScale) || !environmentFinite(environment)) return null;
  const delta = Number.isFinite(deltaSeconds) ? Math.max(deltaSeconds, 0) : 0;
  const enabled = [constraint.x > 0, constraint.y > 0, constraint.rotate > 0 || constraint.shearX > 0, constraint.scaleX > 0];
  const count = !state.reset && enabled.some(Boolean) && delta > 0
    ? Math.ceil(Math.fround(delta * constraint.fps)) : 0;
  if (!Number.isFinite(count) || count > budget.remaining) {
    throw new RuntimeErrorV1("resourceLimit", "apply", "Physics fixed-step budget is exhausted.");
  }
  const next = { ...state }, matrix = { ...current };
  const length = Math.max(bone.length, 0), axisX = length * current.a, axisY = length * current.b;
  let requiredLimit: number | null = 0, limitSaturated = false;
  const observe = (movement: number, limit: number, scale: number): void => {
    limitSaturated ||= Math.abs(movement) > limit;
    const rounded = Math.abs(Math.fround(movement));
    if (rounded === 0 || requiredLimit === null) return;
    const denominator = f32Mul(delta, Math.abs(scale)), candidate = f32Div(rounded, denominator);
    requiredLimit = denominator === 0 || !Number.isFinite(candidate) ? null : Math.max(requiredLimit, candidate);
  };
  if (!state.reset) {
    const inertia = clamp(constraint.inertia, 0, 1), limit = Math.max(constraint.limit, 0);
    const motionX = (current.tx - state.restX) * inertia, motionY = (current.ty - state.restY) * inertia;
    const limitX = limit * delta * Math.abs(rootScaleX), limitY = limit * delta * Math.abs(rootScaleY);
    if (enabled[0] || enabled[2] || enabled[3]) observe(motionX, limitX, rootScaleX);
    if (enabled[1] || enabled[2] || enabled[3]) observe(motionY, limitY, rootScaleY);
    const pivotX = clamp(motionX, -limitX, limitX), pivotY = clamp(motionY, -limitY, limitY);
    if (enabled[0]) next.xOffset -= pivotX;
    if (enabled[1]) next.yOffset -= pivotY;
    const axisLength = Math.hypot(axisX, axisY), previousLength = Math.hypot(state.axisX, state.axisY);
    if (axisLength > 0 && previousLength > 0) {
      const ux = axisX / axisLength, uy = axisY / axisLength;
      if (enabled[2]) next.angleOffset -= inertia * wrapRadians(Math.atan2(axisY, axisX) - Math.atan2(state.axisY, state.axisX))
        + (ux * pivotY - uy * pivotX) / axisLength;
      if (enabled[3]) next.logStretch -= inertia * (Math.log(axisLength) - Math.log(previousLength))
        + (ux * pivotX + uy * pivotY) / axisLength;
    }
    const mass = constraint.mass;
    if (!Number.isFinite(mass) || mass <= 0) physicsFailure();
    const stiffness = Math.max(constraint.strength, 0) / mass, drag = 60 * (1 - clamp(constraint.damping, 0, 1));
    const forceX = referenceScale * (constraint.wind * environment.windX + constraint.gravity * environment.gravityX) * rootScaleX;
    const forceY = -referenceScale * (constraint.wind * environment.windY + constraint.gravity * environment.gravityY) * rootScaleY;
    const ax = forceX / mass, ay = forceY / mass;
    const angular = (axisX * forceY - axisY * forceX) / (referenceScale * referenceScale * mass);
    const axial = (axisX * forceX + axisY * forceY) / (referenceScale * referenceScale * mass);
    const interval = count > 0 ? delta / count : 0;
    for (let step = 0; step < count; step += 1) {
      if (enabled[0]) [next.xOffset, next.xVelocity] = advanceSpring(next.xOffset, next.xVelocity, interval, ax, stiffness, drag);
      if (enabled[1]) [next.yOffset, next.yVelocity] = advanceSpring(next.yOffset, next.yVelocity, interval, ay, stiffness, drag);
      if (enabled[2]) [next.angleOffset, next.angleVelocity] = advanceSpring(next.angleOffset, next.angleVelocity, interval, angular, stiffness, drag);
      if (enabled[3]) [next.logStretch, next.stretchVelocity] = advanceSpring(next.logStretch, next.stretchVelocity, interval, axial, stiffness, drag);
    }
  }
  next.reset = false; next.restX = current.tx; next.restY = current.ty; next.axisX = axisX; next.axisY = axisY;
  if (!enabled[0]) next.xOffset = next.xVelocity = 0;
  if (!enabled[1]) next.yOffset = next.yVelocity = 0;
  if (!enabled[2]) next.angleOffset = next.angleVelocity = 0;
  if (!enabled[3]) next.logStretch = next.stretchVelocity = 0;
  const mix = constraint.mix;
  matrix.tx = f32(current.tx + next.xOffset * mix * constraint.x);
  matrix.ty = f32(current.ty + next.yOffset * mix * constraint.y);
  if (constraint.rotate > 0) rotateBasis(matrix, f32(next.angleOffset * mix * constraint.rotate), false);
  if (constraint.shearX > 0) rotateBasis(matrix, f32(next.angleOffset * mix * constraint.shearX), true);
  if (enabled[3]) {
    const log = next.logStretch * mix * constraint.scaleX, factor = Math.exp(log);
    const yFactor = constraint.scaleYMode === "uniform" ? factor : constraint.scaleYMode === "volume" ? Math.exp(-log) : 1;
    matrix.a = f32(matrix.a * factor); matrix.b = f32(matrix.b * factor);
    matrix.c = f32(matrix.c * yFactor); matrix.d = f32(matrix.d * yFactor);
  }
  const relativeScale = Math.exp(next.logStretch);
  const diagnostic: RuntimePhysicsConstraintDiagnosticV1 = {
    type: "physics", fixedSteps: count, translationOffset: f32(Math.hypot(next.xOffset, next.yOffset)),
    translationSpeed: f32(Math.hypot(next.xVelocity, next.yVelocity)),
    rotationOffsetDegrees: f32(next.angleOffset * (180 / Math.PI)), angularSpeedDegrees: f32(next.angleVelocity * (180 / Math.PI)),
    scaleOffset: f32(relativeScale - 1), scaleSpeed: f32(relativeScale * next.stretchVelocity),
    configuredLimit: Math.max(constraint.limit, 0), requiredLimit, limitSaturated,
  };
  if (!stateFinite(next) || !matrixFinite(matrix) || !diagnosticFinite(diagnostic)) physicsFailure();
  copyPhysicsStateV1(state, next); budget.remaining -= count;
  if (scratch !== null) {
    Object.assign(scratch.matrix, matrix); Object.assign(scratch.diagnostic, diagnostic); return scratch.result;
  }
  return { matrix, diagnostic };
}

function physicsFailure(): never { throw new RuntimeErrorV1("nonFinite", "apply", "Physics state or geometry cannot be represented."); }
function defaultState(): RuntimePhysicsStateV1 {
  return { reset: true, restX: 0, restY: 0, axisX: 0, axisY: 0, xOffset: 0, yOffset: 0, xVelocity: 0, yVelocity: 0, angleOffset: 0, angleVelocity: 0, logStretch: 0, stretchVelocity: 0 };
}
function copyPhysicsStateV1(target: RuntimePhysicsStateV1, source: RuntimePhysicsStateV1): void { Object.assign(target, source); }
function resetPhysicsStateV1(state: RuntimePhysicsStateV1): void { Object.assign(state, defaultState()); }

interface RuntimeRootDeltaV1 {
  readonly a: number;
  readonly b: number;
  readonly c: number;
  readonly d: number;
  readonly tx: number;
  readonly ty: number;
}

type MutableRuntimeRootDeltaV1 = {
  -readonly [Key in keyof RuntimeRootDeltaV1]: RuntimeRootDeltaV1[Key]
};

function rootDeltaV1(
  previous: RootTransformV1,
  next: RootTransformV1,
  output: MutableRuntimeRootDeltaV1,
): RuntimeRootDeltaV1 {
  const previousRadians = f32Mul(previous.rotationDegrees, F32_DEGREES_TO_RADIANS);
  const previousCosine = f32(Math.cos(previousRadians));
  const previousSine = f32(Math.sin(previousRadians));
  const pa = f32Mul(previousCosine, previous.scaleX);
  const pb = f32Mul(previousSine, previous.scaleX);
  const pc = f32Mul(-previousSine, previous.scaleY);
  const pd = f32Mul(previousCosine, previous.scaleY);
  const determinant = f32Sub(f32Mul(pa, pd), f32Mul(pc, pb));
  if (!Number.isFinite(determinant) || determinant === 0) {
    throw new RuntimeErrorV1(
      "invalidState",
      "setRootTransform",
      "Preserving Physics history requires an invertible previous root transform.",
      { field: "rootTransform" },
    );
  }
  const inverseA = f32Div(pd, determinant);
  const inverseB = f32Div(-pb, determinant);
  const inverseC = f32Div(-pc, determinant);
  const inverseD = f32Div(pa, determinant);
  const inverseTx = f32Add(f32Mul(-inverseA, previous.x), f32Mul(-inverseC, previous.y));
  const inverseTy = f32Add(f32Mul(-inverseB, previous.x), f32Mul(-inverseD, previous.y));

  const nextRadians = f32Mul(next.rotationDegrees, F32_DEGREES_TO_RADIANS);
  const nextCosine = f32(Math.cos(nextRadians));
  const nextSine = f32(Math.sin(nextRadians));
  const na = f32Mul(nextCosine, next.scaleX);
  const nb = f32Mul(nextSine, next.scaleX);
  const nc = f32Mul(-nextSine, next.scaleY);
  const nd = f32Mul(nextCosine, next.scaleY);
  output.a = f32Add(f32Mul(na, inverseA), f32Mul(nc, inverseB));
  output.b = f32Add(f32Mul(nb, inverseA), f32Mul(nd, inverseB));
  output.c = f32Add(f32Mul(na, inverseC), f32Mul(nc, inverseD));
  output.d = f32Add(f32Mul(nb, inverseC), f32Mul(nd, inverseD));
  output.tx = f32Add(f32Add(f32Mul(na, inverseTx), f32Mul(nc, inverseTy)), next.x);
  output.ty = f32Add(f32Add(f32Mul(nb, inverseTx), f32Mul(nd, inverseTy)), next.y);
  if (!Number.isFinite(output.a) || !Number.isFinite(output.b)
    || !Number.isFinite(output.c) || !Number.isFinite(output.d)
    || !Number.isFinite(output.tx) || !Number.isFinite(output.ty)) {
    throw new RuntimeErrorV1(
      "nonFinite",
      "setRootTransform",
      "Root motion delta exceeded binary32 range.",
      { field: "rootTransform" },
    );
  }
  return output;
}

function transportedState(state: RuntimePhysicsStateV1, delta: RuntimeRootDeltaV1): RuntimePhysicsStateV1 {
  const vector = (x: number, y: number): readonly [number, number] => [delta.a * x + delta.c * y, delta.b * x + delta.d * y];
  const next = { ...state };
  [next.xOffset, next.yOffset] = vector(state.xOffset, state.yOffset);
  [next.xVelocity, next.yVelocity] = vector(state.xVelocity, state.yVelocity);
  if (!state.reset) {
    const [originX, originY] = vector(state.restX, state.restY);
    const [axisX, axisY] = vector(state.axisX, state.axisY);
    const sine = Math.sin(state.angleOffset), cosine = Math.cos(state.angleOffset), scale = Math.exp(state.logStretch);
    const displacedX = scale * (cosine * state.axisX - sine * state.axisY);
    const displacedY = scale * (sine * state.axisX + cosine * state.axisY);
    const [movedX, movedY] = vector(displacedX, displacedY);
    const [velocityX, velocityY] = vector(displacedX * state.stretchVelocity - displacedY * state.angleVelocity,
      displacedY * state.stretchVelocity + displacedX * state.angleVelocity);
    const restLength = Math.hypot(axisX, axisY), movedLength = Math.hypot(movedX, movedY);
    if (restLength > 0 && movedLength > 0) {
      const ux = movedX / movedLength, uy = movedY / movedLength;
      const principal = wrapRadians(Math.atan2(movedY, movedX) - Math.atan2(axisY, axisX));
      const orientation = delta.a * delta.d - delta.b * delta.c < 0 ? -1 : 1;
      const turnCount = (orientation * state.angleOffset - principal) / (2 * Math.PI);
      const turns = Math.sign(turnCount) * Math.floor(Math.abs(turnCount) + 0.5);
      next.angleOffset = principal + turns * (2 * Math.PI);
      next.angleVelocity = (ux * velocityY - uy * velocityX) / movedLength;
      next.logStretch = Math.log(movedLength) - Math.log(restLength);
      next.stretchVelocity = (ux * velocityX + uy * velocityY) / movedLength;
    } else next.angleOffset = next.angleVelocity = next.logStretch = next.stretchVelocity = 0;
    next.restX = originX + delta.tx; next.restY = originY + delta.ty; next.axisX = axisX; next.axisY = axisY;
  }
  return next;
}
function transformedPhysicsStateFiniteV1(state: RuntimePhysicsStateV1, delta: RuntimeRootDeltaV1): boolean { return stateFinite(transportedState(state, delta)); }
function transformPhysicsStateV1(state: RuntimePhysicsStateV1, delta: RuntimeRootDeltaV1): void { Object.assign(state, transportedState(state, delta)); }
function clearPhysicsInertiaV1(state: RuntimePhysicsStateV1): void { state.xVelocity = state.yVelocity = state.angleVelocity = state.stretchVelocity = 0; }

function rotateBasis(matrix: { a: number; b: number; c: number; d: number }, radians: number, firstOnly: boolean): void {
  const cosine = f32(Math.cos(radians));
  const sine = f32(Math.sin(radians));
  const a = matrix.a;
  const b = matrix.b;
  matrix.a = f32Sub(f32Mul(cosine, a), f32Mul(sine, b));
  matrix.b = f32Add(f32Mul(sine, a), f32Mul(cosine, b));
  if (!firstOnly) {
    const c = matrix.c;
    const d = matrix.d;
    matrix.c = f32Sub(f32Mul(cosine, c), f32Mul(sine, d));
    matrix.d = f32Add(f32Mul(sine, c), f32Mul(cosine, d));
  }
}

function environmentFinite(value: RuntimePhysicsEnvironmentV1): boolean {
  return Number.isFinite(value.windX) && Number.isFinite(value.windY)
    && Number.isFinite(value.gravityX) && Number.isFinite(value.gravityY);
}

function stateFinite(state: RuntimePhysicsStateV1): boolean {
  return Number.isFinite(state.restX)
    && Number.isFinite(state.restY)
    && Number.isFinite(state.axisX)
    && Number.isFinite(state.axisY)
    && Number.isFinite(state.xOffset)
    && Number.isFinite(state.yOffset)
    && Number.isFinite(state.xVelocity)
    && Number.isFinite(state.yVelocity)
    && Number.isFinite(state.angleOffset)
    && Number.isFinite(state.angleVelocity)
    && Number.isFinite(state.logStretch)
    && Number.isFinite(state.stretchVelocity);
}

function matrixFinite(matrix: AffineV1): boolean {
  return Number.isFinite(matrix.a) && Number.isFinite(matrix.b)
    && Number.isFinite(matrix.c) && Number.isFinite(matrix.d)
    && Number.isFinite(matrix.tx) && Number.isFinite(matrix.ty);
}

function diagnosticFinite(diagnostic: RuntimePhysicsConstraintDiagnosticV1): boolean {
  return Number.isFinite(diagnostic.fixedSteps)
    && Number.isFinite(diagnostic.translationOffset)
    && Number.isFinite(diagnostic.translationSpeed)
    && Number.isFinite(diagnostic.rotationOffsetDegrees)
    && Number.isFinite(diagnostic.angularSpeedDegrees)
    && Number.isFinite(diagnostic.scaleOffset)
    && Number.isFinite(diagnostic.scaleSpeed)
    && Number.isFinite(diagnostic.configuredLimit)
    && (diagnostic.requiredLimit === null || Number.isFinite(diagnostic.requiredLimit));
}

function wrapRadians(value: number): number {
  const full = Math.PI * 2;
  return ((value + Math.PI) % full + full) % full - Math.PI;
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), maximum);
}
