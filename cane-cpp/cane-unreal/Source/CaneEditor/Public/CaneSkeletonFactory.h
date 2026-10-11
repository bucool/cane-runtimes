#pragma once
#include "CoreMinimal.h"
#include "Factories/Factory.h"
#include "EditorReimportHandler.h"
#include "CaneSkeletonFactory.generated.h"

class UCaneSkeletonAsset;
UCLASS()
class CANEEDITOR_API UCaneSkeletonFactory : public UFactory, public FReimportHandler
{
    GENERATED_BODY()
public:
    UCaneSkeletonFactory();
    virtual bool FactoryCanImport(const FString& Filename) override;
    virtual UObject* FactoryCreateFile(UClass* Class, UObject* InParent, FName Name, EObjectFlags Flags,
        const FString& Filename, const TCHAR* Parms, FFeedbackContext* Warn, bool& bOutOperationCanceled) override;
    virtual bool CanReimport(UObject* Obj, TArray<FString>& OutFilenames) override;
    virtual void SetReimportPaths(UObject* Obj, const TArray<FString>& NewReimportPaths) override;
    virtual EReimportResult::Type Reimport(UObject* Obj) override;
private:
    bool Import(UCaneSkeletonAsset* Asset, const FString& Filename, FFeedbackContext* Warn);
};
