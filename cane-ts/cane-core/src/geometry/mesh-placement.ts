import type { AffineV1, RuntimeAttachmentV1, RuntimePointV1 } from "../contracts.js";
import { RuntimeErrorV1 } from "../errors.js";
import { f32 } from "../math/f32.js";

const ZERO: RuntimePointV1 = Object.freeze({ x: 0, y: 0 });

/** The displayed mesh owns placement; linked geometry and animated bones do not. */
export function meshPlacementWorldV1(
  attachment: RuntimeAttachmentV1,
  root: AffineV1,
  operation = "apply",
): RuntimePointV1 {
  const p = attachment.type === "mesh" ? attachment.placement : undefined;
  if (p === undefined || (p.x === 0 && p.y === 0)) return ZERO;
  return finitePoint(root.a * p.x + root.c * p.y, root.b * p.x + root.d * p.y, operation);
}

export function placeMeshVerticesV1(vertices: number[], delta: RuntimePointV1, operation = "apply"): void {
  if (delta.x === 0 && delta.y === 0) return;
  for (let i = 0; i < vertices.length; i += 2) {
    const x = f32((vertices[i] as number) + delta.x);
    const y = f32((vertices[i + 1] as number) + delta.y);
    if (!Number.isFinite(x) || !Number.isFinite(y)) placementError(operation);
    vertices[i] = x;
    vertices[i + 1] = y;
  }
}

export function unplaceMeshPointV1(point: RuntimePointV1, delta: RuntimePointV1, operation: string): RuntimePointV1 {
  return delta.x === 0 && delta.y === 0 ? point : finitePoint(point.x - delta.x, point.y - delta.y, operation);
}

export function placeMeshAffineV1(matrix: AffineV1, delta: RuntimePointV1): AffineV1 {
  if (delta.x === 0 && delta.y === 0) return matrix;
  const p = finitePoint(matrix.tx + delta.x, matrix.ty + delta.y, "apply");
  return { ...matrix, tx: p.x, ty: p.y };
}

function finitePoint(x: number, y: number, operation: string): RuntimePointV1 {
  x = f32(x);
  y = f32(y);
  if (!Number.isFinite(x) || !Number.isFinite(y)) placementError(operation);
  return { x, y };
}

function placementError(operation: string): never {
  throw new RuntimeErrorV1("nonFinite", operation, "Mesh placement produces non-finite coordinates.", { field: "placement" });
}
