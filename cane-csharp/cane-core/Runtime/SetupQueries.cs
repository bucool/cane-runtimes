using System;
using System.Collections.Generic;

namespace Cane
{
    public readonly struct SetupBone
    {
        public readonly string Id;
        public readonly string? ParentId;
        public readonly BoneLocal Local;
        public readonly string TransformMode;
        public SetupBone(string id, string? parentId, BoneLocal local, string transformMode = "inheritAll")
        { Id = id; ParentId = parentId; Local = local; TransformMode = transformMode; }
    }
    public sealed partial class RuntimePlayer
    {
        private static void CheckSetupLocal(BoneLocal value, string operation)
        { for (int i = 0; i < 7; i++) Numeric.RequireFinite(value.Get(i), operation, "local"); }
        private static bool SetupMode(string mode) => mode == "inheritAll" || mode == "translationOnly" || mode == "ignoreRotationAndReflection" || mode == "ignoreScale" || mode == "ignoreScaleAndReflection";
        /// <summary>Owned authored hierarchy, without animation, constraints, skins or host transforms.</summary>
        public static RuntimeFrame SetupBoneFrame(IReadOnlyList<SetupBone> bones)
        {
            const string op = "querySetupBones";
            var ids = new Dictionary<string, int>(StringComparer.Ordinal); var world = new Affine[bones.Count]; var state = new byte[bones.Count];
            for (int i = 0; i < bones.Count; i++)
            {
                SetupBone b = bones[i]; CheckSetupLocal(b.Local, op);
                if (string.IsNullOrWhiteSpace(b.Id) || ids.ContainsKey(b.Id) || !SetupMode(b.TransformMode)) throw new RuntimeException(RuntimeErrorCode.ValidationFailed, op, "Invalid or duplicate setup bone.", "bones");
                ids.Add(b.Id, i);
            }
            for (int start = 0; start < bones.Count; start++)
            {
                if (state[start] == 2) continue;
                var path = new List<int>(); int cursor = start;
                while (cursor >= 0 && state[cursor] != 2)
                {
                    SetupBone b = bones[cursor]; if (state[cursor] == 1) throw new RuntimeException(RuntimeErrorCode.ValidationFailed, op, "Setup hierarchy contains a cycle.");
                    state[cursor] = 1; path.Add(cursor);
                    if (b.ParentId == null) cursor = -1;
                    else if (!ids.TryGetValue(b.ParentId, out cursor)) throw new RuntimeException(RuntimeErrorCode.ValidationFailed, op, "Missing setup parent.");
                }
                for (int i = path.Count - 1; i >= 0; i--)
                { int index = path[i]; SetupBone b = bones[index]; world[index] = b.ParentId == null ? Affine.FromLocal(b.Local) : Affine.Child(world[ids[b.ParentId]], b.Local, b.TransformMode); TransformGeometry.CheckWorld(world[index]); state[index] = 2; }
            }
            var poses = new BonePose[bones.Count]; for (int i = 0; i < bones.Count; i++) poses[i] = new BonePose(bones[i].Id, world[i], bones[i].Local);
            return new RuntimeFrame(1, 0, poses, Array.Empty<string>(), Array.Empty<string>(), new RenderPacket(Array.Empty<RenderAttachment>()));
        }
        public static BoneLocal SolveBoneLocalForWorld(Affine? parent, Affine world, string mode, BoneLocal reference)
        {
            const string op = "solveBoneLocalForWorld"; CheckSetupLocal(reference, op);
            if (!world.IsFinite || (parent.HasValue && !parent.Value.IsFinite) || !SetupMode(mode)) throw new RuntimeException(RuntimeErrorCode.InvalidArgument, op, "Affine components and inheritance mode must be valid.");
            try { return TransformGeometry.Recover(world, parent ?? Affine.Identity, parent.HasValue ? mode : "inheritAll", reference); }
            catch (RuntimeException) { throw new RuntimeException(RuntimeErrorCode.InvalidState, op, "Requested world pose is unreachable under the selected parent and inheritance mode."); }
        }
    }
}
