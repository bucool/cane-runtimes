import type { AffineV1, RuntimeBoneV1, RuntimePathAttachmentV1, RuntimePathConstraintDiagnosticV1, RuntimePathConstraintV1 } from "../contracts.js";
import { childWorldAffineV1, determinantV1, localBoneAffineV1 } from "../math/affine.js";
import { pathGeometryFailure } from "../math/cubic-arc.js";
import { F32_RADIANS_TO_DEGREES, f32, f32Add, f32Div, f32MatrixAxisLength, f32Mul, f32Sub } from "../math/f32.js";
import { recoverLocalTransform } from "../math/transform-channels.js";
import { RuntimePathSamplerWorkspaceV1 } from "./path-sampler.js";

type Mutable<T> = { -readonly [K in keyof T]: T[K] };
const angle = (m: AffineV1) => f32Mul(f32(Math.atan2(m.b, m.a)), F32_RADIANS_TO_DEGREES);
const normAngle = (v: number) => { let r = f32(v % 360); if (r < 0) r = f32Add(r, 360); return r > 180 ? f32Sub(r, 360) : r; };
const deltaAngle = (from: number, to: number) => { let r = f32(f32Add(f32Sub(to, from), 180) % 360); if (r < 0) r = f32Add(r, 360); return f32Sub(r, 180); };
const mix = (from: number, to: number, m: number) => f32Add(from, f32Mul(f32Sub(to, from), Math.max(0, Math.min(1, m))));
const maximum = (old: number | null, value: number) => Number.isFinite(value) ? Math.max(old ?? 0, value) : old;
const finite = (m: AffineV1) => [m.a, m.b, m.c, m.d, m.tx, m.ty].every(Number.isFinite);
export function pathSpacingV1(c: RuntimePathConstraintV1, bone: RuntimeBoneV1 | null, worldLength: number | null, pathLength: number, proportionalLength: number, spacesCount: number): number {
  const setup = Math.max(bone?.length ?? 0, 0), length = worldLength ?? setup;
  switch (c.spacingMode) {
    case "length": return setup > 0 ? f32Div(f32Mul(Math.max(f32Add(setup, c.spacing), 0), length), setup) : c.spacing;
    case "fixed": return setup > 0 ? f32Div(f32Mul(c.spacing, length), setup) : c.spacing;
    case "percent": return f32Mul(c.spacing, pathLength);
    case "proportional": return proportionalLength > 0 ? f32Mul(f32Mul(f32Div(setup > 0 ? length : c.spacing, proportionalLength), c.spacing), pathLength)
      : f32Div(f32Mul(c.spacing, pathLength), Math.max(spacesCount, 1));
  }
}
export function solveAdaptivePathV1(c: RuntimePathConstraintV1, path: RuntimePathAttachmentV1, flat: readonly number[], slotWorld: AffineV1,
  bones: readonly RuntimeBoneV1[], boneById: ReadonlyMap<string, Mutable<RuntimeBoneV1>>, root: AffineV1, worlds: Map<string, AffineV1>, workspace: RuntimePathSamplerWorkspaceV1 | null = null): RuntimePathConstraintDiagnosticV1 | null {
  const sampler = (workspace ?? new RuntimePathSamplerWorkspaceV1()).reset(path, flat); if (sampler === null) return null;
  const members = c.boneIds.map(id => boneById.get(id)), lengths = members.map(bone => {
    const world = bone === undefined ? undefined : worlds.get(bone.id); return bone === undefined || world === undefined ? null : f32Mul(Math.max(bone.length, 0), f32MatrixAxisLength(world.a, world.b));
  });
  const spacingCount = c.rotateMode === "tangent" ? Math.max(members.length - 1, 0) : members.length, spacesCount = c.rotateMode === "tangent" ? members.length : members.length + 1;
  let proportionalLength = 0; for (let i = 0; i < spacingCount; i++) proportionalLength = f32Add(proportionalLength, lengths[i] ?? 0);
  const signedRotation = c.rotation !== 0 && determinantV1(slotWorld) < 0 ? -c.rotation : c.rotation;
  let distance = c.positionMode === "fixed" ? c.position : f32Mul(c.position, sampler.totalLength);
  const desired = new Map<string, AffineV1>();
  const d: Mutable<RuntimePathConstraintDiagnosticV1> = { type: "path", drivenBoneCount: 0, mixRotate: c.mixRotate, mixX: c.mixX, mixY: c.mixY,
    maxTranslationResidual: null, maxRotationResidualDegrees: null, maxScaleResidual: null };
  for (let i = 0; i < members.length; i++) {
    const bone = members[i]; if (bone === undefined) continue;
    if (i > 0) distance = f32Add(distance, pathSpacingV1(c, members[i - 1] ?? null, lengths[i - 1] ?? null, sampler.totalLength, proportionalLength, spacesCount));
    const sample = sampler.sample(distance), current = worlds.get(bone.id); if (current === undefined) continue;
    const next = sampler.sample(f32Add(distance, pathSpacingV1(c, bone, lengths[i] ?? null, sampler.totalLength, proportionalLength, spacesCount)));
    const dx = f32Sub(next.x, sample.x), dy = f32Sub(next.y, sample.y), chainAngle = f32Mul(f32(Math.atan2(dy, dx)), F32_RADIANS_TO_DEGREES);
    const desiredRotation = f32Add(c.rotateMode === "tangent" ? sample.tangentDegrees : chainAngle, signedRotation), currentRotation = angle(current);
    const rotation = f32Add(currentRotation, f32Mul(deltaAngle(currentRotation, desiredRotation), Math.max(0, Math.min(1, c.mixRotate))));
    const currentScale = f32MatrixAxisLength(current.a, current.b), targetLength = f32(Math.sqrt(f32Add(f32Mul(dx, dx), f32Mul(dy, dy))));
    const desiredScale = bone.length !== 0 ? f32Div(targetLength, Math.abs(bone.length)) : currentScale;
    const shear = normAngle(f32Sub(f32Mul(f32(Math.atan2(-current.c, current.d)), F32_RADIANS_TO_DEGREES), currentRotation));
    const matrix = localBoneAffineV1({ ...bone, x: mix(current.tx, sample.x, c.mixX), y: mix(current.ty, sample.y, c.mixY), rotation,
      shearX: 0, shearY: shear, scaleX: c.rotateMode === "chainScale" && bone.length !== 0 ? mix(currentScale, desiredScale, c.mixRotate) : currentScale,
      scaleY: f32MatrixAxisLength(current.c, current.d) });
    if (!finite(matrix)) throw pathGeometryFailure(); desired.set(bone.id, matrix); d.drivenBoneCount++;
    if (c.mixX !== 0 || c.mixY !== 0) d.maxTranslationResidual = maximum(d.maxTranslationResidual, f32MatrixAxisLength(c.mixX === 0 ? 0 : f32Sub(matrix.tx, sample.x), c.mixY === 0 ? 0 : f32Sub(matrix.ty, sample.y)));
    if (c.mixRotate !== 0) d.maxRotationResidualDegrees = maximum(d.maxRotationResidualDegrees, Math.abs(deltaAngle(angle(matrix), desiredRotation)));
    if (c.rotateMode === "chainScale" && c.mixRotate !== 0 && bone.length !== 0) d.maxScaleResidual = maximum(d.maxScaleResidual, Math.abs(f32Sub(f32MatrixAxisLength(matrix.a, matrix.b), desiredScale)));
  }
  const affected = new Set<string>();
  for (const bone of bones) {
    const matrix = desired.get(bone.id), parentAffected = bone.parentId !== null && affected.has(bone.parentId);
    if (matrix === undefined && !parentAffected) continue; affected.add(bone.id);
    const local = boneById.get(bone.id) ?? bone, parent = bone.parentId === null ? root : worlds.get(bone.parentId);
    if (matrix !== undefined) {
      if (parent !== undefined) { try { Object.assign(local, recoverLocalTransform(matrix, parent, bone.parentId === null ? "inheritAll" : local.transformMode, local)); } catch { /* Keep the authored local pose when only the world write is representable. */ } }
      worlds.set(bone.id, matrix);
    } else if (parent === undefined) worlds.delete(bone.id);
    else {
      const child = childWorldAffineV1(parent, local, bone.parentId === null ? "inheritAll" : local.transformMode);
      if (finite(child)) worlds.set(bone.id, child); else worlds.delete(bone.id);
    }
  }
  return d;
}
