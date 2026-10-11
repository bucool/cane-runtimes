import { inheritedBasisV1 } from "./inherited-basis.js";
import type {
  AffineV1,
  BoneTransformModeV1,
  RootTransformV1,
  RuntimeBoneV1,
} from "../contracts.js";
import {
  degreesToRadians,
  f32,
  f32Add,
  f32Mul,
  f32Sub,
} from "./f32.js";

export const IDENTITY_AFFINE_V1: AffineV1 = Object.freeze({
  a: 1,
  b: 0,
  c: 0,
  d: 1,
  tx: 0,
  ty: 0,
});

export const IDENTITY_ROOT_TRANSFORM_V1: RootTransformV1 = Object.freeze({
  x: 0,
  y: 0,
  rotationDegrees: 0,
  scaleX: 1,
  scaleY: 1,
});

type MutableAffineV1 = { -readonly [Property in keyof AffineV1]: AffineV1[Property] };

function affine(
  a: number,
  b: number,
  c: number,
  d: number,
  tx: number,
  ty: number,
  output: AffineV1 | null = null,
): AffineV1 {
  const nextA = f32(a);
  const nextB = f32(b);
  const nextC = f32(c);
  const nextD = f32(d);
  const nextTx = f32(tx);
  const nextTy = f32(ty);
  if (output !== null) {
    const mutable = output as MutableAffineV1;
    mutable.a = nextA;
    mutable.b = nextB;
    mutable.c = nextC;
    mutable.d = nextD;
    mutable.tx = nextTx;
    mutable.ty = nextTy;
    return output;
  }
  return { a: nextA, b: nextB, c: nextC, d: nextD, tx: nextTx, ty: nextTy };
}

export function multiplyAffineV1(
  left: AffineV1,
  right: AffineV1,
  output: AffineV1 | null = null,
): AffineV1 {
  // Normalize each public input exactly once, then round at the same f32
  // multiplication/addition boundaries as Rust. Besides avoiding redundant
  // Math.fround calls, caching all fields makes output aliasing either input
  // safe without allocating a temporary affine in performance mode.
  const la = Math.fround(left.a);
  const lb = Math.fround(left.b);
  const lc = Math.fround(left.c);
  const ld = Math.fround(left.d);
  const ltx = Math.fround(left.tx);
  const lty = Math.fround(left.ty);
  const ra = Math.fround(right.a);
  const rb = Math.fround(right.b);
  const rc = Math.fround(right.c);
  const rd = Math.fround(right.d);
  const rtx = Math.fround(right.tx);
  const rty = Math.fround(right.ty);
  const a = Math.fround(Math.fround(la * ra) + Math.fround(lc * rb));
  const b = Math.fround(Math.fround(lb * ra) + Math.fround(ld * rb));
  const c = Math.fround(Math.fround(la * rc) + Math.fround(lc * rd));
  const d = Math.fround(Math.fround(lb * rc) + Math.fround(ld * rd));
  const tx = Math.fround(Math.fround(Math.fround(la * rtx) + Math.fround(lc * rty)) + ltx);
  const ty = Math.fround(Math.fround(Math.fround(lb * rtx) + Math.fround(ld * rty)) + lty);
  if (output !== null) {
    const mutable = output as MutableAffineV1;
    mutable.a = a;
    mutable.b = b;
    mutable.c = c;
    mutable.d = d;
    mutable.tx = tx;
    mutable.ty = ty;
    return output;
  }
  return { a, b, c, d, tx, ty };
}

export function transformPointV1(
  matrix: AffineV1,
  x: number,
  y: number,
  output: [number, number] | null = null,
): readonly [number, number] {
  const nextX = f32Add(f32Add(f32Mul(matrix.a, x), f32Mul(matrix.c, y)), matrix.tx);
  const nextY = f32Add(f32Add(f32Mul(matrix.b, x), f32Mul(matrix.d, y)), matrix.ty);
  if (output !== null) {
    output[0] = nextX;
    output[1] = nextY;
    return output;
  }
  return [nextX, nextY];
}

export function determinantV1(matrix: AffineV1): number {
  return f32Sub(f32Mul(matrix.a, matrix.d), f32Mul(matrix.c, matrix.b));
}

export function localBoneAffineV1(bone: RuntimeBoneV1, output: AffineV1 | null = null): AffineV1 {
  const rx = degreesToRadians(f32Add(bone.rotation, bone.shearX));
  const ry = degreesToRadians(f32Add(bone.rotation, bone.shearY));
  return affine(
    f32Mul(Math.cos(rx), bone.scaleX),
    f32Mul(Math.sin(rx), bone.scaleX),
    f32Mul(-Math.sin(ry), bone.scaleY),
    f32Mul(Math.cos(ry), bone.scaleY),
    bone.x,
    bone.y,
    output,
  );
}

export function rootAffineV1(root: RootTransformV1, output: AffineV1 | null = null): AffineV1 {
  const rotation = degreesToRadians(root.rotationDegrees);
  const cosine = f32(Math.cos(rotation));
  const sine = f32(Math.sin(rotation));
  return affine(
    f32Mul(cosine, root.scaleX),
    f32Mul(sine, root.scaleX),
    f32Mul(-sine, root.scaleY),
    f32Mul(cosine, root.scaleY),
    root.x,
    root.y,
    output,
  );
}

export function regionLocalAffineV1(region: {
  readonly x: number;
  readonly y: number;
  readonly rotation: number;
  readonly scaleX: number;
  readonly scaleY: number;
}, output: AffineV1 | null = null): AffineV1 {
  const rotation = degreesToRadians(region.rotation);
  const cosine = f32(Math.cos(rotation));
  const sine = f32(Math.sin(rotation));
  return affine(
    f32Mul(cosine, region.scaleX),
    f32Mul(sine, region.scaleX),
    f32Mul(-sine, region.scaleY),
    f32Mul(cosine, region.scaleY),
    region.x,
    region.y,
    output,
  );
}

export function childWorldAffineV1(
  parent: AffineV1,
  bone: RuntimeBoneV1,
  mode: BoneTransformModeV1 = bone.transformMode,
  output: AffineV1 | null = null,
): AffineV1 {
  if (mode === "inheritAll") {
    const local = localBoneAffineV1(bone, output === parent ? null : output);
    return multiplyAffineV1(parent, local, output);
  }
  if (mode === "translationOnly") {
    const worldX = f32Add(f32Add(f32Mul(parent.a, bone.x), f32Mul(parent.c, bone.y)), parent.tx);
    const worldY = f32Add(f32Add(f32Mul(parent.b, bone.x), f32Mul(parent.d, bone.y)), parent.ty);
    const rx = degreesToRadians(f32Add(bone.rotation, bone.shearX));
    const ry = degreesToRadians(f32Add(f32Add(bone.rotation, 90), bone.shearY));
    return affine(f32Mul(Math.cos(rx), bone.scaleX), f32Mul(Math.sin(rx), bone.scaleX),
      f32Mul(Math.cos(ry), bone.scaleY), f32Mul(Math.sin(ry), bone.scaleY), worldX, worldY, output);
  }
  const [a, b, c, d] = inheritedBasisV1(parent, mode);
  const local = localBoneAffineV1(bone);
  return affine(a * local.a + c * local.b, b * local.a + d * local.b,
    a * local.c + c * local.d, b * local.c + d * local.d,
    parent.a * bone.x + parent.c * bone.y + parent.tx,
    parent.b * bone.x + parent.d * bone.y + parent.ty, output);
}
