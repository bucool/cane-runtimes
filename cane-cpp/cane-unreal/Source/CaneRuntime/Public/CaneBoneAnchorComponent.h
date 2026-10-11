#pragma once
#include "CoreMinimal.h"
#include "Components/SceneComponent.h"
#include "CaneBoneAnchorComponent.generated.h"
class UCaneComponent;

// Position attachment using the full published affine, including local offset.
// This component intentionally does not approximate skeletal shear as an FTransform.
UCLASS(ClassGroup=(Cane), meta=(BlueprintSpawnableComponent))
class CANERUNTIME_API UCaneBoneAnchorComponent : public USceneComponent
{
    GENERATED_BODY()
public:
    UCaneBoneAnchorComponent();
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane") TObjectPtr<UCaneComponent> Source;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane") FString BoneId;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane") FVector2D LocalOffset = FVector2D::ZeroVector;
    UPROPERTY(BlueprintReadOnly, Category="Cane") bool bBoneActive = false;
    UPROPERTY(BlueprintReadOnly, Category="Cane") FVector BoneAxisX = FVector::ZeroVector;
    UPROPERTY(BlueprintReadOnly, Category="Cane") FVector BoneAxisY = FVector::ZeroVector;
    virtual void TickComponent(float Delta, ELevelTick TickType, FActorComponentTickFunction* Tick) override;
protected:
    virtual void OnRegister() override;
    virtual void OnUnregister() override;
private:
    TWeakObjectPtr<UCaneComponent> Prerequisite;
};
