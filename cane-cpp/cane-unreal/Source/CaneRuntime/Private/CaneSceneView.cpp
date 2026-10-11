#include "CaneSceneView.h"
#include "CaneComponent.h"
#include "CaneSurfaceRenderer.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "SceneViewExtension.h"
#include "SceneManagement.h"
#include "SceneTexturesConfig.h"
#include "PostProcess/PostProcessMaterialInputs.h"
#include "ScreenPass.h"
#include "GlobalShader.h"
#include "ShaderParameterStruct.h"
#include "RenderGraphBuilder.h"
#include "RenderGraphUtils.h"
#include "PipelineStateCache.h"
#include "RHIStaticStates.h"
#include "CommonRenderResources.h"
#include "Math/TranslationMatrix.h"
#include "Math/ScaleMatrix.h"

// Undo pre-exposure before packet blending, then restore it before returning to
// Unreal. Screen/multiply must operate on physical linear RGB, not exposed RGB.
class FCaneExposureVS final : public FGlobalShader
{
    DECLARE_GLOBAL_SHADER(FCaneExposureVS);
    SHADER_USE_PARAMETER_STRUCT(FCaneExposureVS, FGlobalShader);
    BEGIN_SHADER_PARAMETER_STRUCT(FParameters, ) END_SHADER_PARAMETER_STRUCT()
    static bool ShouldCompilePermutation(const FGlobalShaderPermutationParameters& P) { return IsFeatureLevelSupported(P.Platform, ERHIFeatureLevel::SM5); }
};
class FCaneExposurePS final : public FGlobalShader
{
    DECLARE_GLOBAL_SHADER(FCaneExposurePS);
    SHADER_USE_PARAMETER_STRUCT(FCaneExposurePS, FGlobalShader);
    BEGIN_SHADER_PARAMETER_STRUCT(FParameters, )
        SHADER_PARAMETER_RDG_TEXTURE(Texture2D, InputColor)
        SHADER_PARAMETER(FVector4f, InputRect)
        SHADER_PARAMETER(FVector4f, OutputRect)
        SHADER_PARAMETER(float, ExposureScale)
    END_SHADER_PARAMETER_STRUCT()
    static bool ShouldCompilePermutation(const FGlobalShaderPermutationParameters& P) { return IsFeatureLevelSupported(P.Platform, ERHIFeatureLevel::SM5); }
};
IMPLEMENT_GLOBAL_SHADER(FCaneExposureVS, "/Plugin/CaneUnreal/CaneExposure.usf", "VertexMain", SF_Vertex);
IMPLEMENT_GLOBAL_SHADER(FCaneExposurePS, "/Plugin/CaneUnreal/CaneExposure.usf", "PixelMain", SF_Pixel);
BEGIN_SHADER_PARAMETER_STRUCT(FCaneExposurePass, )
    SHADER_PARAMETER_STRUCT_INCLUDE(FCaneExposurePS::FParameters, Pixel)
    RENDER_TARGET_BINDING_SLOTS()
END_SHADER_PARAMETER_STRUCT()

namespace
{
struct FCaneSceneInstance
{
    TSharedPtr<FCaneSceneHandle, ESPMode::ThreadSafe> Handle;
    TSharedPtr<const FCaneGpuFrame, ESPMode::ThreadSafe> Frame;
    FTransform Transform;
    FPrimitiveComponentId PrimitiveId;
    float UnitScale = 1;
    ECaneBlendColorSpace BlendColorSpace = ECaneBlendColorSpace::Gamma;
    int32 SortPriority = 0;
    bool bHiddenInGame = false;
    bool bHiddenInEditor = false;
};
struct FCaneViewBatch
{
    const FSceneInterface* Scene = nullptr;
    uint64 FrameCounter = 0;
    TArray<FCaneSceneInstance> Instances;
};
void AddExposurePass(FRDGBuilder& Graph, FScreenPassTexture Input, FScreenPassRenderTarget Output, float Scale)
{
    auto* Parameters = Graph.AllocParameters<FCaneExposurePass>();
    Parameters->Pixel.InputColor = Input.Texture;
    Parameters->Pixel.InputRect = FVector4f(Input.ViewRect.Min.X, Input.ViewRect.Min.Y, Input.ViewRect.Width(), Input.ViewRect.Height());
    Parameters->Pixel.OutputRect = FVector4f(Output.ViewRect.Min.X, Output.ViewRect.Min.Y, Output.ViewRect.Width(), Output.ViewRect.Height());
    Parameters->Pixel.ExposureScale = Scale;
    Parameters->RenderTargets[0] = FRenderTargetBinding(Output.Texture, Output.LoadAction);
    const TShaderMapRef<FCaneExposureVS> VS(GetGlobalShaderMap(GMaxRHIFeatureLevel));
    const TShaderMapRef<FCaneExposurePS> PS(GetGlobalShaderMap(GMaxRHIFeatureLevel));
    const auto Rect = Output.ViewRect;
    Graph.AddPass(RDG_EVENT_NAME("Cane linear exposure"), Parameters, ERDGPassFlags::Raster,
        [Parameters, VS, PS, Rect](FRHICommandList& Cmd)
        {
            FGraphicsPipelineStateInitializer State; Cmd.ApplyCachedRenderTargets(State);
            State.BlendState = TStaticBlendState<>::GetRHI();
            State.RasterizerState = TStaticRasterizerState<FM_Solid, CM_None>::GetRHI();
            State.DepthStencilState = TStaticDepthStencilState<false, CF_Always>::GetRHI();
            State.PrimitiveType = PT_TriangleList;
            State.BoundShaderState.VertexDeclarationRHI = GEmptyVertexDeclaration.VertexDeclarationRHI;
            State.BoundShaderState.VertexShaderRHI = VS.GetVertexShader(); State.BoundShaderState.PixelShaderRHI = PS.GetPixelShader();
            SetGraphicsPipelineState(Cmd, State, 0);
            Cmd.SetViewport(static_cast<float>(Rect.Min.X), static_cast<float>(Rect.Min.Y), 0,
                static_cast<float>(Rect.Max.X), static_cast<float>(Rect.Max.Y), 1);
            SetShaderParameters(Cmd, PS, PS.GetPixelShader(), Parameters->Pixel);
            Cmd.DrawPrimitive(0, 1, 1);
        });
}

class FCaneViewExtension final : public FSceneViewExtensionBase
{
public:
    explicit FCaneViewExtension(const FAutoRegister& AutoRegister) : FSceneViewExtensionBase(AutoRegister) {}
    TArray<TWeakObjectPtr<UCaneComponent>> Components; // Game thread only.
    // Each world keeps its own snapshot. BeginRenderingViewFamilies may enqueue
    // several worlds before rendering any of them (for example editor and PIE).
    TMap<const FSceneInterface*, TSharedPtr<const FCaneViewBatch, ESPMode::ThreadSafe>> RenderBatches;
    virtual void SetupViewFamily(FSceneViewFamily&) override {}
    virtual void SetupView(FSceneViewFamily&, FSceneView&) override {}
    virtual void BeginRenderViewFamily(FSceneViewFamily& Family) override
    {
        check(IsInGameThread());
        Components.RemoveAll([](const auto& Item) { return !Item.IsValid(); });
        auto Batch = MakeShared<FCaneViewBatch, ESPMode::ThreadSafe>();
        Batch->Scene = Family.Scene; Batch->FrameCounter = Family.FrameCounter;
        for (const auto& Weak : Components)
        {
            auto* Component = Weak.Get();
            if (!Component || !Component->IsRegistered() || !Component->IsVisible() || !Component->GetSceneHandle() ||
                !Component->GetWorld() || Component->GetWorld()->Scene != Family.Scene) continue;
            const auto* Owner = Component->GetOwner();
            FCaneSceneInstance Instance;
            Instance.Handle = Component->GetSceneHandle(); Instance.Transform = Component->GetComponentTransform();
            Instance.UnitScale = Component->UnitsPerCaneUnit; Instance.SortPriority = Component->TranslucencySortPriority;
            Instance.BlendColorSpace = Component->BlendColorSpace;
            Instance.PrimitiveId = Component->GetPrimitiveSceneId();
            Instance.bHiddenInGame = Component->bHiddenInGame || (Owner && Owner->IsHidden());
#if WITH_EDITOR
            Instance.bHiddenInEditor = Owner && Owner->IsHiddenEd();
#endif
            Batch->Instances.Add(MoveTemp(Instance));
        }
        auto Self = StaticCastSharedRef<FCaneViewExtension>(AsShared());
        ENQUEUE_RENDER_COMMAND(CaneViewBatch)([Self, Batch](FRHICommandListImmediate&)
        {
            // Capture the publication and its RHI refs now. A later player update
            // cannot replace the frame while this family's graph is being built.
            for (auto& Instance : Batch->Instances) Instance.Frame = Instance.Handle->Frame;
            for (auto It = Self->RenderBatches.CreateIterator(); It; ++It)
                if (Batch->FrameCounter > It.Value()->FrameCounter + 2) It.RemoveCurrent();
            Self->RenderBatches.Add(Batch->Scene, Batch);
        });
    }
    virtual void SubscribeToPostProcessingPass(EPostProcessingPass Pass, const FSceneView& View,
        FPostProcessingPassDelegateArray& Callbacks, bool) override
    {
        // UE 5.8 calls the overload with InView. The older three-argument overload is not called.
        if (Pass != EPostProcessingPass::BeforeDOF || !View.Family ||
            !View.Family->EngineShowFlags.PostProcessing || View.IsInstancedStereo() || View.bIsMobileMultiViewEnabled ||
            View.GetFeatureLevel() < ERHIFeatureLevel::SM5) return;
        const auto* Batch = RenderBatches.Find(View.Family->Scene);
        if (Batch && !(*Batch)->Instances.IsEmpty())
            Callbacks.Add(FAfterPassCallbackDelegate::CreateRaw(this, &FCaneViewExtension::Render));
    }
    FScreenPassTexture Render(FRDGBuilder& Graph, const FSceneView& View, const FPostProcessMaterialInputs& Inputs)
    {
        if (!View.Family || !Inputs.SceneTextures.SceneTextures)
            return Inputs.ReturnUntouchedSceneColorForPostProcessing(Graph);
        const auto* Found = RenderBatches.Find(View.Family->Scene);
        if (!Found) return Inputs.ReturnUntouchedSceneColorForPostProcessing(Graph);
        const auto Batch = *Found;
        const auto* SceneParameters = Inputs.SceneTextures.SceneTextures->GetContents();
        auto* Depth = SceneParameters->SceneDepthTexture;
        const float PreExposure = View.State ? View.State->GetPreExposure() : 1.0f;
        if (!Depth || !FMath::IsFinite(PreExposure) || PreExposure <= 0)
            return Inputs.ReturnUntouchedSceneColorForPostProcessing(Graph);
        TArray<const FCaneSceneInstance*> Visible;
        for (const auto& Instance : Batch->Instances)
        {
            if (!Instance.Frame || View.HiddenPrimitives.Contains(Instance.PrimitiveId)) continue;
            if (View.bIsGameView ? Instance.bHiddenInGame : Instance.bHiddenInEditor) continue;
            if (View.ShowOnlyPrimitives.IsSet() && !View.ShowOnlyPrimitives->Contains(Instance.PrimitiveId)) continue;
            Visible.Add(&Instance);
        }
        if (Visible.IsEmpty()) return Inputs.ReturnUntouchedSceneColorForPostProcessing(Graph);
        // Stable ordering preserves registration order for equal priorities/distances.
        const FVector Camera = View.ViewMatrices.GetViewOrigin();
        const auto SortDistance = [&View, Camera](const FCaneSceneInstance& Instance)
        {
            const FVector Delta = Instance.Transform.GetLocation() - Camera;
            if (View.TranslucentSortPolicy == ETranslucentSortPolicy::SortAlongAxis)
                return FVector::DotProduct(Delta, View.TranslucentSortAxis);
            if (View.TranslucentSortPolicy == ETranslucentSortPolicy::SortByProjectedZ)
                return FVector::DotProduct(Delta, View.GetViewDirection());
            return Delta.SizeSquared();
        };
        Visible.StableSort([&SortDistance](const FCaneSceneInstance& A, const FCaneSceneInstance& B)
        {
            if (A.SortPriority != B.SortPriority) return A.SortPriority < B.SortPriority;
            return SortDistance(A) > SortDistance(B);
        });
        const auto Input = FScreenPassTexture::CopyFromSlice(Graph, Inputs.GetInput(EPostProcessMaterialInput::SceneColor), {}, 1);
        // This path is intentionally a full-resolution, non-MSAA pass. Refuse a
        // different depth grid instead of reading the wrong viewport's pixels.
        if (Depth->Desc.Extent != Input.Texture->Desc.Extent || Depth->Desc.NumSamples != 1)
            return Inputs.ReturnUntouchedSceneColorForPostProcessing(Graph);
        const auto Desc = FRDGTextureDesc::Create2D(Input.Texture->Desc.Extent, PF_FloatRGBA,
            FClearValueBinding::Transparent, TexCreate_RenderTargetable | TexCreate_ShaderResource);
        auto* Linear = Graph.CreateTexture(Desc, TEXT("CaneSceneLinear"));
        AddExposurePass(Graph, Input, FScreenPassRenderTarget(Linear, Input.ViewRect, ERenderTargetLoadAction::ENoAction), 1.0f / PreExposure);
        auto* Working = Linear;
        ECaneBlendColorSpace WorkingSpace = ECaneBlendColorSpace::Linear;
        const auto SwitchSpace = [&](ECaneBlendColorSpace NextSpace)
        {
            if (WorkingSpace == NextSpace) return;
            auto* Next = Graph.CreateTexture(Desc, TEXT("CaneSceneBlendDomain"));
            FCaneSurfaceRenderer::AddColorSpacePass(Graph, Working, Next, Input.ViewRect, WorkingSpace, NextSpace, true);
            Working = Next; WorkingSpace = NextSpace;
        };
        for (const auto* Instance : Visible)
        {
            SwitchSpace(Instance->BlendColorSpace);
            const FMatrix ToTranslatedWorld = FScaleMatrix(FVector(Instance->UnitScale)) * Instance->Transform.ToMatrixWithScale() *
                FTranslationMatrix(View.ViewMatrices.GetPreViewTranslation());
            const FMatrix44f ToClip(ToTranslatedWorld * View.ViewMatrices.GetTranslatedWorldToClip());
            FCaneSurfaceRenderer::AddDrawPasses(Graph, Working, Input.ViewRect, *Instance->Frame,
                FVector4f(0, 0, 1, 1), ToClip, WorkingSpace, Depth);
        }
        SwitchSpace(ECaneBlendColorSpace::Linear);
        FScreenPassRenderTarget Output = Inputs.OverrideOutput;
        if (!Output.IsValid()) Output = FScreenPassRenderTarget(Graph.CreateTexture(Desc, TEXT("CaneSceneOutput")), Input.ViewRect, ERenderTargetLoadAction::ENoAction);
        AddExposurePass(Graph, FScreenPassTexture(Working, Input.ViewRect), Output, PreExposure);
        return Output;
    }
};
TSharedPtr<FCaneViewExtension, ESPMode::ThreadSafe> Extension;
}
void StartCaneSceneRendering()
{
    check(IsInGameThread());
    if (!Extension && !IsRunningDedicatedServer()) Extension = FSceneViewExtensions::NewExtension<FCaneViewExtension>();
}
void StopCaneSceneRendering()
{
    check(IsInGameThread());
    if (!Extension) return;
    Extension->Components.Reset(); FlushRenderingCommands(); Extension.Reset();
}
void RegisterCaneSceneComponent(UCaneComponent* Component) { check(IsInGameThread()); if (Extension) Extension->Components.AddUnique(Component); }
void UnregisterCaneSceneComponent(UCaneComponent* Component) { check(IsInGameThread()); if (Extension) Extension->Components.Remove(Component); }
