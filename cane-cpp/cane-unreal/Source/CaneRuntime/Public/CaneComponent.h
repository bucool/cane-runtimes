#pragma once
#include "CoreMinimal.h"
#include "ProceduralMeshComponent.h"
#include "CaneTypes.h"
#include "CaneComponent.generated.h"

class UCaneSkeletonAsset;
class UCanePlayer;
class UCaneSurface;
struct FCaneSceneHandle;

UENUM(BlueprintType)
enum class ECaneSceneRendering : uint8 { DirectScene, IsolatedSurface };

UCLASS(ClassGroup=(Cane), meta=(BlueprintSpawnableComponent))
class CANERUNTIME_API UCaneComponent : public UProceduralMeshComponent
{
    GENERATED_BODY()
public:
    UCaneComponent(const FObjectInitializer& ObjectInitializer = FObjectInitializer::Get());
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category="Cane") TObjectPtr<UCaneSkeletonAsset> Asset;
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category="Cane|Display") ECaneSceneRendering Rendering = ECaneSceneRendering::DirectScene;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, BlueprintSetter=SetBlendColorSpace, Category="Cane|Display") ECaneBlendColorSpace BlendColorSpace = ECaneBlendColorSpace::Gamma;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane") FString InitialAnimation;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane") bool bLoop = true;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane") bool bPaused = false;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane", meta=(ClampMin="0")) float PlaybackRate = 1;
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category="Cane", meta=(ClampMin="0.0001")) float UnitsPerCaneUnit = 1;
    UPROPERTY(EditAnywhere, Category="Cane|Editor") bool bPreviewInEditor = false;
    UPROPERTY(BlueprintReadOnly, Transient, Category="Cane") TObjectPtr<UCanePlayer> Player;
    UPROPERTY(BlueprintReadOnly, Transient, Category="Cane") TObjectPtr<UCaneSurface> Surface;
    UPROPERTY(BlueprintReadOnly, Transient, Category="Cane") FString LastError;
    UFUNCTION(BlueprintCallable, Category="Cane") bool SetSkeleton(UCaneSkeletonAsset* InAsset);
    UFUNCTION(BlueprintCallable, Category="Cane") bool Reinitialize();
    UFUNCTION(BlueprintSetter, Category="Cane|Display") void SetBlendColorSpace(ECaneBlendColorSpace InColorSpace);
    UFUNCTION(BlueprintCallable, Category="Cane") bool GetBoneWorldAxes(const FString& BoneId, FVector& Origin, FVector& AxisX, FVector& AxisY);
    virtual void TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTick) override;
    virtual FBoxSphereBounds CalcBounds(const FTransform& LocalToWorld) const override;
    const TSharedPtr<FCaneSceneHandle, ESPMode::ThreadSafe>& GetSceneHandle() const { return SceneHandle; }
protected:
    virtual void OnRegister() override;
    virtual void OnUnregister() override;
#if WITH_EDITOR
    virtual void PostEditChangeProperty(FPropertyChangedEvent& Event) override;
#endif
private:
    void ClearPresentation();
    UFUNCTION() void UpdateFrame();
    bool PrepareDirectFrame(UCanePlayer* InPlayer, TSharedPtr<FCaneSceneHandle, ESPMode::ThreadSafe>& Handle, FBox& OutBounds);
    TSharedPtr<FCaneSceneHandle, ESPMode::ThreadSafe> SceneHandle;
    FBox DirectBounds = FBox(FVector(-1), FVector(1));
};
