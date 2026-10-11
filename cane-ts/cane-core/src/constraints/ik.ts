import type { AffineV1, RuntimeBoneV1, RuntimeIkConstraintV1, RuntimeIkConstraintDiagnosticV1 } from "../contracts.js";
import { RuntimeErrorV1 } from "../errors.js";
import { childWorldAffineV1, transformPointV1 } from "../math/affine.js";
import { affineIkPair, ikFailure, inverseIkTarget, scaledIkAxes, softenedIkDistance, transformIkPoint,
  vAdd, vCross, vLength, vScale, vSub, type Vector2 } from "../math/ik-geometry.js";
import { narrowTransform, wrapTransformAngle } from "../math/transform-channels.js";
import { feasibleIk, minimizeIk, type IkObservation } from "./ik-search.js";

type Mutable<T> = { -readonly [K in keyof T]: T[K] };
interface Evaluation { readonly tip: Vector2; readonly pivot: Vector2; readonly reach: number; readonly bend: number; }
interface Outcome { readonly poses: RuntimeBoneV1[]; readonly iterations: number; readonly residual: number; }
class ChainGeometry {
  remaining = 1_000_000;
  constructor(readonly source: readonly RuntimeBoneV1[], readonly parent: AffineV1 | null, readonly root: AffineV1, readonly options: RuntimeIkConstraintV1) { }
  ordinary(): boolean { return this.source.every((p, i) => (i === 0 && this.parent === null) || p.transformMode === "inheritAll"); }
  outer(): AffineV1 { return this.parent ?? this.root; }
  evaluate(poses: readonly RuntimeBoneV1[]): Evaluation {
    if (poses.length === 0 || poses.length !== this.source.length) throw ikFailure();
    let parent = this.parent; const origins: Vector2[] = [];
    for (const pose of poses) {
      if (this.remaining === 0) throw new RuntimeErrorV1("resourceLimit", "apply", "IK bone evaluation budget is exhausted."); this.remaining--;
      parent = childWorldAffineV1(parent ?? this.root, pose, parent === null ? "inheritAll" : pose.transformMode);
      if (![parent.a, parent.b, parent.c, parent.d, parent.tx, parent.ty].every(Number.isFinite)) throw ikFailure();
      origins.push([parent.tx, parent.ty]);
    }
    const tip = transformPointV1(parent!, poses[poses.length - 1]!.length, 0); if (!tip.every(Number.isFinite)) throw ikFailure();
    let reach = 0;
    for (let i = 1; i < origins.length; i++) reach += vLength(vSub(origins[i]!, origins[i - 1]!));
    reach += vLength(vSub(tip, origins[origins.length - 1]!));
    return { tip, pivot: origins[0]!, reach,
      bend: origins.length === 2 ? vCross(vSub(origins[1]!, origins[0]!), vSub(tip, origins[1]!)) : 0 };
  }
  observation(value: Evaluation, target: Vector2): IkObservation {
    const error = vSub(value.tip, target); return { error, residual: vLength(error), bendMatches: value.bend === 0 || (value.bend > 0) === this.options.bendPositive };
  }
  agrees(expected: Vector2, observed: Evaluation): boolean {
    const extent = Math.max(Math.abs(expected[0]), Math.abs(expected[1]), observed.reach);
    return vLength(vSub(expected, observed.tip)) <= 64 * (2 ** -23) * extent + 2 * (2 ** -149);
  }
  parameterPoses(source: readonly RuntimeBoneV1[], parameters: readonly number[]): { poses: RuntimeBoneV1[]; factor: number } {
    const poses = source.map((pose, i) => ({ ...pose, rotation: narrowTransform(parameters[i]!) }));
    const factor = parameters.length > source.length ? Math.exp(parameters[source.length]!) : 1;
    const [scaleX, scaleY] = scaledIkAxes(source[0]!, factor, 1, this.options.uniform);
    poses[0] = { ...poses[0]!, scaleX, scaleY }; return { poses, factor };
  }
  scaleBounds(pose: RuntimeBoneV1): readonly [number, number] {
    const min = Math.log(2 ** -149), max = Math.log(3.4028234663852886e38); let low = min - max, high = max - min;
    for (const [i, axis] of [pose.scaleX, pose.scaleY].entries()) {
      if (axis === 0 || (i === 1 && this.options.uniform === false)) continue;
      const log = Math.log(Math.abs(axis)), inverse = i === 1 && this.options.uniform === "volume";
      low = Math.max(low, inverse ? log - max : min - log); high = Math.min(high, inverse ? log - min : max - log);
    }
    return [this.options.compress ? Math.min(low, 0) : 0, this.options.stretch ? Math.max(high, 0) : 0];
  }
  search(source: readonly RuntimeBoneV1[], seed: readonly RuntimeBoneV1[], target: Vector2, scale: boolean): { poses: RuntimeBoneV1[]; factor: number; passes: number } {
    const count = source.length, values = seed.map(p => p.rotation), bounds: Array<readonly [number, number]> = values.map(() => [-180, 180]);
    if (scale) { values.push(0); bounds.push(this.scaleBounds(source[0]!)); }
    const result = minimizeIk(values, bounds, count, count <= 2 ? Math.max(this.options.iterations, 1) : this.options.iterations, this.options.threshold,
      parameters => this.observation(this.evaluate(this.parameterPoses(source, parameters).poses), target));
    return { ...this.parameterPoses(source, result.parameters), passes: result.passes };
  }
  single(source: RuntimeBoneV1, target: Vector2): { poses: RuntimeBoneV1[]; factor: number } | null {
    const outer = this.outer(), v = inverseIkTarget(outer, source, target); if (v === null) return null;
    const length = source.length * source.scaleX, requested = vLength(v); if (length === 0) return { poses: [source], factor: 1 };
    const ratio = requested / Math.abs(length), factor = (ratio < 1 && this.options.compress) || (ratio > 1 && this.options.stretch) ? ratio : 1;
    const [scaleX, scaleY] = scaledIkAxes(source, factor, 1, this.options.uniform);
    const rotation = requested === 0 ? source.rotation : narrowTransform(wrapTransformAngle(Math.atan2(v[1], v[0]) * (180 / Math.PI) - source.shearX - (length < 0 ? 180 : 0)));
    const pose = { ...source, rotation, scaleX, scaleY }, observed = this.evaluate([pose]);
    if (requested === 0) return { poses: [pose], factor };
    const unit = Math.abs(length) * factor / requested, expected = transformIkPoint(outer, [source.x + v[0] * unit, source.y + v[1] * unit]);
    return this.agrees(expected, observed) ? { poses: [pose], factor } : null;
  }
  rotate(base: readonly RuntimeBoneV1[], target: Vector2, softness: number): { poses: RuntimeBoneV1[]; passes: number } {
    if (base.length === 2 && this.ordinary()) {
      const candidate = feasibleIk(() => affineIkPair(base[0]!, base[1]!, this.outer(), target, Math.fround(softness), this.options.bendPositive));
      if (candidate !== null) {
        const observed = feasibleIk(() => this.evaluate(candidate.poses));
        if (observed !== null && this.agrees(candidate.expected, observed)) return { poses: candidate.poses, passes: 1 };
      }
    }
    const observed = this.evaluate(base), requested = vLength(vSub(target, observed.pivot));
    const distance = softenedIkDistance(requested, observed.reach, softness), factor = requested === 0 ? 1 : distance / requested;
    return this.search(base, base, vAdd(observed.pivot, vScale(vSub(target, observed.pivot), factor)), false);
  }
  solve(target: Vector2): Outcome {
    const o = this.options, source = this.source;
    if (![...target, o.threshold, o.softness, o.mix].every(Number.isFinite) || o.threshold < 0 || o.softness < 0 || o.mix < 0 || o.mix > 1) throw ikFailure();
    const baseline = this.evaluate(source);
    if (o.mix === 0) return { poses: [...source], iterations: 0, residual: narrowTransform(this.observation(baseline, target).residual) };
    let full = [...source], factor = 1, iterations = 0;
    if (source.length === 1 && this.ordinary()) {
      const candidate = this.single(source[0]!, target);
      if (candidate !== null) { full = candidate.poses; factor = candidate.factor; iterations = 1; }
    }
    if (iterations === 0) {
      const rotated = this.rotate(full, target, 0); full = rotated.poses; iterations = rotated.passes;
      if (source.length <= 2 && (o.compress || o.stretch) && this.observation(this.evaluate(full), target).residual > o.threshold) {
        const scaled = this.search(source, full, target, true); full = scaled.poses; factor = scaled.factor; iterations = Math.max(iterations, scaled.passes);
        if (o.compress && o.uniform !== "volume") {
          const collapsed = [...source], [scaleX, scaleY] = scaledIkAxes(source[0]!, 0, 1, o.uniform); collapsed[0] = { ...collapsed[0]!, scaleX, scaleY };
          const candidate = feasibleIk(() => this.rotate(collapsed, target, 0));
          if (candidate !== null && this.observation(this.evaluate(candidate.poses), target).residual < this.observation(this.evaluate(full), target).residual) {
            full = candidate.poses; factor = 0; iterations = Math.max(iterations, candidate.passes);
          }
        }
      }
    }
    if (o.softness > 0 && source.length > 1) { const softened = this.rotate(full, target, o.softness); full = softened.poses; iterations = Math.max(iterations, softened.passes); }
    const mixed = source.map((pose, i) => {
      const desired = full[i]!.rotation, original = wrapTransformAngle(pose.rotation);
      return { ...pose, rotation: pose.rotation === desired ? pose.rotation : narrowTransform(wrapTransformAngle(original + o.mix * wrapTransformAngle(desired - original))) };
    });
    const [scaleX, scaleY] = scaledIkAxes(source[0]!, factor, o.mix, o.uniform); mixed[0] = { ...mixed[0]!, scaleX, scaleY };
    return { poses: mixed, iterations, residual: narrowTransform(this.observation(this.evaluate(mixed), target).residual) };
  }
}

export function solveAffineIkV1(constraint: RuntimeIkConstraintV1, bones: readonly RuntimeBoneV1[], boneById: ReadonlyMap<string, Mutable<RuntimeBoneV1>>,
  root: AffineV1, worlds: Map<string, AffineV1>): RuntimeIkConstraintDiagnosticV1 | null {
  const source: RuntimeBoneV1[] = [], seen = new Set<string>();
  for (const id of constraint.chainBoneIds) {
    const bone = boneById.get(id); if (bone === undefined || seen.has(id) || (source.length > 0 && bone.parentId !== source[source.length - 1]!.id)) return null;
    source.push(bone); seen.add(id);
  }
  if (source.length === 0) return null;
  let target: Vector2 = [constraint.target.x, constraint.target.y];
  if (constraint.targetBoneId !== null) { const world = worlds.get(constraint.targetBoneId); if (world === undefined) return null; target = [world.tx, world.ty]; }
  const parent = source[0]!.parentId === null ? null : worlds.get(source[0]!.parentId); if (parent === undefined) throw ikFailure();
  const options = { ...constraint, mix: Number.isFinite(constraint.mix) ? Math.max(0, Math.min(1, constraint.mix)) : 0,
    threshold: Number.isFinite(constraint.threshold) ? Math.max(0, constraint.threshold) : 0 };
  const result = new ChainGeometry(source, parent, root, options).solve(target);
  const diagnostic: RuntimeIkConstraintDiagnosticV1 = { type: "ik", residual: result.residual, threshold: options.threshold, iterationsUsed: result.iterations,
    iterationLimit: constraint.iterations, saturated: options.mix >= Math.fround(0.999) && result.residual > Math.max(options.threshold, Math.fround(0.0001)) };
  if (options.mix === 0) return diagnostic;
  const localWrites = new Map(result.poses.map(p => [p.id, p] as const)), worldWrites = new Map<string, AffineV1>(), affected = new Set(constraint.chainBoneIds);
  for (const bone of bones) {
    if (bone.parentId !== null && affected.has(bone.parentId)) affected.add(bone.id); if (!affected.has(bone.id)) continue;
    const local = localWrites.get(bone.id) ?? boneById.get(bone.id) ?? bone;
    const outer = bone.parentId === null ? root : worldWrites.get(bone.parentId) ?? worlds.get(bone.parentId); if (outer === undefined) throw ikFailure();
    const world = childWorldAffineV1(outer, local, bone.parentId === null ? "inheritAll" : local.transformMode);
    if (![world.a, world.b, world.c, world.d, world.tx, world.ty].every(Number.isFinite)) throw ikFailure(); worldWrites.set(bone.id, world);
  }
  for (const [id, local] of localWrites) Object.assign(boneById.get(id)!, local);
  for (const [id, world] of worldWrites) worlds.set(id, world);
  return diagnostic;
}
