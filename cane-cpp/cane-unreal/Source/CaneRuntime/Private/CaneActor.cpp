#include "CaneActor.h"
#include "CaneComponent.h"
ACaneActor::ACaneActor() { Cane = CreateDefaultSubobject<UCaneComponent>(TEXT("Cane")); SetRootComponent(Cane); }
