#pragma once
#include "CoreMinimal.h"
#include "UObject/Object.h"
#include "CaneTypes.h"
#include <memory>
#include "CanePlayer.generated.h"

class UCaneSkeletonAsset;
namespace cane { class RuntimePlayer; class RuntimeFrame; class EventBatch; }

UCLASS(BlueprintType)
class CANERUNTIME_API UCanePlayer : public UObject
{
    GENERATED_BODY()
public:
    UCanePlayer();
    virtual ~UCanePlayer() override;
    UPROPERTY(BlueprintAssignable, Category="Cane") FCaneAnimationEvent OnAnimationEvent;
    UPROPERTY(BlueprintAssignable, Category="Cane") FCaneErrorEvent OnError;
    UPROPERTY(BlueprintAssignable, Category="Cane") FCaneFrameEvent OnFrame;
    UPROPERTY(BlueprintReadOnly, Category="Cane") FString LastError;
    UPROPERTY(BlueprintReadOnly, Category="Cane") TObjectPtr<UCaneSkeletonAsset> Asset;
    UFUNCTION(BlueprintCallable, Category="Cane") bool Initialize(UCaneSkeletonAsset* InAsset);
    UFUNCTION(BlueprintCallable, Category="Cane") bool Advance(float DeltaSeconds);
    UFUNCTION(BlueprintCallable, Category="Cane") bool Play(const FString& AnimationId, int32 Track = 0, bool Loop = true, float MixSeconds = 0.2f);
    UFUNCTION(BlueprintCallable, Category="Cane") bool Queue(const FString& AnimationId, int32 Track, bool Loop, float DelaySeconds, float MixSeconds = 0.2f);
    UFUNCTION(BlueprintCallable, Category="Cane") bool Stop(int32 Track = 0);
    UFUNCTION(BlueprintCallable, Category="Cane") bool Fade(int32 Track, float Seconds);
    UFUNCTION(BlueprintCallable, Category="Cane") bool Reset();
    UFUNCTION(BlueprintCallable, Category="Cane") bool Seek(float Seconds, float FixedStep = 0.016666667f);
    UFUNCTION(BlueprintCallable, Category="Cane") bool SetTrackOptions(int32 Track, float Weight, float Rate, bool Loop);
    UFUNCTION(BlueprintCallable, Category="Cane") bool SetSkins(const TArray<FString>& SkinIds);
    UFUNCTION(BlueprintCallable, Category="Cane") bool SetAttachment(const FString& SlotId, const FString& AttachmentId);
    UFUNCTION(BlueprintCallable, Category="Cane") bool RestoreAttachment(const FString& SlotId);
    UFUNCTION(BlueprintCallable, Category="Cane") bool SetRoot(FVector2D Position, float Angle, FVector2D Scale, bool Teleport = false);
    UFUNCTION(BlueprintCallable, Category="Cane") bool ResetPhysics();
    UFUNCTION(BlueprintCallable, Category="Cane") bool GetBone(const FString& BoneId, FCaneBonePose& Pose);
    UFUNCTION(BlueprintCallable, Category="Cane") bool GetTrack(int32 Track, FCaneTrack& State);
    UFUNCTION(BlueprintPure, Category="Cane") bool IsReady() const;
    // Advanced controls stay in Core. Callback runs on game thread, cannot escape the
    // player reference, and must not call back into this UObject. One publish afterward.
    bool EditNative(TFunctionRef<void(cane::RuntimePlayer&)> Edit);
    const cane::RuntimeFrame* GetNativeFrame() const;
    const cane::RuntimePlayer* GetNativePlayer() const;
private:
    struct FState;
    // Generated UObject constructors also need to destroy this pointer on failure.
    // Keep deletion out of line so those translation units need no Core internals.
    struct FStateDeleter { void operator()(FState* Value) const; };
    std::unique_ptr<FState, FStateDeleter> State;
    bool bBusy = false;
    bool bReportingError = false;
    bool Fail(const FString& Message);
    bool Run(TFunctionRef<void(cane::RuntimePlayer&)> Edit, bool bApply);
    void Publish(const cane::EventBatch* Replay = nullptr);
};
