#pragma once
#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "CaneSkeletonAsset.generated.h"

class UTexture2D;
class UMaterialInterface;
class UAssetImportData;
namespace cane { class RuntimeData; }

USTRUCT()
struct CANERUNTIME_API FCaneTextureAsset
{
    GENERATED_BODY()
    UPROPERTY() FString Key;
    UPROPERTY() FString ImageId;
    UPROPERTY() FString AtlasId;
    UPROPERTY() FString PageId;
    UPROPERTY() bool IsAtlasPage = false;
    UPROPERTY() int32 Width = 0;
    UPROPERTY() int32 Height = 0;
    UPROPERTY() TObjectPtr<UTexture2D> Texture;
};

UCLASS(BlueprintType)
class CANERUNTIME_API UCaneSkeletonAsset : public UDataAsset
{
    GENERATED_BODY()
public:
    UPROPERTY(VisibleAnywhere, Category="Cane") FString SkeletonName;
    UPROPERTY(VisibleAnywhere, Category="Cane") TArray<FString> Animations;
    UPROPERTY(VisibleAnywhere, Category="Cane") TArray<FString> Skins;
    UPROPERTY(VisibleAnywhere, Category="Cane") TArray<FString> Bones;
    UPROPERTY(VisibleAnywhere, Category="Cane") TArray<FString> ImportWarnings;
    UPROPERTY(EditAnywhere, Category="Cane|Display") FVector2D SurfaceOrigin = FVector2D(-512, -512);
    UPROPERTY(EditAnywhere, Category="Cane|Display") FVector2D SurfaceSize = FVector2D(1024, 1024);
    UPROPERTY(EditAnywhere, Category="Cane|Display", meta=(ClampMin="32", ClampMax="4096")) int32 Resolution = 1024;
    UPROPERTY(VisibleAnywhere, Category="Cane|Display") TObjectPtr<UMaterialInterface> SceneMaterial;
    UPROPERTY(VisibleAnywhere, Category="Cane|Display") TObjectPtr<UMaterialInterface> WidgetMaterial;
    UPROPERTY() TArray<uint8> SourceBytes;
    UPROPERTY() bool BinarySource = false;
    UPROPERTY() TMap<FString, FString> AtlasSources;
    UPROPERTY() TArray<FCaneTextureAsset> Textures;
#if WITH_EDITORONLY_DATA
    UPROPERTY(VisibleAnywhere, Instanced, Category="Import") TObjectPtr<UAssetImportData> AssetImportData;
#endif
    // Fully decoded immutable Core data. No disk paths are resolved in a cooked game.
    TSharedPtr<const cane::RuntimeData> LoadData(FString& Error) const;
    void InvalidateData();
private:
    mutable TSharedPtr<const cane::RuntimeData> CachedData;
};
