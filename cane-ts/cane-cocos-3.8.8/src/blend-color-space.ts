/** RGB arithmetic domain. Alpha is linear coverage in both modes. */
export enum CaneCocosBlendColorSpaceV1 {
  Gamma = 0,
  Linear = 1,
}

/** Direct follows native UI ordering. IsolatedSurface composes one packet layer. */
export enum CaneCocosCompositionModeV1 {
  Direct = 0,
  IsolatedSurface = 1,
}

export function requireCaneCocosColorProfileV1(
  colorSpace: CaneCocosBlendColorSpaceV1,
  composition: CaneCocosCompositionModeV1,
  slotObjects = 0,
): void {
  if (colorSpace !== CaneCocosBlendColorSpaceV1.Gamma
    && colorSpace !== CaneCocosBlendColorSpaceV1.Linear) throw new Error("Unknown Cane blend color space.");
  if (composition !== CaneCocosCompositionModeV1.Direct
    && composition !== CaneCocosCompositionModeV1.IsolatedSurface) throw new Error("Unknown Cane composition mode.");
  if (composition === CaneCocosCompositionModeV1.Direct
    && colorSpace === CaneCocosBlendColorSpaceV1.Linear) {
    throw new Error("Cocos Direct uses the Canvas Gamma target. Choose IsolatedSurface explicitly before selecting Linear.");
  }
  if (composition === CaneCocosCompositionModeV1.IsolatedSurface && slotObjects > 0) {
    throw new Error("Cocos IsolatedSurface cannot interleave native Slot nodes inside its packet. Use Direct Gamma for Slot-node ordering.");
  }
}
