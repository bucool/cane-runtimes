import type { AffineV1, BoneTransformModeV1 } from "../contracts.js";
import { RuntimeErrorV1 } from "../errors.js";

/** Column-major binary64 basis; round only after applying a child axis. */
export function inheritedBasisV1(parent: AffineV1, mode: BoneTransformModeV1): readonly [number, number, number, number] {
  const { a, b, c, d } = parent;
  if (![a, b, c, d].every(Number.isFinite)) {
    throw new RuntimeErrorV1("nonFinite", "composeBoneWorld", "Parent basis must be finite.");
  }
  if (mode === "inheritAll") return [a, b, c, d];
  if (mode === "translationOnly") return [1, 0, 0, 1];
  const unit = (x: number, y: number): readonly [number, number] => {
    const length = Math.hypot(x, y);
    return length === 0 ? [1, 0] : [x / length, y / length];
  };
  const [rx, ry] = unit(a + d, b - c);
  if (mode === "ignoreScaleAndReflection") return [rx, ry, -ry, rx];
  let qa = rx, qb = ry, qc = -ry, qd = rx;
  if (a * d - b * c < 0) {
    const [fx, fy] = unit(a - d, b + c);
    qa = fx; qb = fy; qc = fy; qd = -fx;
  }
  if (mode === "ignoreScale") return [qa, qb, qc, qd];
  if (mode !== "ignoreRotationAndReflection") {
    throw new RuntimeErrorV1("invalidArgument", "composeBoneWorld", "Unknown bone inheritance mode.");
  }
  const xy = ((a * qb + c * qd) + (b * qa + d * qc)) * 0.5;
  return [a * qa + c * qc, xy, xy, b * qb + d * qd];
}
