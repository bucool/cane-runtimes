import type {
  RuntimeCurvePropertyNameV1,
  RuntimeCurveV1,
  RuntimeSimpleCurveV1,
} from "../contracts.js";
import { RuntimeErrorV1 } from "../errors.js";
import { sampleCubicFirst } from "../math/cubic-roots.js";
import { f32, f32Add, f32Sub } from "../math/f32.js";

export const NO_CONTRIBUTION_V1: unique symbol = Symbol("cane.runtime.no-contribution");
export type RuntimeSampleV1<T> = T | typeof NO_CONTRIBUTION_V1;

const GROUPED_LAST_KEYS_CACHE = new WeakMap<object, readonly unknown[]>();

export interface RuntimeTimedCurveKeyV1 {
  readonly time: number;
  readonly curve: RuntimeCurveV1;
}

export function selectPropertyCurveV1(
  curve: RuntimeCurveV1,
  property: RuntimeCurvePropertyNameV1,
): RuntimeSimpleCurveV1 {
  if (curve === null || typeof curve === "string" || curve.type !== "properties") {
    return curve;
  }
  if (Object.prototype.hasOwnProperty.call(curve.properties, property)) {
    return curve.properties[property] ?? null;
  }
  return curve.default;
}

export function sampleCurveValueV1(
  curve: RuntimeSimpleCurveV1,
  progress: number,
  from: number,
  to: number,
  options: { readonly angular?: boolean; readonly forceStepped?: boolean } = {},
): number {
  const p = Math.fround(Math.min(Math.max(Math.fround(progress), 0), 1));
  const start = Math.fround(from);
  const end = options.angular === true
    ? Math.fround(start + wrapDegreesV1(Math.fround(Math.fround(to) - start)))
    : Math.fround(to);
  if (![p, start, end].every(Number.isFinite)) {
    throw new RuntimeErrorV1("nonFinite", "sampleCurve", "Animation input must be finite.");
  }
  if (p <= 0) return start;
  if (p >= 1) return end;
  let value: number;
  if (options.forceStepped === true || curve === "stepped") value = start;
  else if (curve === null || curve === "linear") value = interpolateLinear(start, end, p, false);
  else {
    const y1 = curve.type === "bezier" ? start + (end - start) * curve.cy1 : start + curve.dy1;
    const y2 = curve.type === "bezier" ? start + (end - start) * curve.cy2 : end + curve.dy2;
    value = sampleCubicFirst([0, curve.cx1, curve.cx2, 1], [start, y1, y2, end], p);
  }
  const result = Math.fround(value);
  if (!Number.isFinite(result)) {
    throw new RuntimeErrorV1("nonFinite", "sampleCurve", "Animation result cannot be represented as binary32.");
  }
  return result;
}

export function sampleContinuousKeysV1<K extends RuntimeTimedCurveKeyV1>(
  keys: readonly K[],
  time: number,
  property: RuntimeCurvePropertyNameV1,
  value: (key: K) => number,
  options: { readonly angular?: boolean; readonly forceStepped?: boolean } = {},
): RuntimeSampleV1<number> {
  if (keys.length === 0) return NO_CONTRIBUTION_V1;
  const sampleTime = f32(time);
  const groups = groupedLastKeys(keys);
  const first = groups[0];
  if (first === undefined || sampleTime < first.time) return NO_CONTRIBUTION_V1;

  const currentIndex = lastKeyIndexAtOrBefore(groups, sampleTime);
  const current = groups[currentIndex];
  if (current === undefined) return NO_CONTRIBUTION_V1;
  const next = groups[currentIndex + 1];
  if (sampleTime === current.time || next === undefined) return f32(value(current));

  const denominator = f32Sub(next.time, current.time);
  if (denominator === 0) return f32(value(current));
  const progress = f32(f32Sub(sampleTime, current.time) / denominator);
  return sampleCurveValueV1(
    selectPropertyCurveV1(current.curve, property),
    progress,
    value(current),
    value(next),
    options,
  );
}

/**
 * Samples an interleaved numeric array after locating its key interval once.
 * Even and odd components may select different property curves (for example,
 * x/y deform components), but each curve's time transform is prepared once.
 */
export function sampleContinuousArrayKeysV1<K extends RuntimeTimedCurveKeyV1>(
  keys: readonly K[],
  time: number,
  value: (key: K) => readonly number[],
  output: number[],
  evenProperty: RuntimeCurvePropertyNameV1,
  oddProperty: RuntimeCurvePropertyNameV1,
  forceStepped = false,
): boolean {
  if (keys.length === 0) return false;
  const sampleTime = f32(time);
  const groups = groupedLastKeys(keys);
  const first = groups[0];
  if (first === undefined || sampleTime < first.time) return false;

  const currentIndex = lastKeyIndexAtOrBefore(groups, sampleTime);
  const current = groups[currentIndex];
  if (current === undefined) return false;
  const currentValues = value(current);
  const next = groups[currentIndex + 1];
  if (sampleTime === current.time || next === undefined) {
    copySampledArrayV1(currentValues, output);
    return true;
  }

  const denominator = f32Sub(next.time, current.time);
  if (denominator === 0) {
    copySampledArrayV1(currentValues, output);
    return true;
  }
  const progress = f32(f32Sub(sampleTime, current.time) / denominator);
  const nextValues = value(next);
  sampleArrayChannelV1(
    selectPropertyCurveV1(current.curve, evenProperty),
    progress,
    currentValues,
    nextValues,
    output,
    0,
    forceStepped,
  );
  sampleArrayChannelV1(
    selectPropertyCurveV1(current.curve, oddProperty),
    progress,
    currentValues,
    nextValues,
    output,
    1,
    forceStepped,
  );
  return true;
}

/** Samples a small fixed component vector after one shared key-interval lookup. */
export function sampleContinuousComponentArrayKeysV1<K extends RuntimeTimedCurveKeyV1>(
  keys: readonly K[],
  time: number,
  value: (key: K) => readonly number[],
  output: number[],
  properties: readonly RuntimeCurvePropertyNameV1[],
  forceStepped = false,
): boolean {
  if (keys.length === 0) return false;
  const sampleTime = f32(time);
  const groups = groupedLastKeys(keys);
  const first = groups[0];
  if (first === undefined || sampleTime < first.time) return false;
  const currentIndex = lastKeyIndexAtOrBefore(groups, sampleTime);
  const current = groups[currentIndex];
  if (current === undefined) return false;
  const currentValues = value(current);
  const next = groups[currentIndex + 1];
  if (sampleTime === current.time || next === undefined) {
    copySampledArrayV1(currentValues, output);
    return true;
  }
  const denominator = f32Sub(next.time, current.time);
  if (denominator === 0) {
    copySampledArrayV1(currentValues, output);
    return true;
  }
  const progress = f32(Math.min(Math.max(f32(f32Sub(sampleTime, current.time) / denominator), 0), 1));
  const nextValues = value(next);
  if (progress <= 0 || forceStepped) {
    copySampledArrayV1(currentValues, output);
    return true;
  }
  if (progress >= 1) {
    copySampledArrayV1(nextValues, output);
    return true;
  }
  for (let component = 0; component < output.length; component += 1) {
    const property = properties[component];
    output[component] = property === undefined ? f32(currentValues[component] ?? 0)
      : sampleCurveValueV1(selectPropertyCurveV1(current.curve, property), progress,
        currentValues[component] ?? 0, nextValues[component] ?? 0, { forceStepped });
  }
  return true;
}

export function sampleDiscreteKeysV1<K extends { readonly time: number }, T>(
  keys: readonly K[],
  time: number,
  value: (key: K) => T,
): RuntimeSampleV1<T> {
  const sampleTime = f32(time);
  const selectedIndex = lastKeyIndexAtOrBefore(keys, sampleTime);
  const selected = selectedIndex < 0 ? undefined : keys[selectedIndex];
  return selected === undefined ? NO_CONTRIBUTION_V1 : value(selected);
}

export function wrapDegreesV1(value: number): number {
  const shifted = f32Add(value, 180);
  const modulus = 360;
  const remainder = ((shifted % modulus) + modulus) % modulus;
  return f32Sub(remainder, 180);
}

export function quantizeSampleTimeV1(time: number, frameStepSeconds: number): number {
  const step = f32(frameStepSeconds);
  const ratio = f32(time) / step;
  return f32(Math.floor(ratio + 0.5) * step);
}

function groupedLastKeys<K extends { readonly time: number }>(keys: readonly K[]): K[] {
  const cached = GROUPED_LAST_KEYS_CACHE.get(keys);
  if (cached !== undefined) return cached as K[];
  const result: K[] = [];
  for (const key of keys) {
    const previous = result[result.length - 1];
    if (previous !== undefined && previous.time === key.time) {
      result[result.length - 1] = key;
    } else {
      result.push(key);
    }
  }
  GROUPED_LAST_KEYS_CACHE.set(keys, result);
  return result;
}

/** Returns the final duplicate at or before time for a validated, sorted timeline. */
function lastKeyIndexAtOrBefore<K extends { readonly time: number }>(keys: readonly K[], time: number): number {
  let low = 0;
  let high = keys.length;
  while (low < high) {
    const middle = (low + high) >>> 1;
    const key = keys[middle];
    if (key !== undefined && key.time <= time) low = middle + 1;
    else high = middle;
  }
  return low - 1;
}

function interpolateLinear(from: number, to: number, progress: number, angular: boolean): number {
  const start = Math.fround(from);
  const delta = Math.fround(Math.fround(to) - start);
  const resolvedDelta = angular ? wrapDegreesV1(delta) : delta;
  return Math.fround(start + Math.fround(resolvedDelta * Math.fround(progress)));
}

function copySampledArrayV1(source: readonly number[], output: number[]): void {
  for (let index = 0; index < output.length; index += 1) {
    output[index] = f32(source[index] ?? 0);
  }
}

function sampleArrayChannelV1(
  curve: RuntimeSimpleCurveV1,
  progress: number,
  fromValues: readonly number[],
  toValues: readonly number[],
  output: number[],
  parity: 0 | 1,
  forceStepped: boolean,
): void {
  for (let index = parity; index < output.length; index += 2) {
    output[index] = sampleCurveValueV1(curve, progress, fromValues[index] ?? 0,
      toValues[index] ?? 0, { forceStepped });
  }
}
