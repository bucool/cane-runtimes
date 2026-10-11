#include "CaneBoneAnchorComponent.h"
#include "CaneComponent.h"
UCaneBoneAnchorComponent::UCaneBoneAnchorComponent() { PrimaryComponentTick.bCanEverTick = true; bTickInEditor = true; PrimaryComponentTick.TickGroup = TG_PostPhysics; }
void UCaneBoneAnchorComponent::OnRegister() { Super::OnRegister(); if (Source) { AddTickPrerequisiteComponent(Source); Prerequisite = Source; } }
void UCaneBoneAnchorComponent::OnUnregister() { if (Prerequisite.IsValid()) RemoveTickPrerequisiteComponent(Prerequisite.Get()); Prerequisite.Reset(); Super::OnUnregister(); }
void UCaneBoneAnchorComponent::TickComponent(float Delta, ELevelTick TickType, FActorComponentTickFunction* Tick)
{
    Super::TickComponent(Delta, TickType, Tick);
    if (Prerequisite.Get() != Source) { if (Prerequisite.IsValid()) RemoveTickPrerequisiteComponent(Prerequisite.Get()); Prerequisite = Source; if (Source) AddTickPrerequisiteComponent(Source); }
    FVector Origin;
    BoneAxisX = FVector::ZeroVector; BoneAxisY = FVector::ZeroVector;
    bBoneActive = Source && Source->GetBoneWorldAxes(BoneId, Origin, BoneAxisX, BoneAxisY);
    if (bBoneActive) SetWorldLocation(Origin + BoneAxisX * LocalOffset.X + BoneAxisY * LocalOffset.Y);
}
