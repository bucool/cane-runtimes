#include "CaneSkeletonAsset.h"
#include "Engine/Texture2D.h"
#include "cane/runtime_load_plan.hpp"
#include "Portable/packet_projection.hpp"
#include <exception>

TSharedPtr<const cane::RuntimeData> UCaneSkeletonAsset::LoadData(FString& Error) const
{
    check(IsInGameThread());
    Error.Reset();
    if (CachedData) return CachedData;
    try
    {
        if (SourceBytes.IsEmpty()) throw std::invalid_argument("Cane asset has no source data.");
        cane::RuntimeLoadOptions Options;
        for (const auto& Pair : AtlasSources)
            Options.atlas_json.emplace(TCHAR_TO_UTF8(*Pair.Key), TCHAR_TO_UTF8(*Pair.Value));
        const std::vector<uint8_t> Bytes(SourceBytes.GetData(), SourceBytes.GetData() + SourceBytes.Num());
        const auto Plan = BinarySource ? cane::RuntimeLoadPlan::from_caneb(Bytes, Options) :
            cane::RuntimeLoadPlan::from_json(std::string_view(reinterpret_cast<const char*>(Bytes.data()), Bytes.size()), Options);
        if (Plan.texture_requests().size() != static_cast<size_t>(Textures.Num()))
            throw std::invalid_argument("Cane texture inventory is incomplete. Reimport the asset.");
        std::vector<cane::DecodedTextureDimensions> Dimensions;
        TSet<FString> Keys;
        for (const auto& Entry : Textures)
        {
            const auto Key = Entry.IsAtlasPage ? cane_unreal::resource_key(TCHAR_TO_UTF8(*Entry.AtlasId), TCHAR_TO_UTF8(*Entry.PageId)) :
                cane_unreal::resource_key(TCHAR_TO_UTF8(*Entry.ImageId));
            if (Entry.Key != UTF8_TO_TCHAR(Key.c_str()) || Keys.Contains(Entry.Key) || Entry.Width <= 0 || Entry.Height <= 0 ||
                (!IsRunningDedicatedServer() && (!Entry.Texture || Entry.Texture->GetSizeX() != Entry.Width || Entry.Texture->GetSizeY() != Entry.Height || Entry.Texture->SRGB)))
                throw std::invalid_argument("Cane requires unique, full-resolution raw texture assets. Reimport the asset.");
            Keys.Add(Entry.Key);
            cane::DecodedTextureDimensions Size;
            Size.kind = Entry.IsAtlasPage ? cane::TextureResourceKind::atlas_page : cane::TextureResourceKind::direct;
            Size.image_id = TCHAR_TO_UTF8(*Entry.ImageId); Size.atlas_id = TCHAR_TO_UTF8(*Entry.AtlasId); Size.page_id = TCHAR_TO_UTF8(*Entry.PageId);
            Size.width = static_cast<uint32_t>(Entry.Width); Size.height = static_cast<uint32_t>(Entry.Height);
            Dimensions.push_back(std::move(Size));
        }
        CachedData = MakeShared<cane::RuntimeData>(Plan.create_data(Dimensions));
        return CachedData;
    }
    catch (const std::exception& Exception) { Error = UTF8_TO_TCHAR(Exception.what()); return {}; }
}

void UCaneSkeletonAsset::InvalidateData() { check(IsInGameThread()); CachedData.Reset(); }
