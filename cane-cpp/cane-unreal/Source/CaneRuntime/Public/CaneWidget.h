#pragma once
#include "CoreMinimal.h"
#include "Components/Widget.h"
#include "CaneTypes.h"
#include "CaneWidget.generated.h"
class UCaneSkeletonAsset;
class UCanePlayer;
class UCaneSurface;
class SCaneWidget;
struct FCaneSlateFrame;

UENUM(BlueprintType)
enum class ECaneWidgetRendering : uint8
{
    DirectSlate,
    IsolatedSurface
};
UENUM(BlueprintType)
enum class ECaneWidgetColorSpace : uint8
{
    Auto,
    Linear,
    SlateSRGB
};

UCLASS(meta=(DisplayName="Cane Animation"))
class CANERUNTIME_API UCaneWidget : public UWidget
{
    GENERATED_BODY()
public:
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category="Cane") TObjectPtr<UCaneSkeletonAsset> Asset;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane|Display") ECaneWidgetRendering Rendering = ECaneWidgetRendering::DirectSlate;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, BlueprintSetter=SetBlendColorSpace, Category="Cane|Display") ECaneBlendColorSpace BlendColorSpace = ECaneBlendColorSpace::Gamma;
    // Auto uses gamma-encoded 8-bit Slate/retainer targets and linear float targets.
    // Override when an application creates a nonstandard FWidgetRenderer target.
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane|Display") ECaneWidgetColorSpace TargetColorSpace = ECaneWidgetColorSpace::Auto;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane") FString InitialAnimation;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane") bool bLoop = true;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane") bool bPaused = false;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane", meta=(ClampMin="0")) float PlaybackRate = 1;
    UPROPERTY(BlueprintReadOnly, Transient, Category="Cane") TObjectPtr<UCanePlayer> Player;
    UPROPERTY(BlueprintReadOnly, Transient, Category="Cane") TObjectPtr<UCaneSurface> Surface;
    UPROPERTY(BlueprintReadOnly, Transient, Category="Cane") FString LastError;
    UFUNCTION(BlueprintCallable, Category="Cane") bool SetSkeleton(UCaneSkeletonAsset* InAsset);
    UFUNCTION(BlueprintCallable, Category="Cane") bool Reinitialize();
    UFUNCTION(BlueprintSetter, Category="Cane|Display") void SetBlendColorSpace(ECaneBlendColorSpace InColorSpace);
    virtual void SynchronizeProperties() override;
    virtual void ReleaseSlateResources(bool ReleaseChildren) override;
    void TickPlayer(float Delta);
protected:
    virtual TSharedRef<SWidget> RebuildWidget() override;
private:
    TSharedPtr<SCaneWidget> View;
    TSharedPtr<FCaneSlateFrame, ESPMode::ThreadSafe> DirectFrame;
    void ClearPresentation();
    UFUNCTION() void UpdateFrame();
};
