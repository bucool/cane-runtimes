import { RuntimeErrorV1 } from "@cane-runtime/core";

/** Renderer working space; independent from the texture storage color space. */
export type CanePixiBlendColorSpaceV1 = "gamma" | "linear";

export function validateBlendColorSpaceV1(value: CanePixiBlendColorSpaceV1): CanePixiBlendColorSpaceV1 {
  if (value !== "gamma" && value !== "linear") {
    throw new RuntimeErrorV1("invalidArgument", "pixiBlendColorSpace", "Blend color space must be 'gamma' or 'linear'.", {
      field: "blendColorSpace",
    });
  }
  return value;
}
