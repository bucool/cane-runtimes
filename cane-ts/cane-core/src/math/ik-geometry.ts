import type { AffineV1, RuntimeBoneV1, RuntimeIkConstraintV1 } from "../contracts.js";
import { RuntimeErrorV1 } from "../errors.js";
import { localBoneAffineV1 } from "./affine.js";
import { narrowTransform, wrapTransformAngle } from "./transform-channels.js";

export type Vector2 = readonly [number, number];
export const ikFailure = () => new RuntimeErrorV1("nonFinite", "apply", "IK geometry cannot be represented.");
export const vAdd = (a: Vector2, b: Vector2): Vector2 => [a[0] + b[0], a[1] + b[1]];
export const vSub = (a: Vector2, b: Vector2): Vector2 => [a[0] - b[0], a[1] - b[1]];
export const vScale = (a: Vector2, s: number): Vector2 => [a[0] * s, a[1] * s];
export const vDot = (a: Vector2, b: Vector2) => a[0] * b[0] + a[1] * b[1];
export const vCross = (a: Vector2, b: Vector2) => a[0] * b[1] - a[1] * b[0];
export const vLength = (a: Vector2) => Math.hypot(...a);
export const vRotate = (a: Vector2, angle: number): Vector2 => [Math.cos(angle) * a[0] - Math.sin(angle) * a[1], Math.sin(angle) * a[0] + Math.cos(angle) * a[1]];
const radians = (a: number) => { let r = a % (2 * Math.PI); if (r < 0) r += 2 * Math.PI; return r >= Math.PI ? r - 2 * Math.PI : r; };
const finite = (values: readonly number[]) => { if (!values.every(Number.isFinite)) throw ikFailure(); };
export function scaledIkAxes(pose: RuntimeBoneV1, multiplier: number, mix: number, mode: RuntimeIkConstraintV1["uniform"]): Vector2 {
  finite([pose.scaleX, pose.scaleY, multiplier, mix]); if (multiplier < 0 || mix < 0 || mix > 1) throw ikFailure();
  const factor = (1 - mix) + mix * multiplier;
  if (mode === "volume" && factor === 0) throw ikFailure();
  const fy = mode === true ? factor : mode === "volume" ? 1 / factor : 1;
  return [narrowTransform(pose.scaleX * factor, true), narrowTransform(pose.scaleY * fy, true)];
}
export function softenedIkDistance(distance: number, reach: number, softness: number): number {
  finite([distance, reach, softness]); if (distance < 0 || reach < 0 || softness < 0) throw ikFailure();
  const width = Math.min(softness, reach), start = reach - width;
  return width === 0 || distance <= start ? distance : start - width * Math.expm1(-(distance - start) / width);
}
function unique(values: number[], tolerance: number): number[] {
  values.sort((a, b) => a - b); const result: number[] = []; for (const v of values) if (result.length === 0 || Math.abs(v - result[result.length - 1]!) > tolerance) result.push(v); return result;
}
function polynomialRoots(coefficients: readonly number[]): number[] {
  let c = [...coefficients]; while (c.length > 1 && c[c.length - 1] === 0) c.pop(); if (c.length <= 1) return [];
  const scale = Math.max(...c.map(Math.abs)); c = c.map(v => v / scale);
  const evaluate = (x: number) => { let sum = 0; for (let i = c.length - 1; i >= 0; i--) sum = sum * x + c[i]!; return sum; };
  const intervals = unique([...polynomialRoots(c.slice(1).map((v, i) => v * (i + 1))), -1, 1], 1e-14);
  const tolerance = 64 * Number.EPSILON * c.reduce((s, v) => s + Math.abs(v), 0);
  const roots = intervals.filter(x => Math.abs(evaluate(x)) <= tolerance);
  for (let i = 1; i < intervals.length; i++) {
    let low = intervals[i - 1]!, high = intervals[i]!; const left = evaluate(low), right = evaluate(high);
    if (left === 0 || right === 0 || (left < 0) === (right < 0)) continue;
    for (let j = 0; j < 80; j++) {
      const middle = (low + high) / 2; if (middle === low || middle === high) break;
      const value = evaluate(middle); if (value === 0) { low = high = middle; break; }
      if ((value < 0) === (left < 0)) low = middle; else high = middle;
    }
    roots.push((low + high) / 2);
  }
  return unique(roots, 1e-12);
}
class ReachCurve {
  readonly h: Vector2; readonly u: Vector2; readonly z: Vector2; readonly extent: number;
  readonly stationary: number[] = [-Math.PI, -Math.PI / 2, 0, Math.PI / 2];
  minRadius = Infinity; maxRadius = 0;
  constructor(h: Vector2, u: Vector2, z: Vector2) {
    finite([...h, ...u, ...z]); this.extent = Math.max(vLength(h), vLength(u), vLength(z)); finite([this.extent]);
    const normalize = (p: Vector2): Vector2 => this.extent === 0 ? p : [p[0] / this.extent, p[1] / this.extent];
    this.h = normalize(h); this.u = normalize(u); this.z = normalize(z);
    for (const chart of [1, -1]) {
      const cu = vScale(this.u, chart), cz = vScale(this.z, chart);
      const e = [vAdd(this.h, cu), vScale(cz, 2), vSub(this.h, cu)], derivative = [cz, vScale(cu, -2), vScale(cz, -1)];
      const p = [0, 0, 0, 0, 0]; for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) p[i + j]! += vDot(e[i]!, derivative[j]!);
      for (const t of polynomialRoots(p)) this.stationary.push(radians(2 * Math.atan(t) + (chart < 0 ? Math.PI : 0)));
    }
    for (const beta of this.stationary) { const radius = vLength(this.vector(beta)); this.minRadius = Math.min(this.minRadius, radius); this.maxRadius = Math.max(this.maxRadius, radius); }
    finite([this.maxRadius * this.extent]);
  }
  vector(beta: number): Vector2 { return vAdd(this.h, vAdd(vScale(this.u, Math.cos(beta)), vScale(this.z, Math.sin(beta)))); }
  solve(target: Vector2, preferred: Vector2, positive: boolean, reflected: boolean): { endpoint: Vector2; parent: number; child: number } {
    const distance = vLength(target), effective = Math.max(this.minRadius * this.extent, Math.min(this.maxRadius * this.extent, distance)); finite([distance, ...preferred]);
    const radius = this.extent === 0 ? 0 : effective / this.extent, angles = [...this.stationary, radians(preferred[1])];
    for (const chart of [1, -1]) {
      const u = vScale(this.u, chart), z = vScale(this.z, chart), e = [vAdd(this.h, u), vScale(z, 2), vSub(this.h, u)];
      const p = [0, 0, 0, 0, 0]; for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) p[i + j]! += vDot(e[i]!, e[j]!);
      p[0]! -= radius * radius; p[2]! -= 2 * radius * radius; p[4]! -= radius * radius;
      for (const t of polynomialRoots(p)) angles.push(radians(2 * Math.atan(t) + (chart < 0 ? Math.PI : 0)));
    }
    let best: readonly [number, number, number, number] | null = null;
    for (const beta of unique(angles.filter(v => Math.abs(vLength(this.vector(v)) - radius) <= 2e-7), 1e-12)) {
      const vector = this.vector(beta), alpha = distance === 0 || vLength(vector) === 0 ? radians(preferred[0]) : radians(Math.atan2(target[1], target[0]) - Math.atan2(vector[1], vector[0]));
      let bend = vCross(this.h, vSub(vector, this.h)); if (reflected) bend = -bend;
      const candidate = [bend !== 0 && (bend > 0) !== positive ? 1 : 0, radians(alpha - radians(preferred[0])) ** 2 + radians(beta - radians(preferred[1])) ** 2, beta, alpha] as const;
      let better = best === null; if (best !== null) for (let i = 0; i < 4; i++) { if (candidate[i] === best[i]) continue; better = candidate[i]! < best[i]!; break; }
      if (better) best = candidate;
    }
    if (best === null) throw ikFailure();
    const endpoint = vScale(vRotate(this.vector(best[2]), best[3]), this.extent); finite([...endpoint, vLength(vSub(endpoint, target))]);
    return { endpoint, parent: best[3], child: best[2] };
  }
}
export function inverseIkTarget(outer: AffineV1, pose: RuntimeBoneV1, target: Vector2): Vector2 | null {
  const { a, b, c, d } = outer, determinant = a * d - b * c; if (determinant === 0) return null;
  const dx = target[0] - outer.tx, dy = target[1] - outer.ty;
  const result: Vector2 = [(d * dx - c * dy) / determinant - pose.x, (a * dy - b * dx) / determinant - pose.y];
  return result.every(Number.isFinite) ? result : null;
}
export const transformIkPoint = (m: AffineV1, p: Vector2): Vector2 => [m.a * p[0] + m.c * p[1] + m.tx, m.b * p[0] + m.d * p[1] + m.ty];
export function affineIkPair(parent: RuntimeBoneV1, child: RuntimeBoneV1, outer: AffineV1, target: Vector2, softness: number, positive: boolean): { poses: RuntimeBoneV1[]; expected: Vector2 } | null {
  const localTarget = inverseIkTarget(outer, parent, target); if (localTarget === null) return null;
  const shape = localBoneAffineV1({ ...parent, rotation: 0 }), ell = child.length * child.scaleX;
  const hx: Vector2 = [shape.a, shape.b], hy: Vector2 = [shape.c, shape.d];
  const h = vAdd(vScale(hx, child.x), vScale(hy, child.y)), curve = new ReachCurve(h, vScale(hx, ell), vScale(hy, ell));
  const distance = vLength(localTarget), desired = softenedIkDistance(distance, curve.maxRadius * curve.extent, softness), ratio = distance === 0 ? 1 : desired / distance;
  const solution = curve.solve(vScale(localTarget, ratio), [parent.rotation * (Math.PI / 180), (child.rotation + child.shearX) * (Math.PI / 180)], positive, outer.a * outer.d - outer.b * outer.c < 0);
  const mixedAngle = (before: number, after: number) => Math.fround(wrapTransformAngle(wrapTransformAngle(before) + wrapTransformAngle(wrapTransformAngle(after) - wrapTransformAngle(before))));
  return { poses: [{ ...parent, rotation: mixedAngle(parent.rotation, solution.parent * (180 / Math.PI)) },
    { ...child, rotation: mixedAngle(child.rotation, solution.child * (180 / Math.PI) - child.shearX) }],
    expected: transformIkPoint(outer, [parent.x + solution.endpoint[0], parent.y + solution.endpoint[1]]) };
}
