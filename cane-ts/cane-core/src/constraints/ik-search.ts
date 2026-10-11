import { RuntimeErrorV1 } from "../errors.js";
import { ikFailure, type Vector2 } from "../math/ik-geometry.js";
import { wrapTransformAngle } from "../math/transform-channels.js";

export interface IkObservation { readonly residual: number; readonly error: Vector2; readonly bendMatches: boolean; }
type Evaluator = (parameters: readonly number[]) => IkObservation;
type Bound = readonly [number, number];
export interface IkSearchResult { readonly parameters: number[]; readonly observation: IkObservation; readonly passes: number; }
export function feasibleIk<T>(evaluate: () => T): T | null {
  try { return evaluate(); } catch (error) { if (error instanceof RuntimeErrorV1 && error.runtimeName === "nonFinite") return null; throw error; }
}
const clamp = (x: number, low: number, high: number) => Math.max(low, Math.min(high, x));
function minimizeCoordinate(values: number[], coordinate: number, [lower, upper]: Bound, evaluate: Evaluator): void {
  if (lower === upper) return;
  let bestX = values[coordinate]!, bestError = evaluate(values).residual;
  const probes: Array<readonly [number, number]> = [[bestX, bestError]], step = (upper - lower) / 32;
  const sample = (x: number) => {
    values[coordinate] = x; const error = feasibleIk(() => evaluate(values))?.residual ?? Infinity;
    if (error < bestError) { bestX = x; bestError = error; } return error;
  };
  for (let i = 0; i <= 32; i++) { const x = lower + i * step; probes.push([x, sample(x)]); }
  probes.sort((a, b) => a[1] - b[1] || a[0] - b[0]);
  const fraction = (Math.sqrt(5) - 1) / 2;
  for (const [position] of probes.slice(0, 2)) {
    const x = clamp(position, lower, upper); let low = Math.max(x - step, lower), high = Math.min(x + step, upper);
    let left = high - fraction * (high - low), right = low + fraction * (high - low), fl = sample(left), fr = sample(right);
    for (let i = 0; i < 40; i++) {
      if (fl <= fr) { high = right; right = left; fr = fl; left = high - fraction * (high - low); fl = sample(left); }
      else { low = left; left = right; fl = fr; right = low + fraction * (high - low); fr = sample(right); }
    }
  }
  values[coordinate] = bestX;
}
function coupledStep(values: number[], bounds: readonly Bound[], angularCount: number, baseline: IkObservation, evaluate: Evaluator): IkObservation {
  const jacobian: Vector2[] = values.map(() => [0, 0]);
  for (let i = 0; i < values.length; i++) {
    const original = values[i]!, [low, high] = bounds[i]!; if (low === high || original < low || original > high) continue;
    const step = i < angularCount ? 0.05 : 0.001, left = Math.max(original - step, low), right = Math.min(original + step, high);
    values[i] = left; const a = feasibleIk(() => evaluate(values)); values[i] = right; const b = feasibleIk(() => evaluate(values)); values[i] = original;
    if (a !== null && b !== null) jacobian[i] = [(b.error[0] - a.error[0]) / (right - left), (b.error[1] - a.error[1]) / (right - left)];
  }
  let a = 0, b = 0, d = 0; for (const j of jacobian) { a += j[0] * j[0]; b += j[0] * j[1]; d += j[1] * j[1]; }
  const damping = (a + d) * 1e-6; if (damping === 0 || !Number.isFinite(damping)) return baseline;
  a += damping; d += damping; const determinant = a * d - b * b; if (determinant <= 0 || !Number.isFinite(determinant)) return baseline;
  const v: Vector2 = [(d * baseline.error[0] - b * baseline.error[1]) / determinant, (a * baseline.error[1] - b * baseline.error[0]) / determinant];
  const delta = jacobian.map(j => -j[0] * v[0] - j[1] * v[1]);
  let maximum = 1; for (let i = 0; i < delta.length; i++) maximum = Math.max(maximum, Math.abs(delta[i]!) / (i < angularCount ? 30 : 1));
  if (!Number.isFinite(maximum)) return baseline;
  const original = [...values]; let factor = 1 / maximum;
  for (let attempt = 0; attempt < 10; attempt++) {
    for (let i = 0; i < values.length; i++) values[i] = clamp(original[i]! + delta[i]! * factor, ...bounds[i]!);
    const candidate = feasibleIk(() => evaluate(values)); if (candidate !== null && candidate.residual < baseline.residual) return candidate;
    factor *= 0.5;
  }
  for (let i = 0; i < values.length; i++) values[i] = original[i]!; return baseline;
}
export function minimizeIk(initial: readonly number[], bounds: readonly Bound[], angularCount: number, iterations: number, threshold: number, evaluate: Evaluator): IkSearchResult {
  if (initial.length === 0 || initial.length !== bounds.length || angularCount > initial.length || !Number.isFinite(threshold) || threshold < 0
    || !initial.every(Number.isFinite) || !bounds.every(([a, b]) => Number.isFinite(a) && Number.isFinite(b) && a <= b)) throw ikFailure();
  let winner: IkSearchResult = { parameters: [...initial], observation: evaluate(initial), passes: 0 };
  if (winner.observation.residual <= threshold && winner.observation.bendMatches) return winner;
  for (const offset of angularCount === 2 ? [0, 90, -90, 180] : [0]) {
    const values = [...initial]; if (angularCount === 2) values[1] = wrapTransformAngle(values[1]! + offset);
    let current = feasibleIk(() => evaluate(values)); if (current === null) continue;
    let passes = 0;
    for (let pass = 0; pass < iterations; pass++) {
      if (current.residual <= threshold && current.bendMatches) break;
      const before = current.residual;
      for (let coordinate = values.length - 1; coordinate >= 0; coordinate--) { minimizeCoordinate(values, coordinate, bounds[coordinate]!, evaluate); current = evaluate(values); }
      current = coupledStep(values, bounds, angularCount, current, evaluate); passes = pass + 1;
      if (current.residual >= before) break;
    }
    const old = winner.observation;
    const better = current.residual <= threshold && old.residual <= threshold && current.bendMatches !== old.bendMatches ? current.bendMatches : current.residual < old.residual;
    if (better) winner = { parameters: values, observation: current, passes };
    if (winner.observation.residual <= threshold && winner.observation.bendMatches) break;
  }
  return winner;
}
