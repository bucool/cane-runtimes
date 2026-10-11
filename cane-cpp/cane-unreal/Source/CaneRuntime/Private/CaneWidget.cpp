#include "CaneWidget.h"
#include "CanePlayer.h"
#include "CaneSurface.h"
#include "CaneSkeletonAsset.h"
#include "CaneSlateRenderer.h"
#include "Engine/Engine.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Rendering/DrawElements.h"
#include "Widgets/SCompoundWidget.h"
#include "Widgets/Images/SImage.h"

class SCaneWidget final : public SCompoundWidget
{
public:
    SLATE_BEGIN_ARGS(SCaneWidget) {} SLATE_ARGUMENT(TWeakObjectPtr<UCaneWidget>, Owner) SLATE_END_ARGS()
    void Construct(const FArguments& Args)
    {
        Owner = Args._Owner;
        SetCanTick(true);
        Brush.DrawAs = ESlateBrushDrawType::NoDrawType;
        ChildSlot[SAssignNew(Image, SImage).Image(&Brush)];
    }
    void SetSurface(UCaneSurface* Surface)
    {
        Direct.Reset();
        PaintElement.Reset();
        Brush.DrawAs = Surface ? ESlateBrushDrawType::Image : ESlateBrushDrawType::NoDrawType;
        Brush.SetResourceObject(Surface ? Surface->WidgetMaterial.Get() : nullptr);
        Brush.ImageSize = Surface ? FVector2f(Surface->Size) : FVector2f(128, 128);
        Image->Invalidate(EInvalidateWidgetReason::Paint | EInvalidateWidgetReason::Layout);
        Invalidate(EInvalidateWidgetReason::Paint | EInvalidateWidgetReason::Layout);
    }
    void SetFrame(TSharedPtr<FCaneSlateFrame, ESPMode::ThreadSafe> Frame, const UCaneSkeletonAsset& Asset)
    {
        const FVector4f NextBounds(Asset.SurfaceOrigin.X, Asset.SurfaceOrigin.Y, Asset.SurfaceSize.X, Asset.SurfaceSize.Y);
        const bool bChangedSize = !Direct || Bounds != NextBounds;
        Direct = MoveTemp(Frame); Bounds = NextBounds;
        Brush.DrawAs = ESlateBrushDrawType::NoDrawType; Brush.SetResourceObject(nullptr);
        Invalidate(bChangedSize ? EInvalidateWidgetReason::Paint | EInvalidateWidgetReason::Layout : EInvalidateWidgetReason::Paint);
    }
    virtual FVector2D ComputeDesiredSize(float Scale) const override
    { return Direct ? FVector2D(Bounds.Z, Bounds.W) : SCompoundWidget::ComputeDesiredSize(Scale); }
    virtual int32 OnPaint(const FPaintArgs& Args, const FGeometry& Geometry, const FSlateRect& Culling,
        FSlateWindowElementList& Elements, int32 Layer, const FWidgetStyle& Style, bool bEnabled) const override
    {
        if (!Owner.IsValid()) return Layer;
        if (!Direct) return SCompoundWidget::OnPaint(Args, Geometry, Culling, Elements, Layer, Style, bEnabled);
        const auto Size = Geometry.GetLocalSize();
        if (Size.X <= 0 || Size.Y <= 0) return Layer;
        FCaneSlatePaint Paint;
        Paint.TopLeft = FVector2f(Geometry.LocalToAbsolute(FVector2D::ZeroVector));
        Paint.TopRight = FVector2f(Geometry.LocalToAbsolute(FVector2D(Size.X,0)));
        Paint.BottomLeft = FVector2f(Geometry.LocalToAbsolute(FVector2D(0,Size.Y)));
        Paint.Bounds = Bounds; Paint.Tint = Style.GetColorAndOpacityTint();
        Paint.DisplayGamma = GEngine ? GEngine->GetDisplayGamma() : 2.2f;
        Paint.TargetColorSpace = static_cast<uint8>(Owner->TargetColorSpace);
        Paint.BlendColorSpace = Owner->BlendColorSpace;
        Paint.Clips.Add(FSlateClippingZone(Geometry));
        if (const auto Clip = Elements.GetClippingState(); Clip.IsSet())
        {
            if (Clip->ScissorRect.IsSet()) Paint.Clips.Add(*Clip->ScissorRect);
            else Paint.Clips.Append(Clip->StencilQuads);
        }
        PaintElement = MakeCaneSlateElement(Direct, MoveTemp(Paint));
        FSlateDrawElement::MakeCustom(Elements, Layer, PaintElement);
        return Layer;
    }
    void ReleaseOwner() { Owner.Reset(); SetSurface(nullptr); }
    virtual void Tick(const FGeometry& Geometry, double Time, float Delta) override
    {
        SCompoundWidget::Tick(Geometry, Time, Delta);
        if (Owner.IsValid()) Owner->TickPlayer(Delta);
    }
private:
    TWeakObjectPtr<UCaneWidget> Owner;
    FSlateBrush Brush;
    TSharedPtr<SImage> Image;
    TSharedPtr<FCaneSlateFrame, ESPMode::ThreadSafe> Direct;
    mutable TSharedPtr<ICustomSlateElement, ESPMode::ThreadSafe> PaintElement;
    FVector4f Bounds = FVector4f(0,0,128,128);
};
TSharedRef<SWidget> UCaneWidget::RebuildWidget()
{
    SAssignNew(View, SCaneWidget).Owner(this);
    if (DirectFrame && Player && Player->Asset) View->SetFrame(DirectFrame, *Player->Asset);
    else if (Surface) View->SetSurface(Surface);
    return View.ToSharedRef();
}
void UCaneWidget::SynchronizeProperties()
{
    Super::SynchronizeProperties();
    if (Asset) { Reinitialize(); return; }
    ClearPresentation();
}
void UCaneWidget::ClearPresentation()
{
    if (Player) Player->OnFrame.RemoveDynamic(this, &UCaneWidget::UpdateFrame);
    Player = nullptr; Surface = nullptr; DirectFrame.Reset(); LastError.Reset();
    if (View) View->SetSurface(nullptr);
}
void UCaneWidget::ReleaseSlateResources(bool Children)
{
    if (Player) Player->OnFrame.RemoveDynamic(this, &UCaneWidget::UpdateFrame);
    if (View) View->ReleaseOwner();
    View.Reset(); Player = nullptr; Surface = nullptr; DirectFrame.Reset(); Super::ReleaseSlateResources(Children);
}
bool UCaneWidget::SetSkeleton(UCaneSkeletonAsset* InAsset)
{
    if (!InAsset) { Asset = nullptr; ClearPresentation(); return true; }
    const auto Before = Asset; Asset = InAsset;
    if (Reinitialize()) return true;
    Asset = Before; return false;
}
bool UCaneWidget::Reinitialize()
{
    if (!Asset) { ClearPresentation(); return true; }
    auto* P = NewObject<UCanePlayer>(this);
    UCaneSurface* S = nullptr;
    TSharedPtr<FCaneSlateFrame, ESPMode::ThreadSafe> NextFrame;
    if (!P->Initialize(Asset)) { LastError = P->LastError; return false; }
    if (!InitialAnimation.IsEmpty() && !P->Play(InitialAnimation, 0, bLoop, 0)) { LastError = P->LastError; return false; }
    if (Rendering == ECaneWidgetRendering::DirectSlate)
    {
        NextFrame = PrepareCaneSlateFrame(*Asset, *P->GetNativeFrame(), LastError);
        if (!NextFrame) return false;
    }
    else
    {
        S = NewObject<UCaneSurface>(this);
        S->BlendColorSpace = BlendColorSpace;
        if (!S->Initialize(Asset, LastError) || !S->Draw(*P->GetNativeFrame(), LastError)) return false;
    }
    if (Player) Player->OnFrame.RemoveDynamic(this, &UCaneWidget::UpdateFrame);
    Player = P; Surface = S; DirectFrame = NextFrame; Player->OnFrame.AddDynamic(this, &UCaneWidget::UpdateFrame);
    if (View)
    {
        if (DirectFrame) View->SetFrame(DirectFrame, *Asset);
        else View->SetSurface(Surface);
    }
    LastError.Reset(); return true;
}
void UCaneWidget::SetBlendColorSpace(ECaneBlendColorSpace InColorSpace)
{
    if (BlendColorSpace == InColorSpace) return;
    BlendColorSpace = InColorSpace;
    UpdateFrame(); // Invalidate paint without restarting or advancing animation.
}
void UCaneWidget::UpdateFrame()
{
    if (!Player || !Player->GetNativeFrame()) return;
    if (Rendering == ECaneWidgetRendering::DirectSlate)
    {
        auto Next = PrepareCaneSlateFrame(*Player->Asset, *Player->GetNativeFrame(), LastError);
        if (!Next) { bPaused = true; return; }
        DirectFrame = Next;
        if (View) View->SetFrame(DirectFrame, *Player->Asset);
    }
    else if (Surface)
    {
        Surface->BlendColorSpace = BlendColorSpace;
        if (!Surface->Draw(*Player->GetNativeFrame(), LastError)) bPaused = true;
    }
}
void UCaneWidget::TickPlayer(float Delta)
{
    if (Surface && !Surface->CheckRenderStatus(LastError)) { bPaused = true; return; }
    if (DirectFrame && DirectFrame->ResourceStatus.load() < 0)
    {
        LastError = DirectFrame->ResourceStatus.load() == -2 ?
            TEXT("Direct Cane UMG needs a single-sampled SDR Slate UI plane or linear float target; native HDR display planes are not supported.") :
            TEXT("Cane GPU texture is unavailable or has the wrong size. Reinitialize after texture compilation.");
        bPaused = true; return;
    }
    if (IsDesignTime() || !Player || bPaused) return;
    if (!FMath::IsFinite(PlaybackRate) || PlaybackRate < 0) { LastError = TEXT("Playback rate must be finite and nonnegative."); bPaused = true; return; }
    if (PlaybackRate == 0) return;
    if (!Player->Advance(Delta * PlaybackRate)) { LastError = Player->LastError; bPaused = true; }
}
