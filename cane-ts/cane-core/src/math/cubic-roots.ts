import { RuntimeErrorV1 } from "../errors.js";

type Cubic = readonly [number, number, number, number];

function evaluate(p: Cubic, u: number): number {
  const lerp = (a: number, b: number): number => a * (1 - u) + b * u;
  const a = lerp(p[0], p[1]), b = lerp(p[1], p[2]), c = lerp(p[2], p[3]);
  return lerp(lerp(a, b), lerp(b, c));
}

function boundaries(p: Cubic): number[] {
  const d0 = p[1] - p[0], d1 = p[2] - p[1], d2 = p[3] - p[2];
  let a = d0 - 2 * d1 + d2, b = 2 * (d1 - d0), c = d0;
  if (![a, b, c].every(Number.isFinite)) fail();
  const scale = Math.max(Math.abs(a), Math.abs(b), Math.abs(c));
  const points = [0, 0.5];
  const add = (u: number): void => { if (u > 0 && u < 0.5) points.push(u); };
  if (scale !== 0) {
    a /= scale; b /= scale; c /= scale;
    if (a === 0) { if (b !== 0) add(-c / b); }
    else {
      const discriminant = b * b - 4 * a * c;
      if (discriminant >= 0) {
        const root = Math.sqrt(discriminant);
        const q = -0.5 * (b + (b < 0 || Object.is(b, -0) ? -root : root));
        if (q === 0) add(-b / (2 * a));
        else { add(q / a); add(c / q); }
      }
    }
  }
  return [...new Set(points)].sort((x, y) => x - y);
}

function firstHalf(p: Cubic, target: number, reverse: boolean): number | null {
  const points = boundaries(p);
  for (let index = 0; index + 1 < points.length; index += 1) {
    let a = points[reverse ? points.length - 1 - index : index]!;
    let b = points[reverse ? points.length - 2 - index : index + 1]!;
    let fa = evaluate(p, a);
    const fb = evaluate(p, b);
    if (fa === target) return a;
    if (fb === target) return b;
    if ((fa < target) === (fb < target)) continue;
    for (let iteration = 0; iteration < 384; iteration += 1) {
      const mid = a + (b - a) * 0.5;
      if (mid === a || mid === b) return Math.abs(fa - target) <= Math.abs(evaluate(p, b) - target) ? a : b;
      const fm = evaluate(p, mid);
      if (fm === target) return mid;
      if ((fm < target) === (fa < target)) { a = mid; fa = fm; }
      else b = mid;
    }
    fail();
  }
  return null;
}

function fail(): never {
  throw new RuntimeErrorV1("nonFinite", "sampleCurve", "Cubic intersection cannot be represented or resolved.");
}

/** Evaluate the earliest time intersection, retaining distance from either endpoint. */
export function sampleCubicFirst(x: Cubic, y: Cubic, target: number): number {
  if (![...x, ...y, target].every(Number.isFinite)) fail();
  const u = firstHalf(x, target, false);
  let result: number;
  if (u !== null) result = evaluate(y, u);
  else {
    const v = firstHalf([x[3], x[2], x[1], x[0]], target, true);
    if (v === null) fail();
    result = evaluate([y[3], y[2], y[1], y[0]], v);
  }
  if (!Number.isFinite(result)) fail();
  return result;
}
