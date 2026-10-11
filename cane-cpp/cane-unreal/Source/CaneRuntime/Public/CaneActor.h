#pragma once
#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "CaneActor.generated.h"
class UCaneComponent;
UCLASS(Blueprintable)
class CANERUNTIME_API ACaneActor : public AActor
{
    GENERATED_BODY()
public:
    ACaneActor();
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category="Cane") TObjectPtr<UCaneComponent> Cane;
};
