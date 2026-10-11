import type { AffineV1, BoneTransformModeV1, RuntimeBoneV1, RuntimeTransformConstraintV1, RuntimeTransformPropertyV1 } from "../contracts.js";
import { RuntimeErrorV1 } from "../errors.js";
import { childWorldAffineV1 } from "./affine.js";
import { inheritedBasisV1 } from "./inherited-basis.js";

export type TransformChannels = [number, number, number, number, number, number];
type Pair = readonly [number, number];
const F32_EPSILON = 2 ** -23, F32_MIN = 2 ** -149;
const PROPERTIES: readonly RuntimeTransformPropertyV1[] = ["rotate", "x", "y", "scaleX", "scaleY", "shearY"];

class GeometryFailure extends RuntimeErrorV1 {
  constructor(readonly reason: "inconsistentPoint" | "unrepresentable" | "nonFinite") {
    super(reason === "nonFinite" ? "nonFinite" : "invalidState", "apply", "Transform geometry cannot be represented through the parent basis.");
  }
}
function checked(values: readonly number[]): void {
  if (!values.every(Number.isFinite)) throw new GeometryFailure("nonFinite");
}
export function narrowTransform(value: number, scale = false): number {
  checked([value]);
  const result = Math.fround(value);
  if (!Number.isFinite(result) || (scale && value !== 0 && result === 0)) throw new GeometryFailure("unrepresentable");
  return result;
}
export function wrapTransformAngle(value: number): number {
  let result = value % 360; if (result < 0) result += 360;
  return result >= 180 ? result - 360 : result;
}
export function localChannels(bone: RuntimeBoneV1): TransformChannels {
  const values: TransformChannels = [bone.rotation, bone.x, bone.y, bone.scaleX, bone.scaleY, bone.shearY];
  checked([...values, bone.shearX]); return values;
}
export function channelsToLocal(values: TransformChannels, template: RuntimeBoneV1): RuntimeBoneV1 {
  checked([...values, template.shearX]);
  return { ...template, rotation: narrowTransform(values[0]), x: narrowTransform(values[1]), y: narrowTransform(values[2]),
    scaleX: narrowTransform(values[3], true), scaleY: narrowTransform(values[4], true), shearY: narrowTransform(values[5]) };
}
export function worldChannels(matrix: AffineV1): TransformChannels {
  const { a, b, c, d, tx, ty } = matrix; checked([a, b, c, d, tx, ty]);
  const sx = Math.hypot(a, b), yLength = Math.hypot(c, d), degrees = 180 / Math.PI;
  if (sx === 0) return [yLength === 0 ? 0 : wrapTransformAngle(Math.atan2(d, c) * degrees - 90), tx, ty, 0, yLength, 0];
  const sign = a * d - b * c < 0 ? -1 : 1;
  const along = (a / sx) * c + (b / sx) * d, normal = (a / sx) * d - (b / sx) * c;
  const phi = yLength === 0 ? 0 : Math.atan2(-sign * along, sign * normal) * degrees;
  return [wrapTransformAngle(Math.atan2(b, a) * degrees), tx, ty, sx, sign * yLength, phi];
}
export function channelsToWorld(values: TransformChannels): AffineV1 {
  checked(values); const [theta, x, y, sx, sy, phi] = values;
  const t = wrapTransformAngle(theta) * (Math.PI / 180), p = wrapTransformAngle(phi) * (Math.PI / 180);
  const s = Math.sin(t), c = Math.cos(t), sp = Math.sin(p), cp = Math.cos(p);
  const result = { a: narrowTransform(sx * c), b: narrowTransform(sx * s),
    c: narrowTransform(-sy * (s * cp + c * sp)), d: narrowTransform(sy * (c * cp - s * sp)),
    tx: narrowTransform(x), ty: narrowTransform(y) };
  if ((sx !== 0 && result.a === 0 && result.b === 0) || (sy !== 0 && result.c === 0 && result.d === 0)) throw new GeometryFailure("unrepresentable");
  checkWorld(result); return result;
}
export function checkWorld(matrix: AffineV1): void {
  if (![matrix.a, matrix.b, matrix.c, matrix.d, matrix.tx, matrix.ty,
    Math.fround(Math.hypot(matrix.a, matrix.b)), Math.fround(Math.hypot(matrix.c, matrix.d))].every(Number.isFinite)) throw new GeometryFailure("unrepresentable");
}

export function inverseTransformPoint(matrix: AffineV1, point: Pair): Pair {
  const { a, b, c, d, tx, ty } = matrix; checked([a, b, c, d, tx, ty, ...point]);
  const rhs: Pair = [point[0] - tx, point[1] - ty], determinant = a * d - b * c;
  checked(rhs); let result: Pair;
  if (determinant !== 0) result = [(d * rhs[0] - c * rhs[1]) / determinant, (a * rhs[1] - b * rhs[0]) / determinant];
  else {
    const squared = a * a + b * b + c * c + d * d;
    if (squared === 0) {
      if (rhs[0] === 0 && rhs[1] === 0) return [0, 0];
      throw new GeometryFailure("inconsistentPoint");
    }
    result = [(a * rhs[0] + b * rhs[1]) / squared, (c * rhs[0] + d * rhs[1]) / squared];
  }
  checked(result);
  for (const [left, right, wanted] of [[a, c, rhs[0]], [b, d, rhs[1]]] as const) {
    const first = left * result[0], second = right * result[1], error = Math.abs(first + second - wanted);
    if (!Number.isFinite(error) || error > 64 * Number.EPSILON * (Math.abs(first) + Math.abs(second) + Math.abs(wanted))) throw new GeometryFailure("inconsistentPoint");
  }
  return result;
}

function roundoffPreimage(matrix: AffineV1, wanted: Pair, scale: number): Pair {
  const linear = { ...matrix, tx: 0, ty: 0 };
  try { return inverseTransformPoint(linear, wanted); }
  catch (error) { if (!(error instanceof GeometryFailure) || error.reason !== "inconsistentPoint") throw error; }
  const { a, b, c, d } = matrix, squared = a * a + b * b + c * c + d * d;
  if (a * d !== b * c || squared === 0) throw new GeometryFailure("inconsistentPoint");
  const result: Pair = [(a * wanted[0] + b * wanted[1]) / squared, (c * wanted[0] + d * wanted[1]) / squared];
  const observed: Pair = [a * result[0] + c * result[1], b * result[0] + d * result[1]];
  const tolerance = Math.max(64 * F32_EPSILON * scale, F32_MIN);
  if (!observed.every((v, i) => Number.isFinite(v) && Math.abs(v - wanted[i]!) <= tolerance)) throw new GeometryFailure("inconsistentPoint");
  return result;
}
function nearestPoint(matrix: AffineV1, wanted: Pair, reference: Pair): Pair {
  if (matrix.a * matrix.d !== matrix.b * matrix.c) return inverseTransformPoint(matrix, wanted);
  const image: Pair = [matrix.a * reference[0] + matrix.c * reference[1] + matrix.tx,
    matrix.b * reference[0] + matrix.d * reference[1] + matrix.ty];
  const delta = roundoffPreimage(matrix, [wanted[0] - image[0], wanted[1] - image[1]], Math.max(Math.hypot(...wanted), Math.hypot(...image)));
  return [reference[0] + delta[0], reference[1] + delta[1]];
}
function nearestAxis(parent: AffineV1, mode: BoneTransformModeV1, wanted: Pair, reference: Pair): Pair {
  if (mode === "inheritAll") return nearestPoint({ ...parent, tx: 0, ty: 0 }, wanted, reference);
  if (mode === "translationOnly") return wanted;
  const [a, b, c, d] = inheritedBasisV1(parent, mode);
  if (mode !== "ignoreRotationAndReflection") return [a * wanted[0] + b * wanted[1], c * wanted[0] + d * wanted[1]];
  const fullRank = parent.a * parent.d !== parent.b * parent.c;
  const image: Pair = [a * reference[0] + c * reference[1], b * reference[0] + d * reference[1]];
  const rhs: Pair = fullRank ? wanted : [wanted[0] - image[0], wanted[1] - image[1]];
  const preimage = roundoffPreimage(parent, rhs, Math.max(Math.hypot(...wanted), Math.hypot(...image)));
  const [qa, qb, qc, qd] = inheritedBasisV1(parent, "ignoreScale");
  const delta: Pair = [qa * preimage[0] + qc * preimage[1], qb * preimage[0] + qd * preimage[1]];
  return fullRank ? delta : [reference[0] + delta[0], reference[1] + delta[1]];
}

export function recoverLocalTransform(requested: AffineV1, parent: AffineV1, mode: BoneTransformModeV1, reference: RuntimeBoneV1): RuntimeBoneV1 {
  localChannels(reference); worldChannels(requested);
  const origin = nearestPoint(parent, [requested.tx, requested.ty], [reference.x, reference.y]);
  const localAxis = (y: boolean): Pair => {
    const a = (reference.rotation + (y ? reference.shearY + 90 : reference.shearX)) * (Math.PI / 180);
    const scale = y ? reference.scaleY : reference.scaleX; return [scale * Math.cos(a), scale * Math.sin(a)];
  };
  const x = nearestAxis(parent, mode, [requested.a, requested.b], localAxis(false));
  const y = nearestAxis(parent, mode, [requested.c, requested.d], localAxis(true));
  const sxSign = reference.scaleX < 0 ? -1 : 1, sySign = reference.scaleY < 0 ? -1 : 1;
  const next = { ...reference, x: narrowTransform(origin[0]), y: narrowTransform(origin[1]),
    scaleX: narrowTransform(sxSign * Math.hypot(...x), true), scaleY: narrowTransform(sySign * Math.hypot(...y), true) };
  if (x[0] !== 0 || x[1] !== 0) next.rotation = narrowTransform(wrapTransformAngle(Math.atan2(sxSign * x[1], sxSign * x[0]) * (180 / Math.PI) - reference.shearX));
  if (y[0] !== 0 || y[1] !== 0) next.shearY = narrowTransform(wrapTransformAngle(Math.atan2(sySign * y[1], sySign * y[0]) * (180 / Math.PI) - next.rotation - 90));
  const actual = childWorldAffineV1(parent, next, mode); checkWorld(actual);
  for (const [a, b] of [[[actual.a, actual.b], [requested.a, requested.b]], [[actual.c, actual.d], [requested.c, requested.d]]] as const) {
    if ((b[0] !== 0 || b[1] !== 0) && a[0] === 0 && a[1] === 0) throw new GeometryFailure("unrepresentable");
    const tolerance = Math.max(64 * F32_EPSILON * Math.max(Math.hypot(...a), Math.hypot(...b)), F32_MIN);
    if (Math.abs(a[0] - b[0]) > tolerance || Math.abs(a[1] - b[1]) > tolerance) throw new GeometryFailure("unrepresentable");
  }
  for (const [a, b, left, right, originValue] of [[actual.tx, requested.tx, parent.a, parent.c, parent.tx], [actual.ty, requested.ty, parent.b, parent.d, parent.ty]] as const) {
    const extent = Math.abs(left * next.x) + Math.abs(right * next.y) + Math.abs(originValue);
    if (Math.abs(a - b) > Math.max(64 * F32_EPSILON * Math.max(extent, Math.abs(b)), F32_MIN)) throw new GeometryFailure("unrepresentable");
  }
  return next;
}

export interface TransformRoute { readonly from: number; readonly to: number; readonly sourceOrigin: number; readonly targetOrigin: number; readonly gain: number; readonly limit: number | null; }
export function transformRoutes(constraint: RuntimeTransformConstraintV1): TransformRoute[] {
  if (constraint.mapping === null) return PROPERTIES.map((_, i) => ({ from: i, to: i, sourceOrigin: 0, targetOrigin: 0, gain: 1, limit: null }));
  const routes: TransformRoute[] = [];
  for (const source of constraint.mapping.properties) for (const target of source.targets) {
    if (routes.length >= 1_000_000) throw new RuntimeErrorV1("resourceLimit", "apply", "Transform route budget is exhausted.");
    routes.push({ from: PROPERTIES.indexOf(source.property), to: PROPERTIES.indexOf(target.property), sourceOrigin: source.offset,
      targetOrigin: target.offset, gain: target.scale, limit: constraint.mapping.clamp ? target.max : null });
  }
  return routes;
}
export function evaluateTransformChannels(source: TransformChannels, current: TransformChannels, routes: readonly TransformRoute[], mixes: readonly number[], relative: boolean): TransformChannels {
  checked([...source, ...current, ...mixes]); const output: TransformChannels = [...current];
  for (const route of routes) {
    checked([route.sourceOrigin, route.targetOrigin, route.gain, ...(route.limit === null ? [] : [route.limit])]);
    const i = route.to, m = mixes[i]!; if (m === 0) continue;
    let z = route.targetOrigin + (source[route.from]! - route.sourceOrigin) * route.gain; checked([z]);
    if (route.limit !== null) z = Math.max(Math.min(route.targetOrigin, route.limit), Math.min(Math.max(route.targetOrigin, route.limit), z));
    const q = output[i]!; let next: number;
    if (i === 0 || i === 5) { const delta = relative ? z : wrapTransformAngle(z - q); next = delta === 0 ? q : wrapTransformAngle(wrapTransformAngle(q) + m * delta); }
    else if (relative && (i === 3 || i === 4)) next = z === 1 ? q : q * ((1 - m) + m * z);
    else if (relative) next = z === 0 ? q : q + m * z;
    else next = q === z ? q : (1 - m) * q + m * z;
    checked([next]); output[i] = next;
  }
  return output;
}
export function transformSourceChannels(source: TransformChannels, offsets: readonly number[], world: AffineV1 | null): TransformChannels {
  const result = source.map((v, i) => v + offsets[i]!) as TransformChannels;
  if (world !== null) {
    result[1] = world.a * offsets[1]! + world.c * offsets[2]! + world.tx;
    result[2] = world.b * offsets[1]! + world.d * offsets[2]! + world.ty;
  }
  checked(result); return result;
}
