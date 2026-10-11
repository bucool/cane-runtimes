#pragma once
#include "CoreMinimal.h"
#include "CaneTypes.h"
#include "RHIResources.h"
#include "RenderGraphFwd.h"
#include "Portable/packet_projection.hpp"
class FTextureResource;
class FTextureRenderTargetResource;
struct FCaneRenderStatus;
struct FCaneGpuFrame
{
    std::vector<cane_unreal::Draw> Draws;
    TArray<FTextureRHIRef> Textures;
};
struct FCaneSlateDrawOptions
{
    // UI presentation only. Core's final geometry and attachment tint stay owned
    // by the packet; these are inherited widget opacity and Slate clip planes.
    FVector4f Tint = FVector4f(1, 1, 1, 1);
    TArray<FVector4f> ClipPlanes;
    FIntRect Scissor;
};
class FCaneSurfaceRenderer
{
public:
    // Render-thread resources only. No UObject is retained or read by a GPU pass.
    static TSharedPtr<const FCaneGpuFrame, ESPMode::ThreadSafe> Resolve(
        std::vector<cane_unreal::Draw> Draws, const TArray<FTextureResource*>& Resources);
    static void AddDrawPasses(FRDGBuilder& Graph, FRDGTextureRef Output, FIntRect Viewport,
        const FCaneGpuFrame& Frame, FVector4f Bounds, const FMatrix44f& LocalToClip,
        ECaneBlendColorSpace BlendColorSpace, FRDGTextureRef SceneDepth = nullptr, const FCaneSlateDrawOptions* Slate = nullptr);
    static void AddColorSpacePass(FRDGBuilder& Graph, FRDGTextureRef Input, FRDGTextureRef Output,
        FIntRect Viewport, ECaneBlendColorSpace From, ECaneBlendColorSpace To, bool bOpaqueBackground = false);
    static void Enqueue(FTextureRenderTargetResource* Target, TArray<FTextureResource*> Resources,
        std::vector<cane_unreal::Draw> Draws, FVector4f Bounds, ECaneBlendColorSpace BlendColorSpace,
        TSharedPtr<FCaneRenderStatus, ESPMode::ThreadSafe> Status);
};
