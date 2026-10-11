#pragma once
#include "CoreMinimal.h"
#include "Layout/Clipping.h"
#include "Rendering/RenderingCommon.h"
#include "CaneRenderStatus.h"
#include "CaneTypes.h"

class UCaneSkeletonAsset;
struct FCaneGpuFrame;
namespace cane { class RuntimeFrame; }

// Each publication owns a separate handoff. A retained Slate element never reads
// mutable player state or a UObject on the render thread.
struct FCaneSlateFrame : FCaneRenderStatus
{
    TSharedPtr<const FCaneGpuFrame, ESPMode::ThreadSafe> Gpu; // render thread only
};

struct FCaneSlatePaint
{
    FVector2f TopLeft, TopRight, BottomLeft;
    FVector4f Bounds;
    FLinearColor Tint = FLinearColor::White;
    TArray<FSlateClippingZone> Clips;
    float DisplayGamma = 2.2f;
    uint8 TargetColorSpace = 0; // Auto, Linear, SlateSRGB (ECaneWidgetColorSpace)
    ECaneBlendColorSpace BlendColorSpace = ECaneBlendColorSpace::Gamma;
};

TSharedPtr<FCaneSlateFrame, ESPMode::ThreadSafe> PrepareCaneSlateFrame(
    const UCaneSkeletonAsset& Asset, const cane::RuntimeFrame& Frame, FString& Error);
TSharedRef<ICustomSlateElement, ESPMode::ThreadSafe> MakeCaneSlateElement(
    TSharedPtr<FCaneSlateFrame, ESPMode::ThreadSafe> Frame, FCaneSlatePaint Paint);
