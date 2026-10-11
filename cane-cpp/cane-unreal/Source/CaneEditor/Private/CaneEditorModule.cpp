#include "Modules/ModuleManager.h"
#include "AssetToolsModule.h"
#include "AssetTypeActions_Base.h"
#include "CaneSkeletonAsset.h"

class FCaneAssetActions final : public FAssetTypeActions_Base
{
public:
    virtual FText GetName() const override { return NSLOCTEXT("Cane", "SkeletonAsset", "Cane Skeleton"); }
    virtual FColor GetTypeColor() const override { return FColor(38, 161, 207); }
    virtual UClass* GetSupportedClass() const override { return UCaneSkeletonAsset::StaticClass(); }
    virtual uint32 GetCategories() override { return EAssetTypeCategories::Animation; }
};
class FCaneEditorModule final : public IModuleInterface
{
    TSharedPtr<FCaneAssetActions> Actions;
public:
    virtual void StartupModule() override
    {
        Actions = MakeShared<FCaneAssetActions>();
        FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools")).Get().RegisterAssetTypeActions(Actions.ToSharedRef());
    }
    virtual void ShutdownModule() override
    {
        if (Actions && FModuleManager::Get().IsModuleLoaded(TEXT("AssetTools")))
            FModuleManager::GetModuleChecked<FAssetToolsModule>(TEXT("AssetTools")).Get().UnregisterAssetTypeActions(Actions.ToSharedRef());
        Actions.Reset();
    }
};
IMPLEMENT_MODULE(FCaneEditorModule, CaneEditor)
