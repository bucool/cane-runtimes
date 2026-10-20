using UnityEngine;

namespace Cane.Unity
{
    [DisallowMultipleComponent, RequireComponent(typeof(CanePlayer)), DefaultExecutionOrder(30)]
    public sealed class CaneTextureLoader : MonoBehaviour
    {
        public CaneTextureStream Source;
        public CaneStreamResources Stream { get; private set; }
        private CaneTextureStream retained;
        private CanePlayer player;
        private int revision;

        private void OnEnable() => Rebuild();
        public void Rebuild()
        {
            player = GetComponent<CanePlayer>();
            var next = Source == null || Source.Asset == null ? null : Source.Acquire();
            player.Asset = null;
            try { player.Initialize(next?.Resources); }
            catch { if (next != null) Source.Release(next); throw; }
            if (Stream != null) retained.Release(Stream);
            retained = Source; Stream = next; revision = Source == null ? 0 : Source.Revision;
            RequestVisible();
        }
        /// <summary>Also usable after a manually evaluated frame. Runtime-added textures remain caller-managed.</summary>
        public void RequestVisible()
        {
            var draws = player?.Skeleton?.Frame?.Draws;
            if (Stream == null || draws == null) return;
            foreach (var draw in draws) if (draw.Texture < Stream.PageCount) Stream.Request(draw.Texture);
        }
        private void LateUpdate()
        {
            if (Source != retained || Source != null && (Source.Revision != revision || Stream != null && Source.Asset != null && Stream.AssetRevision != Source.Asset.Revision)) Rebuild();
            RequestVisible(); Stream?.CollectUnused(Source.KeepAliveSeconds);
        }
        private void OnDisable()
        {
            if (Stream == null) return;
            if (player != null && ReferenceEquals(player.Resources, Stream.Resources)) { player.Asset = null; player.Initialize(); }
            retained.Release(Stream); Stream = null; retained = null;
        }
    }
}
