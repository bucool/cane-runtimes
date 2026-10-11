#pragma once
#include "CoreMinimal.h"
#include <atomic>

struct FCaneRenderStatus
{
    // Written by queued rendering, read by hosts even while playback is paused.
    std::atomic<int32> ResourceStatus{0}; // 0 pending, 1 ready, -1 resource mismatch
};
