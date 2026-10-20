using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Cane.Unity
{
    [CreateAssetMenu(menuName = "Cane/Texture Stream")]
    public sealed class CaneTextureStream : ScriptableObject
    {
        public CaneAsset Asset;
        public AssetReferenceTexture2D[] Textures = Array.Empty<AssetReferenceTexture2D>();
        [Min(0)] public float KeepAliveSeconds = 30;
        public int Revision { get; private set; }
        [NonSerialized] private CaneStreamResources shared;

        public CaneStreamResources Acquire()
        {
            if (shared == null || shared.AssetRevision != Asset.Revision)
                shared = new CaneStreamResources(Asset, Textures);
            shared.References++; return shared;
        }
        public void Release(CaneStreamResources resources)
        {
            if (--resources.References != 0) return;
            resources.Dispose();
            if (ReferenceEquals(shared, resources)) shared = null;
        }
        /// <summary>Existing users keep their generation until released or rebuilt.</summary>
        public void Reload() { shared = null; Revision++; }
        private void OnValidate() => Reload();
        private void OnDisable() => Reload();
    }
}
