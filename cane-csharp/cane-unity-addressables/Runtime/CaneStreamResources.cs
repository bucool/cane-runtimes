using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Cane.Unity
{
    /// <summary>A shared resource generation. Acquire/release through CaneTextureStream.</summary>
    public sealed class CaneStreamResources
    {
        private struct Page
        {
            internal AsyncOperationHandle<Texture2D> Handle;
            internal int Version;
            internal double LastUsed;
            internal int LastFrame;
            internal bool Installed;
        }
        public readonly CaneResources Resources;
        internal readonly int AssetRevision;
        internal int References;
        private readonly Texture2D[] placeholders;
        private readonly object[] keys;
        private readonly Page[] pages;
        private bool disposed;
        private int sweepFrame = -1;
        public int PageCount => pages.Length;
        public event Action<int> Loaded;
        public event Action<int, Exception> Failed;

        internal CaneStreamResources(CaneAsset asset, AssetReferenceTexture2D[] textures)
        {
            if (textures.Length != asset.Textures.Length) throw new ArgumentException("Addressable pages must match the Cane texture count.", nameof(textures));
            placeholders = (Texture2D[])asset.Textures.Clone(); keys = new object[textures.Length]; pages = new Page[textures.Length];
            for (int i = 0; i < keys.Length; i++) if (textures[i] != null && textures[i].RuntimeKeyIsValid()) keys[i] = textures[i].RuntimeKey;
            Resources = new CaneResources(asset.Data, placeholders, sceneShader: asset.SceneShader);
            AssetRevision = asset.Revision;
        }
        public bool IsLoaded(int page) => keys[page] == null || pages[page].Installed;

        /// <summary>Requests a page, or touches a resident page. The returned handle is borrowed;
        /// this generation releases it. Failed requests are retried explicitly via Unload.</summary>
        public AsyncOperationHandle<Texture2D> Request(int page)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CaneStreamResources));
            ref var entry = ref pages[page]; entry.LastUsed = Time.unscaledTimeAsDouble; entry.LastFrame = Time.frameCount;
            if (keys[page] == null || entry.Handle.IsValid()) return entry.Handle;
            int version = ++entry.Version;
            var operation = entry.Handle = Addressables.LoadAssetAsync<Texture2D>(keys[page]);
            operation.Completed += result => {
                if (disposed || pages[page].Version != version) return;
                if (result.Status != AsyncOperationStatus.Succeeded) { Failed?.Invoke(page, result.OperationException); return; }
                pages[page].Installed = true;
                try { Resources.ReplaceTexture(page, result.Result, Resources.Asset.Textures[page]); }
                catch (Exception error) {
                    if (!disposed && pages[page].Version == version) { pages[page].Installed = false; Failed?.Invoke(page, error); }
                    return;
                }
                if (disposed || pages[page].Version != version) return;
                Loaded?.Invoke(page);
            };
            return operation;
        }
        public void Unload(int page)
        {
            ref var entry = ref pages[page];
            bool installed = entry.Installed;
            var handle = entry.Handle; entry.Handle = default; entry.Installed = false; entry.Version++;
            try { if (installed) Resources.ReplaceTexture(page, placeholders[page], Resources.Asset.Textures[page]); }
            finally { if (handle.IsValid()) Addressables.Release(handle); }
        }
        internal void CollectUnused(float keepAliveSeconds)
        {
            if (keepAliveSeconds <= 0 || sweepFrame == Time.frameCount) return;
            sweepFrame = Time.frameCount; double before = Time.unscaledTimeAsDouble - keepAliveSeconds;
            for (int i = 0; i < pages.Length; i++)
                if (pages[i].Handle.IsValid() && pages[i].LastFrame < Time.frameCount - 1 && pages[i].LastUsed < before) Unload(i);
        }
        internal void Dispose()
        {
            disposed = true; Resources.Dispose();
            for (int i = 0; i < pages.Length; i++) {
                var handle = pages[i].Handle; pages[i].Handle = default; pages[i].Installed = false;
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }
    }
}
