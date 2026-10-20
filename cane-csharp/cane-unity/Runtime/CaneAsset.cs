using System;
using UnityEngine;

namespace Cane.Unity
{
    [CreateAssetMenu(menuName = "Cane/Skeleton Asset")]
    public sealed class CaneAsset : ScriptableObject
    {
        [HideInInspector] public byte[] Document = Array.Empty<byte>();
        public Texture2D[] Textures = Array.Empty<Texture2D>();
        public Shader SceneShader;
        [NonSerialized] private RuntimeAsset data;
        [NonSerialized] private CaneResources resources;
        public int Revision { get; private set; }
        public RuntimeAsset Data => data ??= RuntimeAsset.Load(Document);

        public CaneResources Acquire()
        {
            resources ??= new CaneResources(Data, Textures, sceneShader: SceneShader);
            resources.AssetReferences++; return resources;
        }
        public void Release(CaneResources source)
        {
            if (--source.AssetReferences != 0) return;
            source.Dispose(); if (ReferenceEquals(resources, source)) resources = null;
        }
        /// <summary>Invalidate shared data after changing document bytes or textures.
        /// Existing users keep their resource generation until they rebuild/release it.</summary>
        public void Reload() { data = null; resources = null; Revision++; }
        private void OnValidate() => Reload();
        private void OnDisable() => Reload();
    }
}
