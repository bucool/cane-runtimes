#include "Modules/ModuleManager.h"
#include "Interfaces/IPluginManager.h"
#include "Misc/Paths.h"
#include "Misc/CoreDelegates.h"
#include "ShaderCore.h"
#include "CaneSceneView.h"

class FCaneRuntimeModule final : public IModuleInterface
{
public:
    virtual void StartupModule() override
    {
        const auto Plugin = IPluginManager::Get().FindPlugin(TEXT("CaneUnreal"));
        check(Plugin.IsValid());
        AddShaderSourceDirectoryMapping(TEXT("/Plugin/CaneUnreal"), FPaths::Combine(Plugin->GetBaseDir(), TEXT("Shaders")));
        // Shader types must register early; view extensions need the initialized
        // engine. GetOnPostEngineInit is the UE 5.8 non-deprecated delegate.
        PostEngineInit = FCoreDelegates::GetOnPostEngineInit().AddStatic(&StartCaneSceneRendering);
        EnginePreExit = FCoreDelegates::OnEnginePreExit.AddStatic(&StopCaneSceneRendering);
    }
    virtual void ShutdownModule() override
    {
        FCoreDelegates::GetOnPostEngineInit().Remove(PostEngineInit);
        FCoreDelegates::OnEnginePreExit.Remove(EnginePreExit);
        StopCaneSceneRendering();
    }
    virtual bool SupportsDynamicReloading() override { return false; }
private:
    FDelegateHandle PostEngineInit;
    FDelegateHandle EnginePreExit;
};
IMPLEMENT_MODULE(FCaneRuntimeModule, CaneRuntime)
