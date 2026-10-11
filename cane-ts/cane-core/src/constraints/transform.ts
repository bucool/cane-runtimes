import type { AffineV1, BoneTransformModeV1, RuntimeBoneV1, RuntimeTransformConstraintV1, RuntimeTransformConstraintDiagnosticV1 } from "../contracts.js";
import { RuntimeErrorV1 } from "../errors.js";
import { childWorldAffineV1 } from "../math/affine.js";
import { f32, f32MatrixAxisLength, f32Mul, F32_RADIANS_TO_DEGREES } from "../math/f32.js";
import { channelsToLocal, channelsToWorld, checkWorld, evaluateTransformChannels, localChannels, recoverLocalTransform,
  transformRoutes, transformSourceChannels, worldChannels, wrapTransformAngle } from "../math/transform-channels.js";

type Mutable<T> = { -readonly [K in keyof T]: T[K] };
const sameWorld = (a: AffineV1, b: AffineV1) => a.a === b.a && a.b === b.b && a.c === b.c && a.d === b.d && a.tx === b.tx && a.ty === b.ty;
const maximum = (a: number | null, b: number): number | null => Number.isFinite(b) ? Math.max(a ?? 0, f32(b)) : a;
const angle = (m: AffineV1) => f32Mul(f32(Math.atan2(m.b, m.a)), F32_RADIANS_TO_DEGREES);

/** Capture the source once, then stage ordered member and descendant writes. */
export function solveTransformChannelsV1(constraint: RuntimeTransformConstraintV1, bones: readonly RuntimeBoneV1[],
  boneById: ReadonlyMap<string, Mutable<RuntimeBoneV1>>, root: AffineV1, worlds: Map<string, AffineV1>): RuntimeTransformConstraintDiagnosticV1 | null {
  const sourceLocal = boneById.get(constraint.targetBoneId), sourceWorld = worlds.get(constraint.targetBoneId);
  if (sourceLocal === undefined || sourceWorld === undefined) return null;
  const localSource = constraint.mapping?.localSource ?? constraint.local, localTarget = constraint.mapping?.localTarget ?? constraint.local;
  const source = transformSourceChannels(localSource ? localChannels(sourceLocal) : worldChannels(sourceWorld),
    [constraint.rotation, constraint.x, constraint.y, constraint.scaleX, constraint.scaleY, constraint.shearY], localSource ? null : sourceWorld);
  const routes = transformRoutes(constraint), mixes = [constraint.mixRotate, constraint.mixX, constraint.mixY, constraint.mixScaleX, constraint.mixScaleY, constraint.mixShearY];
  let remaining = 1_000_000;
  const charge = (amount: number) => { if (amount > remaining) throw new RuntimeErrorV1("resourceLimit", "apply", "Transform work budget is exhausted."); remaining -= amount; };
  charge(routes.length);
  evaluateTransformChannels(source, [0, 0, 0, 1, 1, 0], routes, [0, 0, 0, 0, 0, 0], constraint.relative);
  if (!mixes.every(Number.isFinite)) throw new RuntimeErrorV1("nonFinite", "apply", "Transform mixes must be finite.");
  checkWorld(root);
  const children = new Map<string, string[]>();
  for (const b of bones) if (b.parentId !== null) { const list = children.get(b.parentId) ?? []; list.push(b.id); children.set(b.parentId, list); }
  const locals = new Map<string, RuntimeBoneV1>(), writes = new Map<string, AffineV1>();
  const worldAt = (id: string) => writes.get(id) ?? worlds.get(id);
  const enclosing = (b: RuntimeBoneV1): AffineV1 => {
    const p = b.parentId === null ? root : worldAt(b.parentId);
    if (p === undefined) throw new RuntimeErrorV1("nonFinite", "apply", "Transform parent is unavailable."); return p;
  };
  const modeFor = (b: RuntimeBoneV1): BoneTransformModeV1 => b.parentId === null ? "inheritAll" : b.transformMode;
  const candidate = (local: RuntimeBoneV1, world: AffineV1, parent: AffineV1, mode: BoneTransformModeV1, amounts: readonly number[]): [AffineV1, RuntimeBoneV1 | null] => {
    const current = localTarget ? localChannels(local) : worldChannels(world);
    const next = evaluateTransformChannels(source, current, routes, amounts, constraint.relative);
    if (next.every((v, i) => v === current[i])) return [world, local];
    if (!localTarget) return [channelsToWorld(next), null];
    const pose = channelsToLocal(next, local), matrix = childWorldAffineV1(parent, pose, mode); checkWorld(matrix); return [matrix, pose];
  };
  const diagnostic: Mutable<RuntimeTransformConstraintDiagnosticV1> = { type: "transform", drivenBoneCount: 0,
    mixRotate: mixes[0]!, mixX: mixes[1]!, mixY: mixes[2]!, mixScaleX: mixes[3]!, mixScaleY: mixes[4]!, mixShearY: mixes[5]!,
    maxTranslationResidual: null, maxRotationResidualDegrees: null, maxScaleResidual: null, maxShearResidualDegrees: null };
  for (const id of constraint.boneIds) {
    if (id === constraint.targetBoneId) continue;
    const current = locals.get(id) ?? boneById.get(id), world = worldAt(id);
    if (current === undefined || world === undefined) continue;
    charge(1 + 2 * routes.length);
    const parent = enclosing(current), mode = modeFor(current);
    const [nextWorld, projectedLocal] = candidate(current, world, parent, mode, mixes);
    const nextLocal = projectedLocal ?? (sameWorld(nextWorld, world) ? current : recoverLocalTransform(nextWorld, parent, mode, current));
    diagnostic.drivenBoneCount += 1;
    try {
      const [ideal] = candidate(current, world, parent, mode, mixes.map(m => m === 0 ? 0 : 1));
      if (mixes[1] !== 0 || mixes[2] !== 0) diagnostic.maxTranslationResidual = maximum(diagnostic.maxTranslationResidual,
        Math.hypot(mixes[1] === 0 ? 0 : nextWorld.tx - ideal.tx, mixes[2] === 0 ? 0 : nextWorld.ty - ideal.ty));
      if (mixes[0] !== 0) diagnostic.maxRotationResidualDegrees = maximum(diagnostic.maxRotationResidualDegrees, Math.abs(wrapTransformAngle(angle(ideal) - angle(nextWorld))));
      if (mixes[3] !== 0 || mixes[4] !== 0) diagnostic.maxScaleResidual = maximum(diagnostic.maxScaleResidual, Math.max(
        mixes[3] === 0 ? 0 : Math.abs(f32MatrixAxisLength(nextWorld.a, nextWorld.b) - f32MatrixAxisLength(ideal.a, ideal.b)),
        mixes[4] === 0 ? 0 : Math.abs(f32MatrixAxisLength(nextWorld.c, nextWorld.d) - f32MatrixAxisLength(ideal.c, ideal.d))));
      if (mixes[5] !== 0) diagnostic.maxShearResidualDegrees = maximum(diagnostic.maxShearResidualDegrees,
        Math.abs(wrapTransformAngle(f32(worldChannels(ideal)[5]) - f32(worldChannels(nextWorld)[5]))));
    } catch (error) { if (!(error instanceof RuntimeErrorV1)) throw error; }
    if (sameWorld(nextWorld, world) && localChannels(nextLocal).every((v, i) => v === localChannels(current)[i])) continue;
    locals.set(id, nextLocal); writes.set(id, nextWorld);
    const queue = [...(children.get(id) ?? [])];
    for (let i = 0; i < queue.length; i += 1) {
      charge(1); const childId = queue[i]!, child = locals.get(childId) ?? boneById.get(childId);
      if (child === undefined) continue;
      const matrix = childWorldAffineV1(enclosing(child), child, modeFor(child)); checkWorld(matrix); writes.set(childId, matrix);
      queue.push(...(children.get(childId) ?? []));
    }
  }
  for (const [id, local] of locals) Object.assign(boneById.get(id)!, local);
  for (const [id, world] of writes) worlds.set(id, world);
  return diagnostic;
}
