#include "CaneSkeletonFactory.h"
#include "CaneSkeletonAsset.h"
#include "cane/runtime_load_plan.hpp"
#include "cane/runtime_player.hpp"
#include "EditorFramework/AssetImportData.h"
#include "Engine/Texture2D.h"
#include "IImageWrapper.h"
#include "IImageWrapperModule.h"
#include "Materials/Material.h"
#include "Materials/MaterialExpressionTextureSampleParameter2D.h"
#include "MaterialEditingLibrary.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/FeedbackContext.h"
#include "Modules/ModuleManager.h"
#include "UObject/Package.h"
#include "UObject/StrongObjectPtr.h"
#include "TextureCompiler.h"
#include <stdexcept>

namespace
{
struct FImageSource { cane::RuntimeTextureRequest Request; TArray64<uint8> Pixels; int32 Width = 0, Height = 0; FString Key; };
std::string KeyFor(const cane::RuntimeTextureRequest& R)
{
    if (R.kind == cane::TextureResourceKind::direct) return "i" + R.image_id.value();
    const auto& A = R.atlas_id.value(); return "a" + std::to_string(A.size()) + ":" + A + R.page_id.value();
}
FString Resolve(const FString& Root, const FString& Base, const FString& Relative)
{
    if (!FPaths::IsRelative(Relative) || Relative.Contains(TEXT(":")))
        throw std::invalid_argument("Cane resource paths must be relative to the imported export directory.");
    FString Path = FPaths::ConvertRelativePathToFull(FPaths::Combine(Base, Relative));
    FPaths::NormalizeFilename(Path); FPaths::CollapseRelativeDirectories(Path);
    if (!FPaths::IsUnderDirectory(Path, Root)) throw std::invalid_argument("Cane dependency escapes the imported export directory.");
    return Path;
}
FString ReadText(const FString& Path)
{
    FString Text;
    if (!FFileHelper::LoadFileToString(Text, *Path)) throw std::runtime_error(TCHAR_TO_UTF8(*(TEXT("Cannot read Cane atlas: ") + Path)));
    return Text;
}
UMaterial* MakeMaterial(UObject* Outer, bool Widget, UTexture2D* DefaultTexture)
{
    auto* Material = NewObject<UMaterial>(Outer, NAME_None, RF_Public);
    Material->MaterialDomain = Widget ? MD_UI : MD_Surface;
    Material->BlendMode = BLEND_AlphaComposite;
    Material->SetShadingModel(MSM_Unlit);
    Material->TwoSided = true;
    auto* Sample = CastChecked<UMaterialExpressionTextureSampleParameter2D>(
        UMaterialEditingLibrary::CreateMaterialExpression(Material, UMaterialExpressionTextureSampleParameter2D::StaticClass()));
    Sample->ParameterName = TEXT("CaneSurface"); Sample->Texture = DefaultTexture;
    Sample->SamplerType = SAMPLERTYPE_LinearColor;
    Material->UpdateExpressionParameterName(Sample);
    Material->GetEditorOnlyData()->EmissiveColor.Connect(0, Sample);
    Material->GetEditorOnlyData()->Opacity.Connect(4, Sample);
    Material->PostEditChange();
    return Material;
}
}
UCaneSkeletonFactory::UCaneSkeletonFactory()
{
    SupportedClass = UCaneSkeletonAsset::StaticClass();
    bEditorImport = true; bCreateNew = false;
    Formats.Add(TEXT("caneb;Cane runtime binary")); Formats.Add(TEXT("json;Cane runtime JSON"));
    ImportPriority = DefaultImportPriority - 10;
}
bool UCaneSkeletonFactory::FactoryCanImport(const FString& Filename)
{
    TArray<uint8> Bytes;
    if (!FFileHelper::LoadFileToArray(Bytes, *Filename) || Bytes.IsEmpty()) return false;
    try { const std::vector<uint8_t> Source(Bytes.GetData(), Bytes.GetData() + Bytes.Num());
        static_cast<void>(cane::RuntimeLoadPlan::inspect_atlas_references(Source)); return true; }
    catch (const std::exception&) { return false; }
}
UObject* UCaneSkeletonFactory::FactoryCreateFile(UClass* Class, UObject* Parent, FName Name, EObjectFlags Flags,
    const FString& Filename, const TCHAR*, FFeedbackContext* Warn, bool& Canceled)
{
    Canceled = false;
    auto* Asset = NewObject<UCaneSkeletonAsset>(Parent, Class, Name, Flags);
    if (Import(Asset, Filename, Warn)) return Asset;
    Asset->ClearFlags(RF_Public | RF_Standalone); Asset->MarkAsGarbage(); return nullptr;
}
bool UCaneSkeletonFactory::Import(UCaneSkeletonAsset* Asset, const FString& Filename, FFeedbackContext* Warn)
{
    // Prepare all dependencies and validate Core before replacing any live asset fields.
    try
    {
        TArray<uint8> Bytes;
        if (!FFileHelper::LoadFileToArray(Bytes, *Filename) || Bytes.IsEmpty()) throw std::runtime_error("Cannot read Cane runtime source.");
        const std::vector<uint8_t> Source(Bytes.GetData(), Bytes.GetData() + Bytes.Num());
        const bool Binary = Bytes.Num() >= 5 && FMemory::Memcmp(Bytes.GetData(), "CANEB", 5) == 0;
        const FString Root = FPaths::ConvertRelativePathToFull(FPaths::GetPath(Filename));
        cane::RuntimeLoadOptions Options;
        TMap<FString, FString> Atlases;
        TArray<FString> Files; Files.Add(Filename);
        for (const auto& Reference : cane::RuntimeLoadPlan::inspect_atlas_references(Source))
        {
            const auto Path = Resolve(Root, Root, UTF8_TO_TCHAR(Reference.path.c_str()));
            const auto Json = ReadText(Path);
            Atlases.Add(UTF8_TO_TCHAR(Reference.atlas_id.c_str()), Json);
            Options.atlas_json.emplace(Reference.atlas_id, TCHAR_TO_UTF8(*Json)); Files.AddUnique(Path);
        }
        const auto Plan = Binary ? cane::RuntimeLoadPlan::from_caneb(Source, Options) :
            cane::RuntimeLoadPlan::from_json(std::string_view(reinterpret_cast<const char*>(Source.data()), Source.size()), Options);
        TArray<FImageSource> Images;
        std::vector<cane::DecodedTextureDimensions> Dimensions;
        auto& Wrappers = FModuleManager::LoadModuleChecked<IImageWrapperModule>(TEXT("ImageWrapper"));
        for (const auto& Request : Plan.texture_requests())
        {
            const auto Base = Request.atlas_path ? FPaths::GetPath(Resolve(Root, Root, UTF8_TO_TCHAR(Request.atlas_path->c_str()))) : Root;
            const auto Path = Resolve(Root, Base, UTF8_TO_TCHAR(Request.path.c_str()));
            TArray64<uint8> Compressed;
            if (!FFileHelper::LoadFileToArray(Compressed, *Path)) throw std::runtime_error(TCHAR_TO_UTF8(*(TEXT("Cannot read texture: ") + Path)));
            const auto Format = Wrappers.DetectImageFormat(Compressed.GetData(), Compressed.Num());
            if (Format != EImageFormat::PNG && Format != EImageFormat::JPEG && Format != EImageFormat::BMP)
                throw std::runtime_error("Cane Unreal import currently accepts PNG, JPEG and BMP textures.");
            auto Decoder = Wrappers.CreateImageWrapper(Format);
            FImageSource Image; Image.Request = Request; Image.Key = UTF8_TO_TCHAR(KeyFor(Request).c_str());
            if (!Decoder || !Decoder->SetCompressed(Compressed.GetData(), Compressed.Num()) || !Decoder->GetRaw(ERGBFormat::BGRA, 8, Image.Pixels))
                throw std::runtime_error("Could not decode Cane texture as RGBA8.");
            Image.Width = Decoder->GetWidth(); Image.Height = Decoder->GetHeight();
            if (Image.Width <= 0 || Image.Height <= 0 || Image.Width > 16384 || Image.Height > 16384)
                throw std::runtime_error("Cane texture dimensions exceed the adapter's 16384 pixel limit.");
            cane::DecodedTextureDimensions D;
            D.kind = Request.kind; D.image_id = Request.image_id.value_or(""); D.atlas_id = Request.atlas_id.value_or(""); D.page_id = Request.page_id.value_or("");
            D.width = static_cast<uint32_t>(Image.Width); D.height = static_cast<uint32_t>(Image.Height);
            Dimensions.push_back(std::move(D)); Images.Add(MoveTemp(Image)); Files.AddUnique(Path);
        }
        const auto Data = Plan.create_data(Dimensions);
        const cane::RuntimePlayer Setup(Data);
        TArray<FCaneTextureAsset> Textures;
        TArray<UTexture*> CompilationTextures;
        for (const auto& Image : Images)
        {
            auto* Texture = NewObject<UTexture2D>(Asset, NAME_None, RF_Public);
            Texture->Source.Init(Image.Width, Image.Height, 1, 1, TSF_BGRA8, Image.Pixels.GetData());
            // Shader performs explicit sRGB/PMA/filter/wrap conversion from raw bytes.
            Texture->SRGB = false; Texture->CompressionSettings = TC_VectorDisplacementmap;
            Texture->MipGenSettings = TMGS_NoMipmaps; Texture->NeverStream = true;
            Texture->LODGroup = TEXTUREGROUP_UI; Texture->Filter = TF_Nearest;
            Texture->PostEditChange();
            CompilationTextures.Add(Texture);
            FCaneTextureAsset Entry; Entry.Key = Image.Key; Entry.Texture = Texture;
            Entry.IsAtlasPage = Image.Request.kind == cane::TextureResourceKind::atlas_page;
            Entry.ImageId = UTF8_TO_TCHAR(Image.Request.image_id.value_or("").c_str());
            Entry.AtlasId = UTF8_TO_TCHAR(Image.Request.atlas_id.value_or("").c_str());
            Entry.PageId = UTF8_TO_TCHAR(Image.Request.page_id.value_or("").c_str());
            Entry.Width = Image.Width; Entry.Height = Image.Height; Textures.Add(MoveTemp(Entry));
        }
        // A raw default exists even for an empty skeleton so material compilation is valid.
        auto* Default = Textures.IsEmpty() ? NewObject<UTexture2D>(Asset, NAME_None, RF_Public) : Textures[0].Texture.Get();
        if (Textures.IsEmpty()) { const uint8 Clear[4] = {0, 0, 0, 0}; Default->Source.Init(1, 1, 1, 1, TSF_BGRA8, Clear); Default->SRGB = false; Default->CompressionSettings = TC_VectorDisplacementmap; Default->MipGenSettings = TMGS_NoMipmaps; Default->PostEditChange(); }
        if (Textures.IsEmpty()) CompilationTextures.Add(Default);
        FTextureCompilingManager::Get().FinishCompilation(CompilationTextures);
        auto* SceneMaterial = MakeMaterial(Asset, false, Default);
        auto* WidgetMaterial = MakeMaterial(Asset, true, Default);
        Asset->Modify();
        Asset->SourceBytes = MoveTemp(Bytes); Asset->BinarySource = Binary; Asset->AtlasSources = MoveTemp(Atlases); Asset->Textures = MoveTemp(Textures);
        Asset->SkeletonName = UTF8_TO_TCHAR(Data.skeleton().name.c_str());
        Asset->Animations.Reset(); Asset->Skins.Reset(); Asset->Bones.Reset(); Asset->ImportWarnings.Reset();
        for (const auto& Warning : Data.warnings()) Asset->ImportWarnings.Add(UTF8_TO_TCHAR(Warning.message().c_str()));
        for (const auto& Item : Data.catalog(cane::RuntimeCatalogKind::animation)) Asset->Animations.Add(UTF8_TO_TCHAR(Item.id.c_str()));
        for (const auto& Item : Data.catalog(cane::RuntimeCatalogKind::skin)) Asset->Skins.Add(UTF8_TO_TCHAR(Item.id.c_str()));
        for (const auto& Item : Data.catalog(cane::RuntimeCatalogKind::bone)) Asset->Bones.Add(UTF8_TO_TCHAR(Item.id.c_str()));
        // Fit the initial pose once. Playback never changes the surface framing.
        FBox2D Bounds(ForceInit);
        const auto SetupFrame = Setup.frame();
        for (const auto& Draw : SetupFrame.render_packet().attachments())
            for (size_t I = 0; I < Draw.world_vertices_xy.size(); I += 2) Bounds += FVector2D(Draw.world_vertices_xy[I], Draw.world_vertices_xy[I + 1]);
        if (Asset->AssetImportData == nullptr && Bounds.bIsValid)
        {
            const FVector2D Padding(32, 32); Asset->SurfaceOrigin = Bounds.Min - Padding;
            Asset->SurfaceSize = Bounds.GetSize() + Padding * 2;
        }
        Asset->SceneMaterial = SceneMaterial; Asset->WidgetMaterial = WidgetMaterial;
        Asset->AssetImportData = NewObject<UAssetImportData>(Asset);
        Asset->AssetImportData->Update(Filename);
        // Reimport uses the root source; dependencies remain listed for visibility.
        for (int32 Index = 1; Index < Files.Num(); ++Index) Asset->AssetImportData->ScriptedAddFilename(Files[Index], Index, TEXT("Dependency"));
        Asset->InvalidateData(); Asset->MarkPackageDirty(); Asset->PostEditChange();
        return true;
    }
    catch (const std::exception& Error)
    {
        if (Warn) Warn->Logf(ELogVerbosity::Error, TEXT("Cane import failed: %s"), UTF8_TO_TCHAR(Error.what()));
        return false;
    }
}
bool UCaneSkeletonFactory::CanReimport(UObject* Obj, TArray<FString>& OutFilenames)
{
    auto* Asset = Cast<UCaneSkeletonAsset>(Obj);
    if (!Asset || !Asset->AssetImportData) return false;
    OutFilenames.Add(Asset->AssetImportData->GetFirstFilename()); return true;
}
void UCaneSkeletonFactory::SetReimportPaths(UObject* Obj, const TArray<FString>& Paths)
{
    if (auto* Asset = Cast<UCaneSkeletonAsset>(Obj); Asset && Asset->AssetImportData && Paths.Num() == 1) Asset->AssetImportData->UpdateFilenameOnly(Paths[0]);
}
EReimportResult::Type UCaneSkeletonFactory::Reimport(UObject* Obj)
{
    auto* Asset = Cast<UCaneSkeletonAsset>(Obj);
    return Asset && Asset->AssetImportData && Import(Asset, Asset->AssetImportData->GetFirstFilename(), GWarn) ? EReimportResult::Succeeded : EReimportResult::Failed;
}
