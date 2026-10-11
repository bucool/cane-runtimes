using System.IO;
using System;
using System.Security.Cryptography;
using UnrealBuildTool;

public class CaneCore : ModuleRules
{
    public CaneCore(ReadOnlyTargetRules Target) : base(Target)
    {
        Type = ModuleType.External;
        if (Target.bDebugBuildsActuallyUseDebugCRT)
            throw new BuildException("The staged Cane Core library uses the release CRT. Debug CRT targets need a separately built matching library.");
        if (Target.Platform != UnrealTargetPlatform.Win64)
            throw new BuildException("Cane Unreal currently provides the Win64 integration. Other platforms require a matching Core build and engine validation.");
        string Library = Path.Combine(ModuleDirectory, "Lib", "Win64", "cane-core.lib");
        string Includes = Path.Combine(ModuleDirectory, "Include");
        if (!File.Exists(Library) || !File.Exists(Path.Combine(Includes, "cane", "runtime_player.hpp")))
            throw new BuildException("Prepare the Cane Core SDK with cane-unreal/CMakeLists.txt before building this plugin. See docs/INSTALLATION.md.");
        PublicSystemIncludePaths.Add(Includes);
        PublicAdditionalLibraries.Add(Library);
        // Refresh the rules when staging a new SDK. UBT can skip .lib timestamp
        // changes as import-library dependencies; a content fingerprint also
        // invalidates each consumer's compile command and forces it to relink.
        ExternalDependencies.Add(Library);
        using (var CoreLibrary = File.OpenRead(Library))
        using (var Hasher = SHA256.Create())
        {
            string Fingerprint = BitConverter.ToString(Hasher.ComputeHash(CoreLibrary)).Replace("-", "");
            PublicDefinitions.Add("CANE_CORE_SDK_SHA256=\"" + Fingerprint + "\"");
        }
    }
}
