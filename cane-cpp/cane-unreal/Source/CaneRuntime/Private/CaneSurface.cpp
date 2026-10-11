#include "CaneSurface.h"
#include "CaneSkeletonAsset.h"
#include "Engine/Texture2D.h"
#include "Engine/TextureRenderTarget2D.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "TextureResource.h"
#include "CaneSurfaceRenderer.h"
#include "CaneTextureBinding.h"
#include "CaneRenderStatus.h"
#include "cane/runtime_frame.hpp"
#include "Portable/packet_projection.hpp"
#include <stdexcept>

bool UCaneSurface::Initialize(UCaneSkeletonAsset* InAsset, FString& Error)
{
    check(IsInGameThread()); Error.Reset();
    try
    {
        if (!InAsset || !InAsset->SceneMaterial || !InAsset->WidgetMaterial)
            throw std::invalid_argument("Cane asset has no display materials. Reimport it in the editor.");
        cane_unreal::validate_surface({static_cast<float>(InAsset->SurfaceOrigin.X), static_cast<float>(InAsset->SurfaceOrigin.Y),
            static_cast<float>(InAsset->SurfaceSize.X), static_cast<float>(InAsset->SurfaceSize.Y)});
        if (InAsset->Resolution < 32 || InAsset->Resolution > 4096) throw std::invalid_argument("Cane surface resolution must be between 32 and 4096.");
        Origin = InAsset->SurfaceOrigin; Size = InAsset->SurfaceSize;
        const double Density = InAsset->Resolution / FMath::Max(Size.X, Size.Y);
        auto* Target = NewObject<UTextureRenderTarget2D>(this);
        Target->ClearColor = FLinearColor::Transparent; Target->RenderTargetFormat = RTF_RGBA16f;
        Target->bAutoGenerateMips = false;
        Target->InitCustomFormat(FMath::Max(1, FMath::RoundToInt(Size.X * Density)), FMath::Max(1, FMath::RoundToInt(Size.Y * Density)), PF_FloatRGBA, true);
        Target->UpdateResourceImmediate(true);
        auto* Scene = UMaterialInstanceDynamic::Create(InAsset->SceneMaterial, this);
        auto* Widget = UMaterialInstanceDynamic::Create(InAsset->WidgetMaterial, this);
        Scene->SetTextureParameterValue(TEXT("CaneSurface"), Target); Widget->SetTextureParameterValue(TEXT("CaneSurface"), Target);
        Asset = InAsset; Texture = Target; SceneMaterial = Scene; WidgetMaterial = Widget;
        RenderStatus = MakeShared<FCaneRenderStatus, ESPMode::ThreadSafe>(); return true;
    }
    catch (const std::exception& Exception) { Error = UTF8_TO_TCHAR(Exception.what()); return false; }
}
bool UCaneSurface::Draw(const cane::RuntimeFrame& Frame, FString& Error)
{
    check(IsInGameThread()); Error.Reset();
    try
    {
        if (!Asset || !Texture) throw std::invalid_argument("Initialize the Cane surface first.");
        if (!CheckRenderStatus(Error)) return false;
        auto Draws = cane_unreal::project(Frame.render_packet());
        auto Resources = BindCaneTextures(*Asset, Draws);
        FCaneSurfaceRenderer::Enqueue(Texture->GameThread_GetRenderTargetResource(), MoveTemp(Resources), std::move(Draws),
            FVector4f(static_cast<float>(Origin.X), static_cast<float>(Origin.Y), static_cast<float>(Size.X), static_cast<float>(Size.Y)), BlendColorSpace, RenderStatus);
        return true;
    }
    catch (const std::exception& Exception) { Error = UTF8_TO_TCHAR(Exception.what()); return false; }
}
bool UCaneSurface::CheckRenderStatus(FString& Error) const
{
    check(IsInGameThread());
    if (RenderStatus && RenderStatus->ResourceStatus.load() < 0)
    {
        Error = TEXT("Cane GPU texture is unavailable or has the wrong size. Reinitialize after texture compilation.");
        return false;
    }
    return true;
}
