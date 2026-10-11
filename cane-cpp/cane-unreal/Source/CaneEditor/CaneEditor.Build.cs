using UnrealBuildTool;

public class CaneEditor : ModuleRules
{
    public CaneEditor(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        CppStandard = CppStandardVersion.Cpp20;
        bEnableExceptions = true;
        bUseUnity = false;
        PrivateDependencyModuleNames.AddRange(new[] {"Core", "CoreUObject", "Engine", "CaneRuntime", "CaneCore", "UnrealEd", "AssetTools", "AssetRegistry", "ImageWrapper", "MaterialEditor", "Projects", "Slate", "SlateCore"});
    }
}
