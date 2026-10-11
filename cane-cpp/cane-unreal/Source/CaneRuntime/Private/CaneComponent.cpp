#include "CaneComponent.h"
#include "CanePlayer.h"
#include "CaneSurface.h"
#include "CaneSkeletonAsset.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Engine/World.h"
#include "CaneSceneView.h"
#include "CaneSurfaceRenderer.h"
#include "CaneTextureBinding.h"
#include "cane/runtime_frame.hpp"
#include <exception>

UCaneComponent::UCaneComponent(const FObjectInitializer& ObjectInitializer) : Super(ObjectInitializer)
{
    PrimaryComponentTick.bCanEverTick = true;
    PrimaryComponentTick.TickGroup = TG_PrePhysics;
    bTickInEditor = true;
    SetCollisionEnabled(ECollisionEnabled::NoCollision);
    SetCastShadow(false);
}
void UCaneComponent::OnRegister()
{
    Super::OnRegister();
    if (!IsTemplate()) { RegisterCaneSceneComponent(this); if (Asset) Reinitialize(); }
}
void UCaneComponent::OnUnregister()
{
    UnregisterCaneSceneComponent(this); ClearPresentation(); Super::OnUnregister();
}
void UCaneComponent::ClearPresentation()
{
    if (Player) Player->OnFrame.RemoveDynamic(this, &UCaneComponent::UpdateFrame);
    Player = nullptr; Surface = nullptr; SceneHandle.Reset();
    DirectBounds = FBox(FVector(-1), FVector(1));
    ClearAllMeshSections(); UpdateBounds(); MarkRenderTransformDirty(); LastError.Reset();
}
bool UCaneComponent::SetSkeleton(UCaneSkeletonAsset* InAsset)
{
    if (!InAsset) { Asset = nullptr; ClearPresentation(); return true; }
    const auto Previous = Asset; Asset = InAsset;
    if (Reinitialize()) return true;
    Asset = Previous; return false;
}
bool UCaneComponent::Reinitialize()
{
    if (!Asset) { ClearPresentation(); return true; }
    if (!FMath::IsFinite(UnitsPerCaneUnit) || UnitsPerCaneUnit <= 0)
    { LastError = TEXT("Cane needs a skeleton asset and a positive unit scale."); return false; }
    auto* CandidatePlayer = NewObject<UCanePlayer>(this);
    UCaneSurface* CandidateSurface = nullptr;
    TSharedPtr<FCaneSceneHandle, ESPMode::ThreadSafe> CandidateHandle;
    FBox CandidateBounds(ForceInit);
    if (!CandidatePlayer->Initialize(Asset)) { LastError = CandidatePlayer->LastError; return false; }
    if (!InitialAnimation.IsEmpty() && !CandidatePlayer->Play(InitialAnimation, 0, bLoop, 0)) { LastError = CandidatePlayer->LastError; return false; }
    if (!IsRunningDedicatedServer())
    {
        if (Rendering == ECaneSceneRendering::DirectScene)
        {
            if (!PrepareDirectFrame(CandidatePlayer, CandidateHandle, CandidateBounds)) return false;
        }
        else
        {
            CandidateSurface = NewObject<UCaneSurface>(this);
            CandidateSurface->BlendColorSpace = BlendColorSpace;
            if (!CandidateSurface->Initialize(Asset, LastError) || !CandidateSurface->Draw(*CandidatePlayer->GetNativeFrame(), LastError)) return false;
        }
    }
    if (Player) Player->OnFrame.RemoveDynamic(this, &UCaneComponent::UpdateFrame);
    Player = CandidatePlayer; Surface = CandidateSurface; SceneHandle = CandidateHandle; DirectBounds = CandidateBounds;
    Player->OnFrame.AddDynamic(this, &UCaneComponent::UpdateFrame);
    if (IsRunningDedicatedServer()) { Surface = nullptr; ClearAllMeshSections(); LastError.Reset(); return true; }
    if (Rendering == ECaneSceneRendering::DirectScene)
    {
        ClearAllMeshSections(); UpdateBounds(); MarkRenderTransformDirty(); LastError.Reset(); return true;
    }
    const auto Min = Surface->Origin * UnitsPerCaneUnit;
    const auto Max = (Surface->Origin + Surface->Size) * UnitsPerCaneUnit;
    // Single host mapping: Core X/Y -> Unreal X/Z. The final surface is premultiplied.
    TArray<FVector> Vertices = {FVector(Min.X, 0, Min.Y), FVector(Max.X, 0, Min.Y), FVector(Max.X, 0, Max.Y), FVector(Min.X, 0, Max.Y)};
    TArray<int32> Indices = {0, 1, 2, 0, 2, 3};
    TArray<FVector> Normals; Normals.Init(FVector(0, -1, 0), 4);
    TArray<FVector2D> UVs = {FVector2D(0, 1), FVector2D(1, 1), FVector2D(1, 0), FVector2D(0, 0)};
    TArray<FLinearColor> Colors; Colors.Init(FLinearColor::White, 4);
    TArray<FProcMeshTangent> Tangents; Tangents.Init(FProcMeshTangent(1, 0, 0), 4);
    ClearAllMeshSections(); CreateMeshSection_LinearColor(0, Vertices, Indices, Normals, UVs, Colors, Tangents, false);
    SetMaterial(0, Surface->SceneMaterial); LastError.Reset(); return true;
}
void UCaneComponent::SetBlendColorSpace(ECaneBlendColorSpace InColorSpace)
{
    if (BlendColorSpace == InColorSpace) return;
    BlendColorSpace = InColorSpace;
    UpdateFrame(); // Reuse the owned publication, including while paused.
}
void UCaneComponent::UpdateFrame()
{
    if (IsRunningDedicatedServer()) return;
    if (Player && Rendering == ECaneSceneRendering::DirectScene)
    {
        FBox NextBounds(ForceInit);
        if (!PrepareDirectFrame(Player, SceneHandle, NextBounds)) { bPaused = true; return; }
        DirectBounds = NextBounds; UpdateBounds(); MarkRenderTransformDirty(); return;
    }
    if (Surface) Surface->BlendColorSpace = BlendColorSpace;
    if (Player && Surface && Player->GetNativeFrame())
        if (!Surface->Draw(*Player->GetNativeFrame(), LastError)) bPaused = true;
}
bool UCaneComponent::PrepareDirectFrame(UCanePlayer* InPlayer, TSharedPtr<FCaneSceneHandle, ESPMode::ThreadSafe>& Handle, FBox& OutBounds)
{
    try
    {
        if (!InPlayer || !InPlayer->Asset || !InPlayer->GetNativeFrame())
        { LastError = TEXT("Initialize the Cane player before presenting a frame."); return false; }
        if (Handle && Handle->ResourceStatus.load() < 0)
        { LastError = TEXT("Cane GPU texture is unavailable or has the wrong size. Reinitialize after texture compilation."); return false; }
        auto Draws = cane_unreal::project(InPlayer->GetNativeFrame()->render_packet());
        auto Resources = BindCaneTextures(*InPlayer->Asset, Draws);
        OutBounds = FBox(ForceInit);
        for (const auto& Draw : Draws) for (const auto& Vertex : Draw.vertices)
            OutBounds += FVector(Vertex.x * UnitsPerCaneUnit, 0, Vertex.y * UnitsPerCaneUnit);
        if (!OutBounds.IsValid) OutBounds = FBox(FVector(-1), FVector(1));
        else OutBounds = OutBounds.ExpandBy(FVector(0, 0.1, 0));
        if (!Handle) Handle = MakeShared<FCaneSceneHandle, ESPMode::ThreadSafe>();
        const auto Target = Handle;
        ENQUEUE_RENDER_COMMAND(CaneSceneFrame)([Target, Draws = std::move(Draws), Resources = MoveTemp(Resources)](FRHICommandListImmediate&) mutable
        {
            const auto Frame = FCaneSurfaceRenderer::Resolve(std::move(Draws), Resources);
            if (Frame) { Target->Frame = Frame; Target->ResourceStatus.store(1); }
            else Target->ResourceStatus.store(-1);
        });
        LastError.Reset(); return true;
    }
    catch (const std::exception& Error) { LastError = UTF8_TO_TCHAR(Error.what()); return false; }
}
FBoxSphereBounds UCaneComponent::CalcBounds(const FTransform& LocalToWorld) const
{
    if (Rendering == ECaneSceneRendering::DirectScene && DirectBounds.IsValid)
        return FBoxSphereBounds(DirectBounds).TransformBy(LocalToWorld);
    return Super::CalcBounds(LocalToWorld);
}
void UCaneComponent::TickComponent(float Delta, ELevelTick TickType, FActorComponentTickFunction* Tick)
{
    Super::TickComponent(Delta, TickType, Tick);
    if (SceneHandle && SceneHandle->ResourceStatus.load() < 0)
    { LastError = TEXT("Cane GPU texture is unavailable or has the wrong size. Reinitialize after texture compilation."); bPaused = true; return; }
    if (Surface && !Surface->CheckRenderStatus(LastError)) { bPaused = true; return; }
    if (!Player || bPaused || (GetWorld() && !GetWorld()->IsGameWorld() && !bPreviewInEditor)) return;
    if (!FMath::IsFinite(PlaybackRate) || PlaybackRate < 0) { LastError = TEXT("Playback rate must be finite and nonnegative."); bPaused = true; return; }
    if (PlaybackRate == 0) return;
    if (!Player->Advance(Delta * PlaybackRate)) { LastError = Player->LastError; bPaused = true; }
}
bool UCaneComponent::GetBoneWorldAxes(const FString& Id, FVector& Origin, FVector& X, FVector& Y)
{
    FCaneBonePose Pose;
    Origin = FVector::ZeroVector; X = FVector::ZeroVector; Y = FVector::ZeroVector;
    if (!Player || !Player->GetBone(Id, Pose) || !Pose.Active) return false;
    const auto& Host = GetComponentTransform();
    const auto Map = [&](FVector2D P) { return FVector(P.X * UnitsPerCaneUnit, 0, P.Y * UnitsPerCaneUnit); };
    Origin = Host.TransformPosition(Map(Pose.Origin)); X = Host.TransformVector(Map(Pose.AxisX)); Y = Host.TransformVector(Map(Pose.AxisY)); return true;
}
#if WITH_EDITOR
void UCaneComponent::PostEditChangeProperty(FPropertyChangedEvent& Event)
{
    Super::PostEditChangeProperty(Event);
    if (!IsRegistered()) return;
    if (Event.GetPropertyName() == GET_MEMBER_NAME_CHECKED(UCaneComponent, BlendColorSpace))
    { UpdateFrame(); return; }
    if (Asset) { Reinitialize(); return; }
    ClearPresentation();
}
#endif
