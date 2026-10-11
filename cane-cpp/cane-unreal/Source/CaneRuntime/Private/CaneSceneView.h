#pragma once
#include "CoreMinimal.h"
#include "CaneRenderStatus.h"

class UCaneComponent;
struct FCaneGpuFrame;

struct FCaneSceneHandle : FCaneRenderStatus
{
    // Only the render thread reads/writes Frame. Game thread reads the atomic status.
    TSharedPtr<const FCaneGpuFrame, ESPMode::ThreadSafe> Frame;
};

void StartCaneSceneRendering();
void StopCaneSceneRendering();
void RegisterCaneSceneComponent(UCaneComponent* Component);
void UnregisterCaneSceneComponent(UCaneComponent* Component);
