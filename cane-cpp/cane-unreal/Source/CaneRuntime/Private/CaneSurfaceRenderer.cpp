#include "CaneSurfaceRenderer.h"
#include "CaneRenderStatus.h"
#include "GlobalShader.h"
#include "ShaderParameterStruct.h"
#include "ShaderParameterUtils.h"
#include "RenderGraphBuilder.h"
#include "RenderGraphUtils.h"
#include "PipelineStateCache.h"
#include "RHIStaticStates.h"
#include "CommonRenderResources.h"
#include "TextureResource.h"
#include <cmath>

class FCaneSurfaceVS final : public FGlobalShader
{
    DECLARE_GLOBAL_SHADER(FCaneSurfaceVS);
    SHADER_USE_PARAMETER_STRUCT(FCaneSurfaceVS, FGlobalShader);
    class FSlatePresentation : SHADER_PERMUTATION_BOOL("CANE_SLATE_PRESENTATION");
    using FPermutationDomain = TShaderPermutationDomain<FSlatePresentation>;
    BEGIN_SHADER_PARAMETER_STRUCT(FParameters, )
        SHADER_PARAMETER_RDG_BUFFER_SRV(StructuredBuffer<float4>, Vertices)
        SHADER_PARAMETER(FVector4f, Bounds)
        SHADER_PARAMETER(FMatrix44f, LocalToClip)
        SHADER_PARAMETER(uint32, SceneSpace)
    END_SHADER_PARAMETER_STRUCT()
    static bool ShouldCompilePermutation(const FGlobalShaderPermutationParameters& P) { return IsFeatureLevelSupported(P.Platform, ERHIFeatureLevel::SM5); }
};
class FCaneSurfacePS final : public FGlobalShader
{
    DECLARE_GLOBAL_SHADER(FCaneSurfacePS);
    SHADER_USE_PARAMETER_STRUCT(FCaneSurfacePS, FGlobalShader);
    class FSlatePresentation : SHADER_PERMUTATION_BOOL("CANE_SLATE_PRESENTATION");
    using FPermutationDomain = TShaderPermutationDomain<FSlatePresentation>;
    BEGIN_SHADER_PARAMETER_STRUCT(FParameters, )
        SHADER_PARAMETER_RDG_TEXTURE(Texture2D, SourceTexture)
        SHADER_PARAMETER(FVector4f, Light)
        SHADER_PARAMETER(FVector4f, Dark)
        SHADER_PARAMETER(FIntVector4, Modes)
        SHADER_PARAMETER(FIntVector4, Filtering)
        SHADER_PARAMETER_RDG_TEXTURE(Texture2D, SceneDepth)
        SHADER_PARAMETER(uint32, ReadSceneDepth)
        SHADER_PARAMETER(FVector4f, PresentationTint)
        SHADER_PARAMETER_RDG_BUFFER_SRV(StructuredBuffer<float4>, ClipPlanes)
        SHADER_PARAMETER(uint32, ClipPlaneCount)
    END_SHADER_PARAMETER_STRUCT()
    static bool ShouldCompilePermutation(const FGlobalShaderPermutationParameters& P) { return IsFeatureLevelSupported(P.Platform, ERHIFeatureLevel::SM5); }
};
IMPLEMENT_GLOBAL_SHADER(FCaneSurfaceVS, "/Plugin/CaneUnreal/CaneSurface.usf", "VertexMain", SF_Vertex);
IMPLEMENT_GLOBAL_SHADER(FCaneSurfacePS, "/Plugin/CaneUnreal/CaneSurface.usf", "PixelMain", SF_Pixel);

class FCaneBlendTransferVS final : public FGlobalShader
{
    DECLARE_GLOBAL_SHADER(FCaneBlendTransferVS);
    SHADER_USE_PARAMETER_STRUCT(FCaneBlendTransferVS, FGlobalShader);
    BEGIN_SHADER_PARAMETER_STRUCT(FParameters, ) END_SHADER_PARAMETER_STRUCT()
    static bool ShouldCompilePermutation(const FGlobalShaderPermutationParameters& P) { return IsFeatureLevelSupported(P.Platform, ERHIFeatureLevel::SM5); }
};
class FCaneBlendTransferPS final : public FGlobalShader
{
    DECLARE_GLOBAL_SHADER(FCaneBlendTransferPS);
    SHADER_USE_PARAMETER_STRUCT(FCaneBlendTransferPS, FGlobalShader);
    BEGIN_SHADER_PARAMETER_STRUCT(FParameters, )
        SHADER_PARAMETER_RDG_TEXTURE(Texture2D, InputColor)
        SHADER_PARAMETER(int32, Transfer)
        SHADER_PARAMETER(uint32, OpaqueBackground)
    END_SHADER_PARAMETER_STRUCT()
    static bool ShouldCompilePermutation(const FGlobalShaderPermutationParameters& P) { return IsFeatureLevelSupported(P.Platform, ERHIFeatureLevel::SM5); }
};
IMPLEMENT_GLOBAL_SHADER(FCaneBlendTransferVS, "/Plugin/CaneUnreal/CaneBlendTransfer.usf", "VertexMain", SF_Vertex);
IMPLEMENT_GLOBAL_SHADER(FCaneBlendTransferPS, "/Plugin/CaneUnreal/CaneBlendTransfer.usf", "PixelMain", SF_Pixel);
BEGIN_SHADER_PARAMETER_STRUCT(FCaneBlendTransferPass, )
    SHADER_PARAMETER_STRUCT_INCLUDE(FCaneBlendTransferPS::FParameters, Pixel)
    RENDER_TARGET_BINDING_SLOTS()
END_SHADER_PARAMETER_STRUCT()

BEGIN_SHADER_PARAMETER_STRUCT(FCanePassParameters, )
    SHADER_PARAMETER_STRUCT_INCLUDE(FCaneSurfaceVS::FParameters, Vertex)
    SHADER_PARAMETER_STRUCT_INCLUDE(FCaneSurfacePS::FParameters, Pixel)
    RENDER_TARGET_BINDING_SLOTS()
END_SHADER_PARAMETER_STRUCT()

namespace
{
FRHIBlendState* BlendState(cane::RenderBlendMode Mode)
{
    switch (Mode)
    {
    case cane::RenderBlendMode::add:
        return TStaticBlendState<CW_RGBA, BO_Add, BF_One, BF_One, BO_Add, BF_One, BF_One>::GetRHI();
    case cane::RenderBlendMode::multiply:
        return TStaticBlendState<CW_RGBA, BO_Add, BF_DestColor, BF_InverseSourceAlpha, BO_Add, BF_One, BF_InverseSourceAlpha>::GetRHI();
    case cane::RenderBlendMode::screen:
        return TStaticBlendState<CW_RGBA, BO_Add, BF_One, BF_InverseSourceColor, BO_Add, BF_One, BF_InverseSourceAlpha>::GetRHI();
    default:
        return TStaticBlendState<CW_RGBA, BO_Add, BF_One, BF_InverseSourceAlpha, BO_Add, BF_One, BF_InverseSourceAlpha>::GetRHI();
    }
}
float Linear(uint8 Value) { const float V = Value / 255.0f; return V <= 0.04045f ? V / 12.92f : std::pow((V + 0.055f) / 1.055f, 2.4f); }
}
TSharedPtr<const FCaneGpuFrame, ESPMode::ThreadSafe> FCaneSurfaceRenderer::Resolve(
    std::vector<cane_unreal::Draw> Draws, const TArray<FTextureResource*>& Resources)
{
    check(IsInRenderingThread());
    if (Draws.size() != static_cast<size_t>(Resources.Num())) return {};
    auto Result = MakeShared<FCaneGpuFrame, ESPMode::ThreadSafe>();
    for (int32 Index = 0; Index < Resources.Num(); ++Index)
    {
        const auto* Resource = Resources[Index];
        if (!Resource || !Resource->TextureRHI.IsValid()) return {};
        const auto Extent = Resource->TextureRHI->GetDesc().Extent;
        if (Extent.X != static_cast<int32>(Draws[Index].width) || Extent.Y != static_cast<int32>(Draws[Index].height)) return {};
        Result->Textures.Add(Resource->TextureRHI);
    }
    Result->Draws = std::move(Draws);
    return Result;
}
void FCaneSurfaceRenderer::AddDrawPasses(FRDGBuilder& Graph, FRDGTextureRef Output, FIntRect Viewport,
    const FCaneGpuFrame& Frame, FVector4f Bounds, const FMatrix44f& LocalToClip, ECaneBlendColorSpace BlendColorSpace, FRDGTextureRef SceneDepth,
    const FCaneSlateDrawOptions* Slate)
{
        const auto& Draws = Frame.Draws;
        const bool bGamma = BlendColorSpace == ECaneBlendColorSpace::Gamma;
        const auto TintChannel = [bGamma](uint8 Value) { return bGamma ? Value / 255.0f : Linear(Value); };
        FCaneSurfaceVS::FPermutationDomain VertexPermutation;
        FCaneSurfacePS::FPermutationDomain PixelPermutation;
        VertexPermutation.Set<FCaneSurfaceVS::FSlatePresentation>(Slate != nullptr);
        PixelPermutation.Set<FCaneSurfacePS::FSlatePresentation>(Slate != nullptr);
        const TShaderMapRef<FCaneSurfaceVS> VS(GetGlobalShaderMap(GMaxRHIFeatureLevel), VertexPermutation);
        const TShaderMapRef<FCaneSurfacePS> PS(GetGlobalShaderMap(GMaxRHIFeatureLevel), PixelPermutation);
        const FVector4f EmptyPlane(0, 0, 1, 0);
        const uint32 PlaneCount = Slate ? static_cast<uint32>(Slate->ClipPlanes.Num()) : 0u;
        auto* Planes = CreateStructuredBuffer(Graph, TEXT("CaneWidgetClips"), sizeof(FVector4f), FMath::Max(1u, PlaneCount),
            PlaneCount ? Slate->ClipPlanes.GetData() : &EmptyPlane, FMath::Max(1u, PlaneCount) * sizeof(FVector4f));
        const FIntRect Scissor = Slate ? Slate->Scissor : Viewport;
        const bool bScissor = Slate != nullptr;
        for (size_t Index = 0; Index < Draws.size(); ++Index)
        {
            const auto& Draw = Draws[Index];
            const uint32 Count = static_cast<uint32>(Draw.vertices.size());
            auto* Parameters = Graph.AllocParameters<FCanePassParameters>();
            auto* Buffer = CreateStructuredBuffer(Graph, TEXT("CaneFinalVertices"), sizeof(cane_unreal::Vertex), Count,
                Draw.vertices.data(), Draw.vertices.size() * sizeof(cane_unreal::Vertex));
            Parameters->Vertex.Vertices = Graph.CreateSRV(Buffer); Parameters->Vertex.Bounds = Bounds;
            Parameters->Vertex.LocalToClip = LocalToClip; Parameters->Vertex.SceneSpace = SceneDepth ? 1u : 0u;
            Parameters->Pixel.SourceTexture = Graph.RegisterExternalTexture(CreateRenderTarget(Frame.Textures[static_cast<int32>(Index)], TEXT("CaneRawTexture")));
            Parameters->Pixel.SceneDepth = SceneDepth ? SceneDepth : Parameters->Pixel.SourceTexture;
            Parameters->Pixel.ReadSceneDepth = SceneDepth ? 1u : 0u;
            Parameters->Pixel.PresentationTint = Slate ? Slate->Tint : FVector4f(1, 1, 1, 1);
            Parameters->Pixel.ClipPlanes = Graph.CreateSRV(Planes);
            Parameters->Pixel.ClipPlaneCount = PlaneCount;
            Parameters->Pixel.Light = FVector4f(TintChannel(Draw.tint.light[0]), TintChannel(Draw.tint.light[1]), TintChannel(Draw.tint.light[2]), Draw.tint.alpha);
            Parameters->Pixel.Dark = Draw.tint.dark ? FVector4f(TintChannel((*Draw.tint.dark)[0]), TintChannel((*Draw.tint.dark)[1]), TintChannel((*Draw.tint.dark)[2]), 0) : FVector4f(0, 0, 0, 0);
            Parameters->Pixel.Modes = FIntVector4(Draw.color_space == cane::ColorSpace::srgb, Draw.alpha_mode == cane::AlphaMode::premultiplied, Draw.tint.two_color(), bGamma);
            Parameters->Pixel.Filtering = FIntVector4(static_cast<int32>(Draw.min_filter), static_cast<int32>(Draw.mag_filter), static_cast<int32>(Draw.wrap_u), static_cast<int32>(Draw.wrap_v));
            Parameters->RenderTargets[0] = FRenderTargetBinding(Output, ERenderTargetLoadAction::ELoad);
            const auto Mode = Draw.blend;
            Graph.AddPass(RDG_EVENT_NAME("Cane attachment"), Parameters, ERDGPassFlags::Raster,
                [Parameters, VS, PS, Viewport, Count, Mode, Scissor, bScissor](FRHICommandList& Cmd)
                {
                    FGraphicsPipelineStateInitializer State;
                    Cmd.ApplyCachedRenderTargets(State);
                    State.BlendState = BlendState(Mode);
                    State.RasterizerState = TStaticRasterizerState<FM_Solid, CM_None>::GetRHI();
                    State.DepthStencilState = TStaticDepthStencilState<false, CF_Always>::GetRHI();
                    State.PrimitiveType = PT_TriangleList;
                    State.BoundShaderState.VertexDeclarationRHI = GEmptyVertexDeclaration.VertexDeclarationRHI;
                    State.BoundShaderState.VertexShaderRHI = VS.GetVertexShader(); State.BoundShaderState.PixelShaderRHI = PS.GetPixelShader();
                    SetGraphicsPipelineState(Cmd, State, 0);
                    Cmd.SetViewport(static_cast<float>(Viewport.Min.X), static_cast<float>(Viewport.Min.Y), 0,
                        static_cast<float>(Viewport.Max.X), static_cast<float>(Viewport.Max.Y), 1);
                    Cmd.SetScissorRect(bScissor, Scissor.Min.X, Scissor.Min.Y, Scissor.Max.X, Scissor.Max.Y);
                    SetShaderParameters(Cmd, VS, VS.GetVertexShader(), Parameters->Vertex);
                    SetShaderParameters(Cmd, PS, PS.GetPixelShader(), Parameters->Pixel);
                    Cmd.DrawPrimitive(0, Count / 3, 1);
                    Cmd.SetScissorRect(false, 0, 0, 0, 0);
                });
        }
}
void FCaneSurfaceRenderer::AddColorSpacePass(FRDGBuilder& Graph, FRDGTextureRef Input, FRDGTextureRef Output,
    FIntRect Viewport, ECaneBlendColorSpace From, ECaneBlendColorSpace To, bool bOpaqueBackground)
{
    check(From != To && Input != Output);
    auto* Parameters = Graph.AllocParameters<FCaneBlendTransferPass>();
    Parameters->Pixel.InputColor = Input;
    Parameters->Pixel.Transfer = To == ECaneBlendColorSpace::Gamma ? 1 : -1;
    Parameters->Pixel.OpaqueBackground = bOpaqueBackground ? 1u : 0u;
    Parameters->RenderTargets[0] = FRenderTargetBinding(Output, ERenderTargetLoadAction::ENoAction);
    const TShaderMapRef<FCaneBlendTransferVS> VS(GetGlobalShaderMap(GMaxRHIFeatureLevel));
    const TShaderMapRef<FCaneBlendTransferPS> PS(GetGlobalShaderMap(GMaxRHIFeatureLevel));
    Graph.AddPass(RDG_EVENT_NAME("Cane blend color space"), Parameters, ERDGPassFlags::Raster,
        [Parameters, VS, PS, Viewport](FRHICommandList& Cmd)
        {
            FGraphicsPipelineStateInitializer State; Cmd.ApplyCachedRenderTargets(State);
            State.BlendState = TStaticBlendState<>::GetRHI();
            State.RasterizerState = TStaticRasterizerState<FM_Solid, CM_None>::GetRHI();
            State.DepthStencilState = TStaticDepthStencilState<false, CF_Always>::GetRHI();
            State.PrimitiveType = PT_TriangleList;
            State.BoundShaderState.VertexDeclarationRHI = GEmptyVertexDeclaration.VertexDeclarationRHI;
            State.BoundShaderState.VertexShaderRHI = VS.GetVertexShader(); State.BoundShaderState.PixelShaderRHI = PS.GetPixelShader();
            SetGraphicsPipelineState(Cmd, State, 0);
            Cmd.SetViewport(Viewport.Min.X, Viewport.Min.Y, 0, Viewport.Max.X, Viewport.Max.Y, 1);
            SetShaderParameters(Cmd, PS, PS.GetPixelShader(), Parameters->Pixel);
            Cmd.DrawPrimitive(0, 1, 1);
        });
}
void FCaneSurfaceRenderer::Enqueue(FTextureRenderTargetResource* Target, TArray<FTextureResource*> Resources,
    std::vector<cane_unreal::Draw> Draws, FVector4f Bounds, ECaneBlendColorSpace BlendColorSpace, TSharedPtr<FCaneRenderStatus, ESPMode::ThreadSafe> Status)
{
    // Resolve resources before later UObject release commands. The graph retains
    // RHI references; no UObject access occurs inside the queued work.
    ENQUEUE_RENDER_COMMAND(CaneSurface)([Target, Resources = MoveTemp(Resources), Draws = std::move(Draws), Bounds, BlendColorSpace, Status](FRHICommandListImmediate& RHICmdList) mutable
    {
        if (!Target || !Target->GetRenderTargetTexture()) { Status->ResourceStatus.store(-1); return; }
        const auto Frame = Resolve(std::move(Draws), Resources);
        if (!Frame) { Status->ResourceStatus.store(-1); return; }
        FRDGBuilder Graph(RHICmdList);
        auto* Output = Graph.RegisterExternalTexture(CreateRenderTarget(Target->GetRenderTargetTexture(), TEXT("CaneSurface")));
        const FIntRect Viewport(FIntPoint::ZeroValue, Output->Desc.Extent);
        auto* Working = BlendColorSpace == ECaneBlendColorSpace::Gamma ? Graph.CreateTexture(Output->Desc, TEXT("CaneGammaSurface")) : Output;
        AddClearRenderTargetPass(Graph, Working, FLinearColor::Transparent);
        AddDrawPasses(Graph, Working, Viewport, *Frame, Bounds, FMatrix44f::Identity, BlendColorSpace);
        if (Working != Output) AddColorSpacePass(Graph, Working, Output, Viewport, BlendColorSpace, ECaneBlendColorSpace::Linear);
        Graph.Execute();
        Status->ResourceStatus.store(1);
    });
}
