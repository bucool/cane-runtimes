import { RuntimeErrorV1 } from "../errors.js";
import { applyEndpointLayerV1 } from "../animation/endpoint-pose.js";
import { solveAdaptivePathV1 } from "./path-constraint.js";
import { RuntimePathSamplerWorkspaceV1, type PathPointV1, type PathSampleV1 } from "./path-sampler.js";
import { solveAffineIkV1 } from "./ik.js";
import { solveTransformChannelsV1 } from "./transform.js";
import { inverseTransformPoint, localChannels, worldChannels, narrowTransform, recoverLocalTransform, wrapTransformAngle } from "../math/transform-channels.js";
import type {
  AffineV1,
  RuntimeBoneV1,
  RuntimeConstraintV1,
  RuntimeConstraintDiagnosticV1,
  RuntimeIkConstraintV1,
  RuntimePathAttachmentV1,
  RuntimePathConstraintV1,
  RuntimePathConstraintPositionV1,
  RuntimePhysicsEnvironmentV1,
  RuntimeSamplingV1,
  RuntimeSkinV1,
  RuntimeSliderConstraintV1,
  RuntimeSlotV1,
  RuntimeTransformConstraintV1,
  RuntimeTransformConstraintOffsetsV1,
  RuntimeTransformPropertyV1,
} from "../contracts.js";
import {
  childWorldAffineV1,
  determinantV1,
  localBoneAffineV1,
  multiplyAffineV1,
  transformPointV1,
} from "../math/affine.js";
import {
  F32_DEGREES_TO_RADIANS,
  F32_RADIANS_TO_DEGREES,
  f32,
  f32Add,
  f32MatrixAxisLength,
  f32Mul,
} from "../math/f32.js";
import type { RuntimeDataV1 } from "../data.js";
import {
  type RuntimeSampledDeformV1,
  type RuntimeSampledPoseV1,
} from "../animation/sampling.js";
import { quantizeSampleTimeV1 } from "../animation/curves.js";
import { evaluateVertexAttachmentWorldV1 } from "../geometry/vertices.js";
import {
  applyPhysicsConstraintV1,
  createRuntimePhysicsApplyScratchV1,
  type RuntimePhysicsApplyScratchV1,
  type RuntimePhysicsBudgetV1,
  RuntimePhysicsStateStoreV1,
} from "./physics.js";

const MATRIX_EPSILON = 1.1920928955078125e-7;
const IK_MIN_VECTOR_SQUARED = 0.000001;

interface SkinMembershipCacheV1 {
  readonly bones: ReadonlyMap<string, readonly string[]>;
  readonly constraints: ReadonlyMap<string, readonly string[]>;
}

const SKIN_MEMBERSHIP_CACHE = new WeakMap<object, SkinMembershipCacheV1>();

type Mutable<T> = { -readonly [Property in keyof T]: T[Property] };
type MutableConstraintSolveResultV1 = {
  -readonly [Property in keyof RuntimeConstraintSolveResultV1]: RuntimeConstraintSolveResultV1[Property]
};

export interface RuntimeConstraintSolveScratchV1 {
  readonly runtimeConstraintSolveScratchV1: true;
}

interface ConstraintSolveScratchStorageV1 {
  readonly boneById: Map<string, Mutable<RuntimeBoneV1>>;
  readonly boneIndexById: Map<string, number>;
  readonly slotById: Map<string, RuntimeSlotV1>;
  readonly worlds: Map<string, AffineV1>;
  readonly physicsApply: Map<string, RuntimePhysicsApplyScratchV1>;
  readonly pathSolve: Map<string, RuntimePathSolveScratchV1>;
  readonly diagnostics: Map<string, RuntimeConstraintDiagnosticV1>;
  readonly diagnosticEpochById: Map<string, number>;
  readonly reconstruction: RuntimeLocalReconstructionScratchV1;
  diagnosticEpoch: number;
  result: MutableConstraintSolveResultV1 | null;
}

interface RuntimeLocalReconstructionScratchV1 {
  readonly inverse: AffineV1;
  readonly local: AffineV1;
  readonly position: [number, number];
  candidate: Mutable<RuntimeBoneV1> | null;
  readonly matrix: AffineV1;
  readonly nextMatrix: AffineV1;
  readonly residual: number[];
  readonly jacobian: number[];
  readonly augmented: number[];
  readonly correction: number[];
}

interface RuntimePathSolveScratchV1 {
  readonly flatPoints: number[];
  readonly sampler: RuntimePathSamplerWorkspaceV1;
}

const CONSTRAINT_SOLVE_SCRATCH = new WeakMap<
  RuntimeConstraintSolveScratchV1,
  ConstraintSolveScratchStorageV1
>();

export function createRuntimeConstraintSolveScratchV1(): RuntimeConstraintSolveScratchV1 {
  const scratch = Object.freeze({ runtimeConstraintSolveScratchV1: true as const });
  CONSTRAINT_SOLVE_SCRATCH.set(scratch, {
    boneById: new Map(),
    boneIndexById: new Map(),
    slotById: new Map(),
    worlds: new Map(),
    physicsApply: new Map(),
    pathSolve: new Map(),
    diagnostics: new Map(),
    diagnosticEpochById: new Map(),
    reconstruction: createLocalReconstructionScratchV1(),
    diagnosticEpoch: 0,
    result: null,
  });
  return scratch;
}

export interface RuntimeConstraintSolveResultV1 extends RuntimeSampledPoseV1 {
  readonly worldByBoneId: ReadonlyMap<string, AffineV1>;
}

export interface RuntimeConstraintSolveContextV1 {
  readonly data: RuntimeDataV1;
  readonly slots: readonly RuntimeSlotV1[];
  readonly deforms: ReadonlyMap<string, RuntimeSampledDeformV1>;
  readonly physicsStates: RuntimePhysicsStateStoreV1;
  readonly physicsDeltaSeconds: number;
  readonly physicsEnvironment: RuntimePhysicsEnvironmentV1;
  readonly physicsBudget: RuntimePhysicsBudgetV1;
  readonly referenceScale: number;
  readonly rootScaleX: number;
  readonly rootScaleY: number;
  readonly sampling: RuntimeSamplingV1;
  /** Internal trusted workspace used by RuntimePlayer performance mode. */
  readonly scratch?: RuntimeConstraintSolveScratchV1 | null;
}

export function solveRuntimeConstraintsV1(
  sampledPose: RuntimeSampledPoseV1,
  skins: readonly RuntimeSkinV1[],
  root: AffineV1,
  _rootRotationDegrees = 0,
  context: RuntimeConstraintSolveContextV1 | null = null,
): RuntimeConstraintSolveResultV1 {
  const scratch = context?.scratch === undefined || context.scratch === null
    ? null
    : constraintSolveScratchStorageV1(context.scratch);
  let pose = sampledPose;
  let bones = scratch === null
    ? cloneBonesV1(pose.bones)
    : pose.bones as Mutable<RuntimeBoneV1>[];
  let boneById = indexBonesV1(bones, scratch?.boneById ?? null);
  let boneIndexById = indexBoneIndicesV1(bones, scratch?.boneIndexById ?? null);
  let slotById = indexSlotsV1(pose.slots, scratch?.slotById ?? null);
  let constraints = pose.constraints;
  let sampledSliderTimes: ReadonlyMap<string, number> = pose.sampledSliderTimes;
  const diagnosticEpoch = scratch === null ? 0 : beginConstraintDiagnosticsV1(scratch);
  let constraintDiagnostics: ReadonlyMap<string, RuntimeConstraintDiagnosticV1> = scratch === null
    ? pose.constraintDiagnostics
    : scratch.diagnostics;
  if (scratch !== null) retainConstraintDiagnosticsV1(scratch, pose.constraintDiagnostics, diagnosticEpoch);
  let constraintDiagnosticsMutable = scratch !== null;
  let worlds = buildWorlds(bones, root, scratch?.worlds ?? null);
  const declarations = context?.data.document.constraints ?? constraints;
  for (let declarationIndex = 0; declarationIndex < declarations.length; declarationIndex += 1) {
    const declaration = declarations[declarationIndex];
    if (declaration === undefined) continue;
    const orderedConstraint = constraints[declarationIndex];
    const constraint = orderedConstraint?.id === declaration.id
      ? orderedConstraint
      : constraintByIdV1(constraints, declaration.id);
    if (constraint === undefined) continue;
    if (!memberIsActive(constraint.id, "constraint", skins, pose.sampledSkinIds)) continue;
    if (constraint.type === "ik") {
      if (constraint.targetBoneId !== null
        && !memberIsActive(constraint.targetBoneId, "bone", skins, pose.sampledSkinIds)) continue;
      const diagnostic = constraintResult(constraint.id, () => solveAffineIkV1(constraint, bones, boneById, root, worlds));
      if (diagnostic !== null) {
        if (!constraintDiagnosticsMutable) { constraintDiagnostics = new Map(constraintDiagnostics); constraintDiagnosticsMutable = true; }
        (constraintDiagnostics as Map<string, RuntimeConstraintDiagnosticV1>).set(constraint.id, diagnostic);
        if (scratch !== null) scratch.diagnosticEpochById.set(constraint.id, diagnosticEpoch);
      }
    } else if (constraint.type === "transform") {
      if (!memberIsActive(constraint.targetBoneId, "bone", skins, pose.sampledSkinIds)) continue;
      const diagnostic = constraintResult(constraint.id, () => solveTransformChannelsV1(constraint, bones, boneById, root, worlds));
      if (diagnostic !== null) {
        if (!constraintDiagnosticsMutable) { constraintDiagnostics = new Map(constraintDiagnostics); constraintDiagnosticsMutable = true; }
        (constraintDiagnostics as Map<string, RuntimeConstraintDiagnosticV1>).set(constraint.id, diagnostic);
        if (scratch !== null) scratch.diagnosticEpochById.set(constraint.id, diagnosticEpoch);
      }
    } else if (constraint.type === "path" && context !== null) {
      const slot = slotById.get(constraint.targetSlotId);
      if (slot === undefined || !memberIsActive(slot.boneId, "bone", skins, pose.sampledSkinIds)) continue;
      if (slot.attachmentId === null) continue;
      const attachment = context.data.attachment(slot.attachmentId), slotWorld = worlds.get(slot.boneId);
      if (attachment.type !== "path" || slotWorld === undefined) continue;
      const workspace = scratch === null ? null : reusablePathSolveV1(scratch, constraint.id);
      const flat = evaluateVertexAttachmentWorldV1(attachment, slotWorld, worlds, pose.deforms.get(attachment.id) ?? null, workspace?.flatPoints ?? null);
      const diagnostic = constraintResult(constraint.id, () => solveAdaptivePathV1(constraint, attachment, flat, slotWorld, bones, boneById, root, worlds, workspace?.sampler ?? null));
      if (diagnostic !== null) {
        if (!constraintDiagnosticsMutable) { constraintDiagnostics = new Map(constraintDiagnostics); constraintDiagnosticsMutable = true; }
        (constraintDiagnostics as Map<string, RuntimeConstraintDiagnosticV1>).set(constraint.id, diagnostic);
        if (scratch !== null) scratch.diagnosticEpochById.set(constraint.id, diagnosticEpoch);
      }
    } else if (constraint.type === "physics" && context !== null) {
      if (!memberIsActive(constraint.boneId, "bone", skins, pose.sampledSkinIds)) continue;
      const bone = boneById.get(constraint.boneId);
      const current = worlds.get(constraint.boneId);
      if (bone === undefined || current === undefined) continue;
      const applied = applyPhysicsConstraintV1(
        constraint,
        context.physicsDeltaSeconds,
        context.referenceScale,
        context.physicsEnvironment,
        context.physicsStates.state(constraint.id),
        bone,
        current,
        context.rootScaleX,
        context.rootScaleY,
        context.physicsBudget,
        scratch === null ? null : reusablePhysicsApplyV1(scratch, constraint.id),
      );
      if (applied === null) continue;
      const nextWorld = applied.matrix;
      if (!constraintDiagnosticsMutable) {
        constraintDiagnostics = new Map(constraintDiagnostics);
        constraintDiagnosticsMutable = true;
      }
      (constraintDiagnostics as Map<string, RuntimeConstraintDiagnosticV1>).set(constraint.id, applied.diagnostic);
      if (scratch !== null) scratch.diagnosticEpochById.set(constraint.id, diagnosticEpoch);
      const index = boneIndexById.get(bone.id) ?? -1;
      reconstructLocalPose(bone, nextWorld, root, worlds, scratch?.reconstruction ?? null);
      worlds.set(bone.id, nextWorld);
      if (index >= 0) worlds = propagateDescendants(index, bones, worlds, boneIndexById);
    } else if (constraint.type === "slider" && context !== null) {
      const currentPose: RuntimeSampledPoseV1 = {
        ...pose,
        bones,
        constraints,
        sampledSliderTimes,
        constraintDiagnostics,
      };
      const applied = solveSlider(
        constraint,
        currentPose,
        worlds,
        root,
        skins,
        context,
      );
      if (applied === null) continue;
      pose = applied.pose;
      bones = scratch === null
        ? cloneBonesV1(pose.bones)
        : pose.bones as Mutable<RuntimeBoneV1>[];
      boneById = indexBonesV1(bones, scratch?.boneById ?? null);
      boneIndexById = indexBoneIndicesV1(bones, scratch?.boneIndexById ?? null);
      slotById = indexSlotsV1(pose.slots, scratch?.slotById ?? null);
      constraints = pose.constraints;
      sampledSliderTimes = scratch === null
        ? new Map(pose.sampledSliderTimes)
        : pose.sampledSliderTimes;
      (sampledSliderTimes as Map<string, number>).set(constraint.id, applied.resolvedTime);
      if (scratch === null) {
        constraintDiagnostics = new Map(pose.constraintDiagnostics);
      } else {
        retainConstraintDiagnosticsV1(scratch, pose.constraintDiagnostics, diagnosticEpoch);
        constraintDiagnostics = scratch.diagnostics;
      }
      constraintDiagnosticsMutable = true;
      (constraintDiagnostics as Map<string, RuntimeConstraintDiagnosticV1>).set(constraint.id, applied.diagnostic);
      if (scratch !== null) scratch.diagnosticEpochById.set(constraint.id, diagnosticEpoch);
      if (context.data.animation(constraint.animationId).boneTimelines.length > 0) {
        worlds = buildWorlds(bones, root, scratch?.worlds ?? worlds);
      }
    }
  }
  if (scratch !== null) {
    finishConstraintDiagnosticsV1(scratch, diagnosticEpoch);
    let result = scratch.result;
    if (result === null) {
      result = { ...pose, worldByBoneId: worlds };
      scratch.result = result;
    } else {
      result.bones = pose.bones;
      result.slots = pose.slots;
      result.regions = pose.regions;
      result.deforms = pose.deforms;
      result.sequenceIndices = pose.sequenceIndices;
      result.slotAttachmentKeys = pose.slotAttachmentKeys;
      result.sampledSkinIds = pose.sampledSkinIds;
      result.constraints = pose.constraints;
      result.drawOrderSlotIds = pose.drawOrderSlotIds;
      result.drawOrderSampled = pose.drawOrderSampled;
      result.sampledSliderTimes = pose.sampledSliderTimes;
      result.constraintDiagnostics = pose.constraintDiagnostics;
    }
    result.bones = bones;
    result.constraints = constraints;
    result.sampledSliderTimes = sampledSliderTimes;
    result.constraintDiagnostics = constraintDiagnostics;
    result.worldByBoneId = worlds;
    return result;
  }
  return { ...pose, bones, constraints, sampledSliderTimes, constraintDiagnostics, worldByBoneId: worlds };
}

function cloneBonesV1(source: readonly RuntimeBoneV1[]): Mutable<RuntimeBoneV1>[] {
  const result = new Array<Mutable<RuntimeBoneV1>>(source.length);
  for (let index = 0; index < source.length; index += 1) {
    const bone = source[index];
    if (bone !== undefined) result[index] = { ...bone };
  }
  return result;
}

function indexBonesV1(
  bones: readonly Mutable<RuntimeBoneV1>[],
  output: Map<string, Mutable<RuntimeBoneV1>> | null = null,
): Map<string, Mutable<RuntimeBoneV1>> {
  const result = output ?? new Map<string, Mutable<RuntimeBoneV1>>();
  if (!indexedMapShapeMatchesV1(result, bones)) result.clear();
  for (let index = 0; index < bones.length; index += 1) {
    const bone = bones[index];
    if (bone !== undefined) result.set(bone.id, bone);
  }
  return result;
}

function indexBoneIndicesV1(
  bones: readonly RuntimeBoneV1[],
  output: Map<string, number> | null = null,
): Map<string, number> {
  const result = output ?? new Map<string, number>();
  if (!indexedMapShapeMatchesV1(result, bones)) result.clear();
  for (let index = 0; index < bones.length; index += 1) {
    const bone = bones[index];
    if (bone !== undefined) result.set(bone.id, index);
  }
  return result;
}

function indexSlotsV1(
  slots: readonly RuntimeSlotV1[],
  output: Map<string, RuntimeSlotV1> | null = null,
): Map<string, RuntimeSlotV1> {
  const result = output ?? new Map<string, RuntimeSlotV1>();
  if (!indexedMapShapeMatchesV1(result, slots)) result.clear();
  for (let index = 0; index < slots.length; index += 1) {
    const slot = slots[index];
    if (slot !== undefined) result.set(slot.id, slot);
  }
  return result;
}

function indexedMapShapeMatchesV1(
  target: ReadonlyMap<string, unknown>,
  values: readonly { readonly id: string }[],
): boolean {
  if (target.size !== values.length) return false;
  for (let index = 0; index < values.length; index += 1) {
    const value = values[index];
    if (value === undefined) return false;
    if (!target.has(value.id)) return false;
  }
  return true;
}

function constraintByIdV1(
  constraints: readonly RuntimeConstraintV1[],
  id: string,
): RuntimeConstraintV1 | undefined {
  for (let index = 0; index < constraints.length; index += 1) {
    const constraint = constraints[index];
    if (constraint === undefined) continue;
    if (constraint.id === id) return constraint;
  }
  return undefined;
}

function constraintSolveScratchStorageV1(
  scratch: RuntimeConstraintSolveScratchV1,
): ConstraintSolveScratchStorageV1 {
  let storage = CONSTRAINT_SOLVE_SCRATCH.get(scratch);
  if (storage === undefined) {
    storage = {
      boneById: new Map(),
      boneIndexById: new Map(),
      slotById: new Map(),
      worlds: new Map(),
          physicsApply: new Map(),
      pathSolve: new Map(),
      diagnostics: new Map(),
      diagnosticEpochById: new Map(),
      reconstruction: createLocalReconstructionScratchV1(),
      diagnosticEpoch: 0,
      result: null,
    };
    CONSTRAINT_SOLVE_SCRATCH.set(scratch, storage);
  }
  return storage;
}

interface RuntimeSliderSolveResultV1 {
  readonly pose: RuntimeSampledPoseV1;
  readonly resolvedTime: number;
  readonly diagnostic: Extract<RuntimeConstraintDiagnosticV1, { readonly type: "slider" }>;
}

function solveSlider(
  constraint: RuntimeSliderConstraintV1,
  pose: RuntimeSampledPoseV1,
  worlds: ReadonlyMap<string, AffineV1>,
  root: AffineV1,
  skins: readonly RuntimeSkinV1[],
  context: RuntimeConstraintSolveContextV1,
): RuntimeSliderSolveResultV1 | null {
  if (!Number.isFinite(constraint.mix) || constraint.mix === 0) return null;
  const animation = context.data.animation(constraint.animationId);
  let mappedTime = constraint.time;
  let sourceValue: number | null = null;
  if (constraint.sourceBoneId !== null) {
    if (!memberIsActive(constraint.sourceBoneId, "bone", skins, pose.sampledSkinIds)) return null;
    const sourceBone = pose.bones.find((bone) => bone.id === constraint.sourceBoneId);
    const sourceWorld = worlds.get(constraint.sourceBoneId);
    if (sourceBone === undefined || sourceWorld === undefined) return null;
    sourceValue = constraint.local
      ? sliderLocalSource(sourceBone, constraint.sourceProperty)
      : sliderWorldSource(sourceWorld, root, constraint.sourceProperty);
    if (sourceValue === null) return null;
    mappedTime = f32(constraint.timeOffset + f32((sourceValue - constraint.sourceOffset) * constraint.timeScale));
    if (!Number.isFinite(mappedTime)) return null;
  }

  const duration = animation.duration;
  let resolvedTime = mappedTime;
  let wrapped = false;
  let clamped = false;
  if (constraint.looping && duration > 0) {
    resolvedTime = f32(((mappedTime % duration) + duration) % duration);
    wrapped = resolvedTime !== mappedTime;
  } else if (constraint.sourceBoneId !== null) {
    resolvedTime = f32(Math.min(Math.max(mappedTime, 0), Math.max(duration, 0)));
    clamped = resolvedTime !== mappedTime;
  }
  if (!Number.isFinite(resolvedTime)) return null;

  let sampleTime = resolvedTime;
  if (context.sampling.mode === "fixedFrame" || context.sampling.mode === "fixedFrameStepped") {
    sampleTime = quantizeSampleTimeV1(resolvedTime, context.sampling.frameStepSeconds);
  }
  const appliedPose = applyEndpointLayerV1(context.data, pose, {
    animation,
    sampleTime,
    alpha: constraint.mix,
    blend: constraint.additive ? "additive" : "replace",
    forceStepped: context.sampling.mode === "forceStepped" || context.sampling.mode === "fixedFrameStepped",
    attachmentsAllowed: true,
    drawOrderAllowed: true,
    unboundedAlpha: true,
  });
  return {
    pose: appliedPose,
    resolvedTime,
    diagnostic: {
      type: "slider",
      sourceValue,
      mappedTimeSeconds: mappedTime,
      resolvedTimeSeconds: resolvedTime,
      targetDurationSeconds: duration,
      wrapped,
      clamped,
    },
  };
}

function sliderLocalSource(bone: RuntimeBoneV1, property: RuntimeTransformPropertyV1): number {
  switch (property) {
    case "rotate": return bone.rotation;
    case "x": return bone.x;
    case "y": return bone.y;
    case "scaleX": return bone.scaleX;
    case "scaleY": return bone.scaleY;
    case "shearY": return bone.shearY;
  }
}

function sliderWorldSource(
  world: AffineV1,
  root: AffineV1,
  property: RuntimeTransformPropertyV1,
): number | null {
  const inverseRoot = inverseAffine(root);
  if (inverseRoot === null) return null;
  const matrix = multiplyAffineV1(inverseRoot, world);
  let value: number;
  switch (property) {
    case "rotate": {
      value = radiansToDegreesV1(Math.atan2(matrix.b, matrix.a));
      if (value < 0) value += 360;
      break;
    }
    case "x": value = matrix.tx; break;
    case "y": value = matrix.ty; break;
    case "scaleX": value = f32MatrixAxisLength(matrix.a, matrix.b); break;
    case "scaleY": value = f32MatrixAxisLength(matrix.c, matrix.d); break;
    case "shearY": {
      value = radiansToDegreesV1(Math.atan2(matrix.d, matrix.c))
        - radiansToDegreesV1(Math.atan2(matrix.b, matrix.a))
        - 90;
      break;
    }
  }
  const rounded = f32(value);
  return Number.isFinite(rounded) ? rounded : null;
}

type PathSamplerV1 = RuntimePathSamplerWorkspaceV1;

export function queryRuntimePathConstraintPositionV1(
  constraint: RuntimePathConstraintV1,
  pose: RuntimeSampledPoseV1,
  worlds: ReadonlyMap<string, AffineV1>,
  data: RuntimeDataV1,
): RuntimePathConstraintPositionV1 | null {
  const sampler = resolvedPathSampler(constraint, pose, worlds, data);
  if (sampler === null) return null;
  const distance = constraint.positionMode === "fixed"
    ? constraint.position
    : f32Mul(constraint.position, sampler.totalLength);
  const sample = sampler.sample(distance);
  const start = sampler.sample(0);
  const end = sampler.sample(sampler.totalLength);
  if (sample === null || start === null || end === null) return null;
  const values = [sample.x, sample.y, sample.tangentDegrees, distance, sampler.totalLength, start.x, start.y, end.x, end.y];
  if (!values.every(Number.isFinite)) return null;
  return {
    constraintId: constraint.id,
    point: { x: f32(sample.x), y: f32(sample.y) },
    tangentDegrees: f32(sample.tangentDegrees),
    distance: f32(distance),
    pathLength: f32(sampler.totalLength),
    pathStart: { x: f32(start.x), y: f32(start.y) },
    pathEnd: { x: f32(end.x), y: f32(end.y) },
    closed: sampler.closed,
  };
}

export function queryRuntimePathConstraintPositionForWorldTargetV1(
  constraint: RuntimePathConstraintV1,
  pose: RuntimeSampledPoseV1,
  worlds: ReadonlyMap<string, AffineV1>,
  data: RuntimeDataV1,
  target: PathPointV1,
): number | null {
  if (!Number.isFinite(target.x) || !Number.isFinite(target.y)) return null;
  const sampler = resolvedPathSampler(constraint, pose, worlds, data);
  if (sampler === null) return null;
  const distance = sampler.projectDistance(target);
  if (distance === null || !Number.isFinite(distance)) return null;
  const position = constraint.positionMode === "fixed" ? distance : distance / sampler.totalLength;
  return Number.isFinite(position) ? f32(position) : null;
}

function resolvedPathSampler(
  constraint: RuntimePathConstraintV1,
  pose: RuntimeSampledPoseV1,
  worlds: ReadonlyMap<string, AffineV1>,
  data: RuntimeDataV1,
): PathSamplerV1 | null {
  const slot = pose.slots.find((candidate) => candidate.id === constraint.targetSlotId);
  if (slot === undefined || slot.attachmentId === null) return null;
  const attachment = data.attachment(slot.attachmentId);
  if (attachment.type !== "path") return null;
  const slotWorld = worlds.get(slot.boneId);
  if (slotWorld === undefined) return null;
  const flat = evaluateVertexAttachmentWorldV1(
    attachment,
    slotWorld,
    worlds,
    pose.deforms.get(attachment.id) ?? null,
  );
  return createPathSampler(attachment, flat);
}

function createPathSampler(path: RuntimePathAttachmentV1, flat: readonly number[]): PathSamplerV1 | null {
  return new RuntimePathSamplerWorkspaceV1().reset(path, flat);
}

function worldShearY(matrix: AffineV1): number {
  return constraintDegrees(radiansToDegreesV1(Math.atan2(-matrix.c, matrix.d)) - xAngle(matrix));
}

function wrapDegrees(value: number): number {
  return ((value + 180) % 360 + 360) % 360 - 180;
}

function propagateDescendants(
  constrainedIndex: number,
  bones: readonly RuntimeBoneV1[],
  worlds: ReadonlyMap<string, AffineV1>,
  boneIndexById: ReadonlyMap<string, number>,
): Map<string, AffineV1> {
  const result = worlds as Map<string, AffineV1>;
  const constrained = bones[constrainedIndex];
  if (constrained === undefined) return result;
  const workspace = descendantWorkspaceV1(result, bones.length);
  workspace.epoch += 1;
  if (workspace.epoch > 0xffff_ffff) {
    workspace.marks.fill(0);
    workspace.epoch = 1;
  }
  const epoch = workspace.epoch;
  workspace.marks[constrainedIndex] = epoch;
  for (let index = constrainedIndex + 1; index < bones.length; index += 1) {
    const bone = bones[index];
    if (bone === undefined || bone.parentId === null) continue;
    const parentIndex = boneIndexById.get(bone.parentId);
    if (parentIndex === undefined || workspace.marks[parentIndex] !== epoch) continue;
    const parent = result.get(bone.parentId);
    if (parent === undefined) continue;
    const reusable = result.get(bone.id) ?? null;
    result.set(bone.id, childWorldAffineV1(parent, bone, bone.transformMode, reusable));
    workspace.marks[index] = epoch;
  }
  return result;
}

interface RuntimeDescendantWorkspaceV1 {
  marks: Uint32Array;
  epoch: number;
}

const DESCENDANT_WORKSPACE_BY_WORLDS = new WeakMap<object, RuntimeDescendantWorkspaceV1>();

function descendantWorkspaceV1(worlds: Map<string, AffineV1>, boneCount: number): RuntimeDescendantWorkspaceV1 {
  let result = DESCENDANT_WORKSPACE_BY_WORLDS.get(worlds);
  if (result === undefined) {
    result = { marks: new Uint32Array(boneCount), epoch: 0 };
    DESCENDANT_WORKSPACE_BY_WORLDS.set(worlds, result);
  } else if (result.marks.length !== boneCount) {
    result.marks = new Uint32Array(boneCount);
    result.epoch = 0;
  }
  return result;
}

function beginConstraintDiagnosticsV1(scratch: ConstraintSolveScratchStorageV1): number {
  scratch.diagnosticEpoch += 1;
  if (!Number.isSafeInteger(scratch.diagnosticEpoch)) {
    scratch.diagnosticEpoch = 1;
    scratch.diagnosticEpochById.clear();
  }
  return scratch.diagnosticEpoch;
}

function retainConstraintDiagnosticsV1(
  scratch: ConstraintSolveScratchStorageV1,
  source: ReadonlyMap<string, RuntimeConstraintDiagnosticV1>,
  epoch: number,
): void {
  for (const [constraintId, diagnostic] of source) {
    scratch.diagnostics.set(constraintId, diagnostic);
    scratch.diagnosticEpochById.set(constraintId, epoch);
  }
}

function finishConstraintDiagnosticsV1(
  scratch: ConstraintSolveScratchStorageV1,
  epoch: number,
): void {
  for (const constraintId of scratch.diagnostics.keys()) {
    if (scratch.diagnosticEpochById.get(constraintId) !== epoch) {
      scratch.diagnostics.delete(constraintId);
    }
  }
}

function reusablePhysicsApplyV1(
  scratch: ConstraintSolveScratchStorageV1,
  constraintId: string,
): RuntimePhysicsApplyScratchV1 {
  let result = scratch.physicsApply.get(constraintId);
  if (result === undefined) {
    result = createRuntimePhysicsApplyScratchV1();
    scratch.physicsApply.set(constraintId, result);
  }
  return result;
}

function reusablePathSolveV1(scratch: ConstraintSolveScratchStorageV1, id: string): RuntimePathSolveScratchV1 {
  let workspace = scratch.pathSolve.get(id);
  if (workspace === undefined) { workspace = { flatPoints: [], sampler: new RuntimePathSamplerWorkspaceV1() }; scratch.pathSolve.set(id, workspace); }
  return workspace;
}

function copyAffineV1(source: AffineV1, output: AffineV1 | null): AffineV1 {
  if (output === null) return { ...source };
  const mutable = output as Mutable<AffineV1>;
  mutable.a = source.a;
  mutable.b = source.b;
  mutable.c = source.c;
  mutable.d = source.d;
  mutable.tx = source.tx;
  mutable.ty = source.ty;
  return output;
}

function createLocalReconstructionScratchV1(): RuntimeLocalReconstructionScratchV1 {
  return {
    inverse: { a: 1, b: 0, c: 0, d: 1, tx: 0, ty: 0 },
    local: { a: 1, b: 0, c: 0, d: 1, tx: 0, ty: 0 },
    position: [0, 0],
    candidate: null,
    matrix: { a: 1, b: 0, c: 0, d: 1, tx: 0, ty: 0 },
    nextMatrix: { a: 1, b: 0, c: 0, d: 1, tx: 0, ty: 0 },
    residual: new Array<number>(4),
    jacobian: new Array<number>(16),
    augmented: new Array<number>(20),
    correction: new Array<number>(4),
  };
}

function reconstructLocalPose(
  previous: Mutable<RuntimeBoneV1>, targetWorld: AffineV1, root: AffineV1,
  worlds: ReadonlyMap<string, AffineV1>, _scratch: RuntimeLocalReconstructionScratchV1 | null,
): boolean {
  const parent = previous.parentId === null ? root : worlds.get(previous.parentId);
  if (parent === undefined) return false;
  try {
    Object.assign(previous, recoverLocalTransform(targetWorld, parent, previous.parentId === null ? "inheritAll" : previous.transformMode, previous));
    return true;
  } catch { return false; }
}

function rotateWorldAxes(
  matrix: AffineV1,
  degrees: number,
  output: AffineV1 | null = null,
): AffineV1 {
  const radians = degreesToRadiansV1(degrees);
  const cosine = Math.cos(radians);
  const sine = Math.sin(radians);
  const a = f32(cosine * matrix.a - sine * matrix.b);
  const b = f32(sine * matrix.a + cosine * matrix.b);
  const c = f32(cosine * matrix.c - sine * matrix.d);
  const d = f32(sine * matrix.c + cosine * matrix.d);
  const result = copyAffineV1(matrix, output);
  const mutable = result as Mutable<AffineV1>;
  mutable.a = a;
  mutable.b = b;
  mutable.c = c;
  mutable.d = d;
  return result;
}

function yAngle(matrix: AffineV1): number {
  return radiansToDegreesV1(Math.atan2(matrix.d, matrix.c));
}

function yScale(matrix: AffineV1): number {
  return f32MatrixAxisLength(matrix.c, matrix.d);
}

function finiteAffine(matrix: AffineV1): boolean {
  return Number.isFinite(matrix.a) && Number.isFinite(matrix.b) && Number.isFinite(matrix.c)
    && Number.isFinite(matrix.d) && Number.isFinite(matrix.tx) && Number.isFinite(matrix.ty);
}

export function buildRuntimeBoneWorldsV1(
  bones: readonly RuntimeBoneV1[],
  root: AffineV1,
): ReadonlyMap<string, AffineV1> {
  return buildWorlds(bones, root);
}

/**
 * Computes the canonical Match offsets that leave the first driven bone at its
 * current sampled pose. Non-canonical property mappings are intentionally not
 * invertible through this convenience projection.
 */
export function queryRuntimeMatchedTransformConstraintOffsetsV1(
  constraint: RuntimeTransformConstraintV1,
  bones: readonly RuntimeBoneV1[],
  worlds: ReadonlyMap<string, AffineV1>,
): RuntimeTransformConstraintOffsetsV1 | null {
  const constrainedId = constraint.boneIds[0];
  if (constrainedId === undefined) return null;
  const sourceLocal = bones.find((bone) => bone.id === constraint.targetBoneId);
  const constrainedLocal = bones.find((bone) => bone.id === constrainedId);
  const sourceWorld = worlds.get(constraint.targetBoneId);
  const constrainedWorld = worlds.get(constrainedId);
  if (sourceLocal === undefined || constrainedLocal === undefined
    || sourceWorld === undefined || constrainedWorld === undefined) return null;

  let localSource = constraint.local;
  let localTarget = constraint.local;
  if (constraint.mapping !== null) {
    if (!isCanonicalTransformMappingV1(constraint.mapping)) return null;
    localSource = constraint.mapping.localSource;
    localTarget = constraint.mapping.localTarget;
  }

  try {
    const desired = constraint.relative ? [0, 0, 0, 1, 1, 0] : localTarget ? localChannels(constrainedLocal) : worldChannels(constrainedWorld);
    const source = localSource ? localChannels(sourceLocal) : worldChannels(sourceWorld);
    const values = desired.map((v, i) => v - source[i]!);
    if (!constraint.relative) { values[0] = wrapTransformAngle(values[0]!); values[5] = wrapTransformAngle(values[5]!); }
    if (!localSource) { const xy = inverseTransformPoint(sourceWorld, [desired[1]!, desired[2]!]); values[1] = xy[0]; values[2] = xy[1]; }
    const result = values.map(v => narrowTransform(v));
    return { rotationDegrees: result[0]!, x: result[1]!, y: result[2]!, scaleX: result[3]!, scaleY: result[4]!, shearYDegrees: result[5]! };
  } catch { return null; }
}

function isCanonicalTransformMappingV1(mapping: RuntimeTransformConstraintV1["mapping"]): boolean {
  if (mapping === null || mapping.clamp || mapping.properties.length !== 6) return false;
  const properties: readonly RuntimeTransformPropertyV1[] = ["rotate", "x", "y", "scaleX", "scaleY", "shearY"];
  return properties.every((property) => mapping.properties.some((source) => {
    const target = source.targets[0];
    return source.property === property
      && source.offset === 0
      && source.targets.length === 1
      && target?.property === property
      && target.offset === 0
      && target.scale === 1;
  }));
}

function buildWorlds(
  bones: readonly RuntimeBoneV1[],
  root: AffineV1,
  output: Map<string, AffineV1> | null = null,
): Map<string, AffineV1> {
  const worlds = output ?? new Map<string, AffineV1>();
  let shapeMatches = worlds.size === bones.length;
  if (shapeMatches) {
    for (let index = 0; index < bones.length; index += 1) {
      const bone = bones[index];
      if (bone === undefined) {
        shapeMatches = false;
        break;
      }
      if (!worlds.has(bone.id)) {
        shapeMatches = false;
        break;
      }
    }
  }
  if (!shapeMatches) worlds.clear();
  for (let index = 0; index < bones.length; index += 1) {
    const bone = bones[index];
    if (bone === undefined) continue;
    const reusable = worlds.get(bone.id) ?? null;
    const world = bone.parentId === null
      ? multiplyAffineV1(root, localBoneAffineV1(bone, reusable), reusable)
      : childWorldAffineV1(worlds.get(bone.parentId) ?? root, bone, bone.transformMode, reusable);
    if (![world.a, world.b, world.c, world.d, world.tx, world.ty].every(Number.isFinite)) {
      throw new RuntimeErrorV1("invalidState", "apply", "Bone world transform cannot be represented.", { field: "worldTransform", entityId: bone.id });
    }
    worlds.set(bone.id, world);
  }
  return worlds;
}

function inverseAffine(matrix: AffineV1, output: AffineV1 | null = null): AffineV1 | null {
  const determinant = determinantV1(matrix);
  if (!Number.isFinite(determinant) || determinant === 0) return null;
  const a = matrix.d / determinant;
  const b = -matrix.b / determinant;
  const c = -matrix.c / determinant;
  const d = matrix.a / determinant;
  const tx = (matrix.c * matrix.ty - matrix.d * matrix.tx) / determinant;
  const ty = (matrix.b * matrix.tx - matrix.a * matrix.ty) / determinant;
  if (!Number.isFinite(a) || !Number.isFinite(b) || !Number.isFinite(c)
    || !Number.isFinite(d) || !Number.isFinite(tx) || !Number.isFinite(ty)) return null;
  const result = output ?? { a: 1, b: 0, c: 0, d: 1, tx: 0, ty: 0 };
  const mutable = result as Mutable<AffineV1>;
  mutable.a = f32(a);
  mutable.b = f32(b);
  mutable.c = f32(c);
  mutable.d = f32(d);
  mutable.tx = f32(tx);
  mutable.ty = f32(ty);
  return result;
}

function memberIsActive(
  id: string,
  kind: "bone" | "constraint",
  skins: readonly RuntimeSkinV1[],
  activeSkinIds: readonly string[],
): boolean {
  const cache = skinMembershipCacheV1(skins);
  const memberships = (kind === "bone" ? cache.bones : cache.constraints).get(id);
  if (memberships === undefined) return true;
  for (let index = 0; index < memberships.length; index += 1) {
    const skinId = memberships[index];
    if (skinId === undefined) continue;
    if (activeSkinIds.includes(skinId)) return true;
  }
  return false;
}

function skinMembershipCacheV1(skins: readonly RuntimeSkinV1[]): SkinMembershipCacheV1 {
  const existing = SKIN_MEMBERSHIP_CACHE.get(skins);
  if (existing !== undefined) return existing;
  const bones = new Map<string, string[]>();
  const constraints = new Map<string, string[]>();
  for (const skin of skins) {
    for (const boneId of skin.boneIds) appendMembership(bones, boneId, skin.id);
    for (const constraintId of skin.constraintIds) appendMembership(constraints, constraintId, skin.id);
  }
  const created: SkinMembershipCacheV1 = { bones, constraints };
  SKIN_MEMBERSHIP_CACHE.set(skins, created);
  return created;
}

function appendMembership(target: Map<string, string[]>, id: string, skinId: string): void {
  const memberships = target.get(id);
  if (memberships === undefined) target.set(id, [skinId]);
  else memberships.push(skinId);
}

function xAngle(matrix: AffineV1): number {
  return radiansToDegreesV1(Math.atan2(matrix.b, matrix.a));
}

function xScale(matrix: AffineV1): number {
  return f32MatrixAxisLength(matrix.a, matrix.b);
}

function radiansToDegreesV1(radians: number): number {
  return f32Mul(radians, F32_RADIANS_TO_DEGREES);
}

function degreesToRadiansV1(degrees: number): number {
  return f32Mul(degrees, F32_DEGREES_TO_RADIANS);
}

function constraintDegrees(value: number): number {
  const remainder = ((value % 360) + 360) % 360;
  return remainder > 180 ? remainder - 360 : remainder;
}

function constraintAngleDelta(from: number, to: number): number {
  return constraintDegrees(to - from);
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), maximum);
}

function constraintResult<T>(id: string, evaluate: () => T): T {
  try { return evaluate(); } catch (error) {
    if (error instanceof RuntimeErrorV1 && error.entityId === null) throw new RuntimeErrorV1(error.runtimeName, error.operation, error.message, {field: error.field, entityId: id, cause: error});
    throw error;
  }
}
