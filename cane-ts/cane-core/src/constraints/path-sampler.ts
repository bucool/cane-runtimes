import type { RuntimePathAttachmentV1 } from "../contracts.js";
import { ArcBudget, CubicArc, pathGeometryFailure, type ArcPoint } from "../math/cubic-arc.js";

export interface PathPointV1 { readonly x: number; readonly y: number; }
export interface PathSampleV1 extends PathPointV1 { readonly tangentDegrees: number; }
type MutableSample = { x: number; y: number; tangentDegrees: number };
function finiteFloat(value: number): number { const v = Math.fround(value); if (!Number.isFinite(v)) throw pathGeometryFailure(); return v; }
function modulo(value: number, span: number): number { const r = value % span; return r < 0 ? r + span : r; }
function setupLengths(curves: readonly CubicArc[]): number[] {
  let cumulative = 0; const result: number[] = [];
  for (const curve of curves) {
    const base = curve.controls[0], p = curve.controls.map(point => [point[0] - base[0], point[1] - base[1]] as const);
    let previous: ArcPoint = [0, 0];
    for (let step = 1; step <= 24; step++) {
      const t = step / 24, u = 1 - t, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
      const point: ArcPoint = [b * p[1]![0] + c * p[2]![0] + d * p[3]![0], b * p[1]![1] + c * p[2]![1] + d * p[3]![1]];
      cumulative += Math.hypot(point[0] - previous[0], point[1] - previous[1]); previous = point;
    }
    if (cumulative > 3.4028234663852886e38) throw pathGeometryFailure(); result.push(finiteFloat(cumulative));
  }
  return result;
}
/** Shared adaptive geometry for placement and inverse projection queries. */
export class RuntimePathSamplerWorkspaceV1 {
  closed = false; private constant = true; private total = 0;
  private curves: CubicArc[] = []; private lengths: number[] = [];
  get totalLength(): number { return finiteFloat(this.total); }
  reset(path: RuntimePathAttachmentV1, flat: readonly number[]): this | null {
    const knots = flat.length / 6; if (!Number.isInteger(knots) || knots < 2 || flat.length !== path.vertices.length) return null;
    const curves: CubicArc[] = [], lengths: number[] = [], budget = new ArcBudget(); const count = path.closed ? knots : knots - 1;
    let total = 0; const read = (i: number): ArcPoint => [flat[i * 2]!, flat[i * 2 + 1]!];
    for (let i = 0; i < count; i++) {
      const next = (i + 1) % knots;
      const curve = new CubicArc([read(i * 3 + 1), read(i * 3 + 2), read(next * 3), read(next * 3 + 1)], budget);
      total += curve.length; finiteFloat(total); lengths.push(total); curves.push(curve);
    }
    if (!path.constantSpeed) {
      const stored = path.lengths.slice(0, count);
      const authored = path.lengths.length === knots && stored.every((v, i) => Number.isFinite(v) && v >= (i === 0 ? 0 : stored[i - 1]!));
      const values = authored ? stored : setupLengths(curves); lengths.length = 0; for (const v of values) lengths.push(v); total = lengths[lengths.length - 1] ?? 0;
    }
    this.closed = path.closed; this.constant = path.constantSpeed; this.curves = curves; this.lengths = lengths; this.total = total; return this;
  }
  private output(point: ArcPoint, tangent: ArcPoint, output: MutableSample | null): PathSampleV1 {
    const value = output ?? { x: 0, y: 0, tangentDegrees: 0 };
    value.x = finiteFloat(point[0]); value.y = finiteFloat(point[1]); value.tangentDegrees = finiteFloat(Math.atan2(tangent[1], tangent[0]) * (180 / Math.PI)); return value;
  }
  sample(distance: number, output: MutableSample | null = null): PathSampleV1 {
    let d = finiteFloat(distance);
    if (this.closed && this.total > 0) d = modulo(d, this.total);
    else if (!this.closed && (d < 0 || d > this.total)) {
      const index = d < 0 ? 0 : this.curves.length - 1, t = d < 0 ? 0 : 1, extra = d < 0 ? d : d - this.total;
      const curve = this.curves[index]!, p = curve.point(t), tangent = curve.tangent(t), norm = Math.hypot(...tangent);
      return this.output([p[0] + extra * tangent[0] / norm, p[1] + extra * tangent[1] / norm], tangent, output);
    }
    let index = 0; while (index + 1 < this.lengths.length && this.lengths[index]! < d) index++;
    const start = index === 0 ? 0 : this.lengths[index - 1]!, span = this.lengths[index]! - start, curve = this.curves[index]!;
    const t = span === 0 ? 0 : this.constant ? curve.parameterAtDistance(d - start) : Math.max(0, Math.min(1, (d - start) / span));
    return this.output(curve.point(t), curve.tangent(t), output);
  }
  projectDistance(point: PathPointV1): number {
    const target: ArcPoint = [point.x, point.y], budget = new ArcBudget(); let bestError = Infinity, bestDistance = 0;
    const consider = (p: ArcPoint, distance: number) => {
      const error = Math.hypot(target[0] - p[0], target[1] - p[1]); if (error < bestError || (error === bestError && distance < bestDistance)) { bestError = error; bestDistance = distance; }
    };
    for (let i = 0; i < this.curves.length; i++) {
      const curve = this.curves[i]!, t = curve.closestParameter(target, budget), start = i === 0 ? 0 : this.lengths[i - 1]!;
      consider(curve.point(t), start + (this.constant ? curve.distanceAtParameter(t) : t * (this.lengths[i]! - start)));
    }
    if (!this.closed) for (const [index, t, end] of [[0, 0, false], [this.curves.length - 1, 1, true]] as const) {
      const curve = this.curves[index]!, p = curve.point(t), tangent = curve.tangent(t), norm = Math.hypot(...tangent), ux = tangent[0] / norm, uy = tangent[1] / norm;
      const along = (target[0] - p[0]) * ux + (target[1] - p[1]) * uy;
      if ((!end && along < 0) || (end && along > 0)) consider([p[0] + along * ux, p[1] + along * uy], end ? this.total + along : along);
    }
    return finiteFloat(this.closed && this.total > 0 ? modulo(bestDistance, this.total) : bestDistance);
  }
}
