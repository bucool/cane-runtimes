#pragma once
#include "CoreMinimal.h"
#include "Portable/packet_projection.hpp"
class UCaneSkeletonAsset;
class FTextureResource;

// All paths share one imported-asset binding contract. Texture metadata is never
// inferred from materials or filenames.
TArray<FTextureResource*> BindCaneTextures(const UCaneSkeletonAsset& Asset, const std::vector<cane_unreal::Draw>& Draws);
