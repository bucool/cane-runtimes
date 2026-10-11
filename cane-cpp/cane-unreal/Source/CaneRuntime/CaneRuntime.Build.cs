using UnrealBuildTool;

public class CaneRuntime : ModuleRules
{
    public CaneRuntime(ReadOnlyTargetRules Target) : base(Target)
    {
        if (Target.Version.MajorVersion != 5 || Target.Version.MinorVersion != 8)
            throw new BuildException("CaneUnreal targets Unreal Engine 5.8. Build and validate against that engine version.");
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        CppStandard = CppStandardVersion.Cpp20;
        bEnableExceptions = true;
        bUseUnity = false;
        PublicDependencyModuleNames.AddRange(new[] {"Core", "CoreUObject", "Engine", "UMG", "ProceduralMeshComponent", "CaneCore"});
        PrivateDependencyModuleNames.AddRange(new[] {"Slate", "SlateCore", "RenderCore", "Renderer", "RHI", "Projects"});
    }
}
