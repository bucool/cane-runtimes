#pragma once
#include "CoreMinimal.h"
#include "UObject/Object.h"
#include "CaneTypes.h"
#include "CaneSurface.generated.h"

class UCaneSkeletonAsset;
class UTextureRenderTarget2D;
class UMaterialInstanceDynamic;
struct FCaneRenderStatus;
namespace cane { class RuntimeFrame; }

// A fixed framing surface. Packet blending is internal to this surface; its final
// premultiplied result is displayed as one translucent primitive or UMG image.
UCLASS(BlueprintType)
class CANERUNTIME_API UCaneSurface : public UObject
{
    GENERATED_BODY()
public:
    // Internal compositing space. Texture always exposes linear premultiplied RGB
    // to Unreal materials, including when internal composition uses Gamma.
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="Cane|Display") ECaneBlendColorSpace BlendColorSpace = ECaneBlendColorSpace::Gamma;
    UPROPERTY(BlueprintReadOnly, Category="Cane") TObjectPtr<UTextureRenderTarget2D> Texture;
    UPROPERTY(BlueprintReadOnly, Category="Cane") TObjectPtr<UMaterialInstanceDynamic> SceneMaterial;
    UPROPERTY(BlueprintReadOnly, Category="Cane") TObjectPtr<UMaterialInstanceDynamic> WidgetMaterial;
    UPROPERTY(BlueprintReadOnly, Category="Cane") FVector2D Origin;
    UPROPERTY(BlueprintReadOnly, Category="Cane") FVector2D Size;
    bool Initialize(UCaneSkeletonAsset* InAsset, FString& Error);
    bool Draw(const cane::RuntimeFrame& Frame, FString& Error);
    bool CheckRenderStatus(FString& Error) const;
private:
    TSharedPtr<FCaneRenderStatus, ESPMode::ThreadSafe> RenderStatus;
    UPROPERTY() TObjectPtr<UCaneSkeletonAsset> Asset;
};
