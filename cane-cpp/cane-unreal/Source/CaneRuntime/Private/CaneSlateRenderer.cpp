#include "CaneSlateRenderer.h"
#include "CaneSurfaceRenderer.h"
#include "CaneSkeletonAsset.h"
#include "CaneTextureBinding.h"
#include "cane/runtime_frame.hpp"
#include "GlobalShader.h"
#include "ShaderParameterStruct.h"
#include "RenderGraphBuilder.h"
#include "RenderGraphUtils.h"
#include "PipelineStateCache.h"
#include "RHIStaticStates.h"
#include "CommonRenderResources.h"
#include "RenderDeferredCleanup.h"
#include <exception>

class FCaneSlateTransferVS final : public FGlobalShader
{
    DECLARE_GLOBAL_SHADER(FCaneSlateTransferVS);
    SHADER_USE_PARAMETER_STRUCT(FCaneSlateTransferVS, FGlobalShader);
    BEGIN_SHADER_PARAMETER_STRUCT(FParameters, ) END_SHADER_PARAMETER_STRUCT()
    static bool ShouldCompilePermutation(const FGlobalShaderPermutationParameters& P) { return IsFeatureLevelSupported(P.Platform, ERHIFeatureLevel::SM5); }
};
class FCaneSlateTransferPS final : public FGlobalShader
{
    DECLARE_GLOBAL_SHADER(FCaneSlateTransferPS);
    SHADER_USE_PARAMETER_STRUCT(FCaneSlateTransferPS, FGlobalShader);
    BEGIN_SHADER_PARAMETER_STRUCT(FParameters, )
        SHADER_PARAMETER_RDG_TEXTURE(Texture2D, InputColor)
        SHADER_PARAMETER_RDG_BUFFER_SRV(StructuredBuffer<float4>, ClipPlanes)
        SHADER_PARAMETER(uint32, ClipPlaneCount)
        SHADER_PARAMETER(int32, Transfer)
        SHADER_PARAMETER(float, GammaRatio)
        SHADER_PARAMETER(uint32, DecodeHardwareSRGB)
        SHADER_PARAMETER(uint32, TargetEncoded)
        SHADER_PARAMETER(uint32, BlendGamma)
    END_SHADER_PARAMETER_STRUCT()
    static bool ShouldCompilePermutation(const FGlobalShaderPermutationParameters& P) { return IsFeatureLevelSupported(P.Platform, ERHIFeatureLevel::SM5); }
};
IMPLEMENT_GLOBAL_SHADER(FCaneSlateTransferVS, "/Plugin/CaneUnreal/CaneSlateTransfer.usf", "VertexMain", SF_Vertex);
IMPLEMENT_GLOBAL_SHADER(FCaneSlateTransferPS, "/Plugin/CaneUnreal/CaneSlateTransfer.usf", "PixelMain", SF_Pixel);
BEGIN_SHADER_PARAMETER_STRUCT(FCaneSlateTransferPass, )
    SHADER_PARAMETER_STRUCT_INCLUDE(FCaneSlateTransferPS::FParameters, Pixel)
    RENDER_TARGET_BINDING_SLOTS()
END_SHADER_PARAMETER_STRUCT()

namespace
{
void TransferColor(FRDGBuilder& Graph, FRDGTextureRef Input, FRDGTextureRef Output,
    const FCaneSlateDrawOptions& Options, int32 Direction, float GammaRatio, bool bClip,
    bool bTargetEncoded, ECaneBlendColorSpace BlendColorSpace, bool bDecodeHardwareSRGB = false)
{
    auto* Parameters = Graph.AllocParameters<FCaneSlateTransferPass>();
    auto* Planes = CreateStructuredBuffer(Graph, TEXT("CaneSlateTransferClips"), sizeof(FVector4f), Options.ClipPlanes.Num(),
        Options.ClipPlanes.GetData(), Options.ClipPlanes.Num() * sizeof(FVector4f));
    Parameters->Pixel.InputColor = Input;
    Parameters->Pixel.ClipPlanes = Graph.CreateSRV(Planes);
    Parameters->Pixel.ClipPlaneCount = bClip ? Options.ClipPlanes.Num() : 0;
    Parameters->Pixel.Transfer = Direction;
    Parameters->Pixel.GammaRatio = GammaRatio;
    Parameters->Pixel.DecodeHardwareSRGB = bDecodeHardwareSRGB ? 1u : 0u;
    Parameters->Pixel.TargetEncoded = bTargetEncoded ? 1u : 0u;
    Parameters->Pixel.BlendGamma = BlendColorSpace == ECaneBlendColorSpace::Gamma ? 1u : 0u;
    Parameters->RenderTargets[0] = FRenderTargetBinding(Output, bClip ? ERenderTargetLoadAction::ELoad : ERenderTargetLoadAction::ENoAction);
    const TShaderMapRef<FCaneSlateTransferVS> VS(GetGlobalShaderMap(GMaxRHIFeatureLevel));
    const TShaderMapRef<FCaneSlateTransferPS> PS(GetGlobalShaderMap(GMaxRHIFeatureLevel));
    const FIntPoint Extent = Output->Desc.Extent;
    const auto Rect = Options.Scissor;
    Graph.AddPass(RDG_EVENT_NAME("Cane Slate color transfer"), Parameters, ERDGPassFlags::Raster,
        [Parameters, VS, PS, Extent, Rect](FRHICommandList& Cmd)
        {
            FGraphicsPipelineStateInitializer State; Cmd.ApplyCachedRenderTargets(State);
            State.BlendState = TStaticBlendState<>::GetRHI();
            State.RasterizerState = TStaticRasterizerState<FM_Solid, CM_None>::GetRHI();
            State.DepthStencilState = TStaticDepthStencilState<false, CF_Always>::GetRHI();
            State.PrimitiveType = PT_TriangleList;
            State.BoundShaderState.VertexDeclarationRHI = GEmptyVertexDeclaration.VertexDeclarationRHI;
            State.BoundShaderState.VertexShaderRHI = VS.GetVertexShader(); State.BoundShaderState.PixelShaderRHI = PS.GetPixelShader();
            SetGraphicsPipelineState(Cmd, State, 0);
            Cmd.SetViewport(0, 0, 0, Extent.X, Extent.Y, 1);
            Cmd.SetScissorRect(true, Rect.Min.X, Rect.Min.Y, Rect.Max.X, Rect.Max.Y);
            SetShaderParameters(Cmd, PS, PS.GetPixelShader(), Parameters->Pixel);
            Cmd.DrawPrimitive(0, 1, 1);
            Cmd.SetScissorRect(false, 0, 0, 0, 0);
        });
}

class FCaneSlateElement final : public ICustomSlateElement, public FDeferredCleanupInterface
{
public:
    FCaneSlateElement(TSharedPtr<FCaneSlateFrame, ESPMode::ThreadSafe> InFrame, FCaneSlatePaint InPaint)
        : Frame(MoveTemp(InFrame)), Paint(MoveTemp(InPaint)) {}

    void Draw_RenderThread(FRDGBuilder& Graph, const FDrawPassInputs& Inputs) override
    {
        check(IsInRenderingThread());
        if (!Frame || !Frame->Gpu || Frame->Gpu->Draws.empty() || !Inputs.OutputTexture || Paint.Tint.A <= 0) return;
        auto* Output = Inputs.OutputTexture;
        // The normal Slate SDR UI plane also works on HDR desktops. A native HDR
        // display plane has additional gamut/luminance semantics and is explicit.
        if (Inputs.bOutputIsHDRDisplay || Output->Desc.NumSamples != 1)
        { Frame->ResourceStatus.store(-2); return; }
        const auto Extent = Output->Desc.Extent;
        const auto PixelPoint = [&](FVector2f P)
        {
            // Slate's ElementsMatrix already includes the window/retainer offset.
            // Adding ElementsOffset here would translate offscreen widgets twice.
            const auto Clip = Inputs.ElementsMatrix.TransformFVector4(FVector4f(P.X, P.Y, 0, 1));
            return FVector2f((Clip.X / Clip.W * .5f + .5f) * Extent.X, (.5f - Clip.Y / Clip.W * .5f) * Extent.Y);
        };
        FCaneSlateDrawOptions Options;
        Options.Tint = FVector4f(Paint.Tint.R, Paint.Tint.G, Paint.Tint.B, Paint.Tint.A);
        const FVector2f A = PixelPoint(Paint.TopLeft), B = PixelPoint(Paint.TopRight), C = PixelPoint(Paint.BottomLeft);
        const FVector2f D = B + C - A;
        FVector2f Min = A, Max = A;
        for (const auto P : {B, C, D})
        { Min.X = FMath::Min(Min.X,P.X); Min.Y = FMath::Min(Min.Y,P.Y); Max.X = FMath::Max(Max.X,P.X); Max.Y = FMath::Max(Max.Y,P.Y); }
        Options.Scissor = FIntRect(FMath::Clamp(FMath::FloorToInt(Min.X),0,Extent.X), FMath::Clamp(FMath::FloorToInt(Min.Y),0,Extent.Y),
            FMath::Clamp(FMath::CeilToInt(Max.X),0,Extent.X), FMath::Clamp(FMath::CeilToInt(Max.Y),0,Extent.Y));
        if (Options.Scissor.IsEmpty()) return;
        for (const auto& Zone : Paint.Clips)
        {
            const FVector2f Points[] = {PixelPoint(Zone.TopLeft), PixelPoint(Zone.TopRight), PixelPoint(Zone.BottomRight), PixelPoint(Zone.BottomLeft)};
            const auto First = Points[1] - Points[0], Second = Points[2] - Points[1];
            const float Area = First.X * Second.Y - First.Y * Second.X;
            if (FMath::Abs(Area) < 0.00001f) return;
            const float Sign = Area > 0 ? 1.0f : -1.0f;
            for (int32 I = 0; I < 4; ++I)
            {
                const auto Edge = Points[(I + 1) % 4] - Points[I];
                const FVector2f Normal(-Edge.Y * Sign, Edge.X * Sign);
                Options.ClipPlanes.Add(FVector4f(Normal.X, Normal.Y, -FVector2f::DotProduct(Normal, Points[I]), 0));
            }
        }
        if (Options.ClipPlanes.IsEmpty()) return;

        const FVector2f AxisX = (Paint.TopRight - Paint.TopLeft) / Paint.Bounds.Z;
        const FVector2f AxisY = (Paint.TopLeft - Paint.BottomLeft) / Paint.Bounds.W;
        const FVector2f Origin = Paint.BottomLeft - AxisX * Paint.Bounds.X - AxisY * Paint.Bounds.Y;
        FMatrix44f ToSlate = FMatrix44f::Identity;
        ToSlate.M[0][0] = AxisX.X; ToSlate.M[0][1] = AxisX.Y;
        ToSlate.M[1][0] = AxisY.X; ToSlate.M[1][1] = AxisY.Y;
        ToSlate.M[3][0] = Origin.X; ToSlate.M[3][1] = Origin.Y;
        const FMatrix44f ToClip = ToSlate * Inputs.ElementsMatrix;

        // Slate's custom-draw inputs do not expose bAllowGammaCorrection. Auto
        // follows ordinary 8-bit Slate/retainer targets and linear float targets;
        // custom FWidgetRenderer callers can explicitly select either contract.
        const bool bFloat = Output->Desc.Format == PF_FloatRGBA || Output->Desc.Format == PF_A32B32G32R32F;
        const bool bEncoded = Paint.TargetColorSpace == 2 || (Paint.TargetColorSpace == 0 && !bFloat);
        const float Gamma = FMath::IsFinite(Paint.DisplayGamma) && Paint.DisplayGamma > 0 ? 2.2f / Paint.DisplayGamma : 1.0f;
        auto* Background = Graph.CreateTexture(FRDGTextureDesc::Create2D(Extent, Output->Desc.Format,
            FClearValueBinding::None, TexCreate_ShaderResource), TEXT("CaneSlateBackground"));
        FRHICopyTextureInfo Copy;
        Copy.SourcePosition = FIntVector(Options.Scissor.Min.X, Options.Scissor.Min.Y, 0);
        Copy.DestPosition = Copy.SourcePosition;
        Copy.Size = FIntVector(Options.Scissor.Width(), Options.Scissor.Height(), 1);
        AddCopyTexturePass(Graph, Output, Background, Copy);
        auto* Linear = Graph.CreateTexture(FRDGTextureDesc::Create2D(Extent, PF_FloatRGBA,
            FClearValueBinding::None, TexCreate_RenderTargetable | TexCreate_ShaderResource), TEXT("CaneSlateLinear"));
        // Some Retainer targets are SRGB render targets even though Slate writes
        // gamma-encoded UI values into them. The raw copy contains both encodings:
        // undo hardware SRGB first, then Slate gamma. Writes mirror those stages.
        const bool bHardwareSRGB = EnumHasAnyFlags(Output->Desc.Flags, TexCreate_SRGB);
        TransferColor(Graph, Background, Linear, Options, -1, Gamma, false, bEncoded, Paint.BlendColorSpace, bHardwareSRGB);
        FCaneSurfaceRenderer::AddDrawPasses(Graph, Linear, FIntRect(FIntPoint::ZeroValue, Extent), *Frame->Gpu,
            Paint.Bounds, ToClip, Paint.BlendColorSpace, nullptr, &Options);
        TransferColor(Graph, Linear, Output, Options, 1, Gamma, true, bEncoded, Paint.BlendColorSpace);
        Frame->ResourceStatus.store(1);
    }
private:
    TSharedPtr<FCaneSlateFrame, ESPMode::ThreadSafe> Frame;
    FCaneSlatePaint Paint;
};
}

TSharedPtr<FCaneSlateFrame, ESPMode::ThreadSafe> PrepareCaneSlateFrame(
    const UCaneSkeletonAsset& Asset, const cane::RuntimeFrame& Frame, FString& Error)
{
    check(IsInGameThread()); Error.Reset();
    try
    {
        cane_unreal::validate_surface({static_cast<float>(Asset.SurfaceOrigin.X), static_cast<float>(Asset.SurfaceOrigin.Y),
            static_cast<float>(Asset.SurfaceSize.X), static_cast<float>(Asset.SurfaceSize.Y)});
        auto Draws = cane_unreal::project(Frame.render_packet());
        auto Resources = BindCaneTextures(Asset, Draws);
        auto Result = MakeShared<FCaneSlateFrame, ESPMode::ThreadSafe>();
        ENQUEUE_RENDER_COMMAND(CaneSlatePublication)([Result, Draws = std::move(Draws), Resources = MoveTemp(Resources)](FRHICommandListImmediate&) mutable
        {
            Result->Gpu = FCaneSurfaceRenderer::Resolve(std::move(Draws), Resources);
            Result->ResourceStatus.store(Result->Gpu ? 1 : -1);
        });
        return Result;
    }
    catch (const std::exception& Exception) { Error = UTF8_TO_TCHAR(Exception.what()); return {}; }
}

TSharedRef<ICustomSlateElement, ESPMode::ThreadSafe> MakeCaneSlateElement(
    TSharedPtr<FCaneSlateFrame, ESPMode::ThreadSafe> Frame, FCaneSlatePaint Paint)
{
    // Slate caches a weak pointer and batches a raw pointer. Keep the newest
    // paint alive in its widget, and retire replaced paints only after Unreal
    // has drained the render queue, including deferred retainer draws.
    return MakeShareable(new FCaneSlateElement(MoveTemp(Frame), MoveTemp(Paint)),
        [](FCaneSlateElement* Element) { BeginCleanup(Element); });
}
