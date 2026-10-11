#include "CaneTextureBinding.h"
#include "CaneSkeletonAsset.h"
#include "Engine/Texture2D.h"
#include "TextureResource.h"
#include <stdexcept>

TArray<FTextureResource*> BindCaneTextures(const UCaneSkeletonAsset& Asset, const std::vector<cane_unreal::Draw>& Draws)
{
    check(IsInGameThread());
    TArray<FTextureResource*> Resources;
    Resources.Reserve(static_cast<int32>(Draws.size()));
    for (const auto& Draw : Draws)
    {
        const FString Key(UTF8_TO_TCHAR(Draw.texture_key.c_str()));
        const auto* Entry = Asset.Textures.FindByPredicate([&](const FCaneTextureAsset& Item) { return Item.Key == Key; });
        if (!Entry || !Entry->Texture || Entry->Width != static_cast<int32>(Draw.width) ||
            Entry->Height != static_cast<int32>(Draw.height) || Entry->Texture->SRGB)
            throw std::invalid_argument("Cane packet references a missing or incompatible texture resource.");
        auto* Resource = Entry->Texture->GetResource();
        if (!Resource) throw std::runtime_error("Cane texture resource is not ready. Wait for texture compilation before playback.");
        Resources.Add(Resource);
    }
    return Resources;
}
