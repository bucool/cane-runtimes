import { RuntimeErrorV1 } from "../errors.js";

export type ArcPoint = readonly [number, number];
export type ArcControls = readonly [ArcPoint, ArcPoint, ArcPoint, ArcPoint];
export const pathGeometryFailure = () => new RuntimeErrorV1("nonFinite", "apply", "Path geometry cannot be represented.");
const resourceFailure = () => new RuntimeErrorV1("resourceLimit", "apply", "Path subdivision budget is exhausted.");
export class ArcBudget { remaining = 1_000_000; visit(): void { if (this.remaining === 0) throw resourceFailure(); this.remaining--; } }
const difference = (a: ArcPoint, b: ArcPoint): ArcPoint => [a[0] - b[0], a[1] - b[1]];
const magnitude = (p: ArcPoint) => Math.hypot(p[0], p[1]);
const lerp = (a: ArcPoint, b: ArcPoint, t: number): ArcPoint => [a[0] * (1 - t) + b[0] * t, a[1] * (1 - t) + b[1] * t];
const clamp = (t: number) => Math.max(0, Math.min(1, t));
function split(p: ArcControls, t: number): readonly [ArcControls, ArcControls] {
  const a = lerp(p[0], p[1], t), b = lerp(p[1], p[2], t), c = lerp(p[2], p[3], t), d = lerp(a, b, t), e = lerp(b, c, t), f = lerp(d, e, t);
  return [[p[0], a, d, f], [f, e, c, p[3]]];
}
function lengthBounds(p: ArcControls): readonly [number, number] {
  const lower = magnitude(difference(p[3], p[0])); let upper = 0;
  for (let i = 0; i < 3; i++) upper += magnitude(difference(p[i + 1]!, p[i]!)); upper = Math.max(upper, lower);
  if (!Number.isFinite(lower) || !Number.isFinite(upper)) throw pathGeometryFailure(); return [lower, upper];
}
function partialLength(p: ArcControls, t: number): number { const [lower, upper] = lengthBounds(split(p, clamp(t))[0]); return lower + (upper - lower) * .5; }
function boxDistance(p: ArcControls, target: ArcPoint): number {
  const delta = [0, 0]; for (let i = 0; i < 2; i++) {
    const low = Math.min(p[0][i]!, p[1][i]!, p[2][i]!, p[3][i]!), high = Math.max(p[0][i]!, p[1][i]!, p[2][i]!, p[3][i]!);
    delta[i] = target[i]! < low ? low - target[i]! : target[i]! > high ? target[i]! - high : 0;
  }
  return Math.hypot(delta[0]!, delta[1]!);
}
interface Node { readonly controls: ArcControls; readonly start: number; readonly end: number; readonly depth: number; }
interface Leaf extends Omit<Node, "depth"> { readonly from: number; readonly to: number; }
export class CubicArc {
  readonly leaves: Leaf[] = []; readonly tolerance: number; readonly length: number;
  constructor(readonly controls: ArcControls, budget: ArcBudget) {
    if (!controls.every(p => p.every(v => Number.isFinite(v) && Math.abs(v) <= 3.4028234663852886e38))) throw pathGeometryFailure();
    this.tolerance = lengthBounds(controls)[1] * 1e-6; let length = 0;
    const pending: Node[] = [{ controls, start: 0, end: 1, depth: 0 }];
    while (pending.length > 0) {
      const node = pending.pop()!; budget.visit(); const [lower, upper] = lengthBounds(node.controls);
      if (upper - lower <= this.tolerance * (node.end - node.start)) {
        const next = length + lower + (upper - lower) * .5; if (!Number.isFinite(next)) throw pathGeometryFailure();
        this.leaves.push({ controls: node.controls, start: node.start, end: node.end, from: length, to: next }); length = next;
      } else {
        if (node.depth === 32) throw resourceFailure(); const [left, right] = split(node.controls, .5), middle = (node.start + node.end) * .5;
        pending.push({ controls: right, start: middle, end: node.end, depth: node.depth + 1 }, { controls: left, start: node.start, end: middle, depth: node.depth + 1 });
      }
    }
    this.length = length;
  }
  point(t: number): ArcPoint { return split(this.controls, clamp(t))[0][3]; }
  tangent(t: number): ArcPoint {
    t = clamp(t); const e = [difference(this.controls[1], this.controls[0]), difference(this.controls[2], this.controls[1]), difference(this.controls[3], this.controls[2])];
    const first = lerp(lerp(e[0]!, e[1]!, t), lerp(e[1]!, e[2]!, t), t); if (magnitude(first) > 0) return first;
    const a = difference(e[1]!, e[0]!), b = difference(e[2]!, e[1]!); let second = lerp(a, b, t); if (t === 1) second = [-second[0], -second[1]];
    if (magnitude(second) > 0) return second; const third = difference(b, a); return magnitude(third) > 0 ? third : [1, 0];
  }
  parameterAtDistance(distance: number): number {
    if (distance <= 0 || this.length === 0) return 0; if (distance >= this.length) return 1;
    let lo = 0, hi = this.leaves.length; while (lo < hi) { const mid = (lo + hi) >>> 1; if (this.leaves[mid]!.to < distance) lo = mid + 1; else hi = mid; }
    const leaf = this.leaves[Math.min(lo, this.leaves.length - 1)]!, requested = distance - leaf.from; let low = 0, high = 1;
    for (let i = 0; i < 48; i++) { const middle = (low + high) * .5; if (partialLength(leaf.controls, middle) < requested) low = middle; else high = middle; }
    return leaf.start + (leaf.end - leaf.start) * (low + high) * .5;
  }
  distanceAtParameter(t: number): number {
    if (t <= 0) return 0; if (t >= 1) return this.length;
    let lo = 0, hi = this.leaves.length; while (lo < hi) { const mid = (lo + hi) >>> 1; if (this.leaves[mid]!.end < t) lo = mid + 1; else hi = mid; }
    const leaf = this.leaves[Math.min(lo, this.leaves.length - 1)]!;
    return leaf.from + Math.max(0, Math.min(leaf.to - leaf.from, partialLength(leaf.controls, (t - leaf.start) / (leaf.end - leaf.start))));
  }
  closestParameter(target: ArcPoint, budget: ArcBudget): number {
    if (!target.every(Number.isFinite)) throw pathGeometryFailure();
    let bestT = 0, bestDistance = magnitude(difference(this.controls[0], target)); const endDistance = magnitude(difference(this.controls[3], target));
    if (endDistance < bestDistance) { bestT = 1; bestDistance = endDistance; } if (!Number.isFinite(bestDistance)) throw pathGeometryFailure();
    const pending: Node[] = [{ controls: this.controls, start: 0, end: 1, depth: 0 }];
    while (pending.length > 0) {
      const node = pending.pop()!; budget.visit(); if (bestDistance - boxDistance(node.controls, target) <= this.tolerance) continue;
      const [left, right] = split(node.controls, .5), middle = (node.start + node.end) * .5, distance = magnitude(difference(left[3], target));
      if (distance < bestDistance || (distance === bestDistance && middle < bestT)) { bestT = middle; bestDistance = distance; }
      if (bestDistance - boxDistance(node.controls, target) <= this.tolerance) continue;
      if (node.depth === 32) throw resourceFailure();
      pending.push({ controls: right, start: middle, end: node.end, depth: node.depth + 1 }, { controls: left, start: node.start, end: middle, depth: node.depth + 1 });
    }
    return bestT;
  }
}
