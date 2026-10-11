using System;
using System.Collections.Generic;

namespace Cane
{
    public readonly struct RuntimeVersionRange
    {
        public uint MinimumMajor { get; }
        public uint MinimumMinor { get; }
        public uint MaximumMajor { get; }
        public uint MaximumMinor { get; }
        internal RuntimeVersionRange(uint minimumMajor, uint minimumMinor, uint maximumMajor, uint maximumMinor)
        { MinimumMajor = minimumMajor; MinimumMinor = minimumMinor; MaximumMajor = maximumMajor; MaximumMinor = maximumMinor; }
        public bool Contains(uint major, uint minor) =>
            (major > MinimumMajor || (major == MinimumMajor && minor >= MinimumMinor)) &&
            (major < MaximumMajor || (major == MaximumMajor && minor <= MaximumMinor));
    }

    public sealed class RuntimeConformanceInfo
    {
        public string SuiteVersion => "1.17.0";
        public string ManifestSha256 => "f462e113115ecd60322f6f5d9df451e05d82649d0ded3278137f4b873e3ce0f9";
        public int RequiredCaseCount => 391;
        internal RuntimeConformanceInfo() { }
    }

    /// <summary>Immutable Core metadata. This conformance result does not certify an engine or complete SDK.</summary>
    public sealed class RuntimeCapabilities
    {
        private readonly string[] features = {
            "animation.curve.roots-v2",
            "animation.sequence.clock-v2",
            "animation.track.pose-v2",
            "attachment.bounding-box",
            "attachment.clipping",
            "attachment.mesh",
            "attachment.path",
            "attachment.point",
            "attachment.region",
            "attachment.sequence",
            "blend.add",
            "blend.multiply",
            "blend.screen",
            "bone.inheritance.polar-v2",
            "constraint.ik.affine-v2",
            "constraint.path.geometry-v2",
            "constraint.physics.spring-v2",
            "constraint.slider",
            "constraint.transform.channels-v2",
            "event",
            "mesh.deform",
            "mesh.placement",
            "mesh.weighted",
            "skin",
            "timeline.draw-order",
            "timeline.skin",
            "tint.two-color",
        };
        public static RuntimeCapabilities Current { get; } = new RuntimeCapabilities();
        public string ImplementationName => "cane-csharp-runtime";
        public string ImplementationVersion => "0.1.0-alpha.5";
        public string NumericPrecision => "binary32";
        public RuntimeVersionRange RuntimeJson { get; } = new RuntimeVersionRange(1, 1, 1, 1);
        public RuntimeVersionRange Caneb { get; } = new RuntimeVersionRange(1, 1, 1, 1);
        public RuntimeVersionRange Atlas { get; } = new RuntimeVersionRange(1, 0, 1, 0);
        public RuntimeVersionRange RuntimeApi { get; } = new RuntimeVersionRange(1, 0, 1, 13);
        public IReadOnlyList<string> SupportedRuntimeFeatures { get; }
        public ulong ApiFeatureBits => (1UL << 53) - 1;
        public RuntimeConformanceInfo LastPassedConformance { get; } = new RuntimeConformanceInfo();
        private RuntimeCapabilities() { SupportedRuntimeFeatures = Array.AsReadOnly(features); }
        public bool SupportsRuntimeFeature(string? name) => name != null && Array.BinarySearch(features, name, StringComparer.Ordinal) >= 0;
    }
}
