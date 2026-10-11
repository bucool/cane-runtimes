#pragma once
#include "CoreMinimal.h"
#include "CaneTypes.generated.h"

// Presentation choice, independent of texture encoding and Runtime packet data.
UENUM(BlueprintType)
enum class ECaneBlendColorSpace : uint8 { Gamma, Linear };

UENUM(BlueprintType)
enum class ECaneEventKind : uint8 { Started, Interrupted, Ended, Removed, Completed, Custom };

USTRUCT(BlueprintType)
struct CANERUNTIME_API FCaneEvent
{
    GENERATED_BODY()
    UPROPERTY(BlueprintReadOnly, Category="Cane") ECaneEventKind Kind = ECaneEventKind::Custom;
    UPROPERTY(BlueprintReadOnly, Category="Cane") int32 Track = 0;
    UPROPERTY(BlueprintReadOnly, Category="Cane") FString AnimationId;
    UPROPERTY(BlueprintReadOnly, Category="Cane") FString EventId;
    UPROPERTY(BlueprintReadOnly, Category="Cane") FString Name;
    UPROPERTY(BlueprintReadOnly, Category="Cane") bool HasTimelineTime = false;
    UPROPERTY(BlueprintReadOnly, Category="Cane") float TimelineTime = 0;
    UPROPERTY(BlueprintReadOnly, Category="Cane") bool HasInteger = false;
    UPROPERTY(BlueprintReadOnly, Category="Cane") int64 Integer = 0;
    UPROPERTY(BlueprintReadOnly, Category="Cane") bool HasNumber = false;
    UPROPERTY(BlueprintReadOnly, Category="Cane") float Number = 0;
    UPROPERTY(BlueprintReadOnly, Category="Cane") bool HasText = false;
    UPROPERTY(BlueprintReadOnly, Category="Cane") FString Text;
    UPROPERTY(BlueprintReadOnly, Category="Cane") FString AudioId;
    UPROPERTY(BlueprintReadOnly, Category="Cane") float Volume = 1;
    UPROPERTY(BlueprintReadOnly, Category="Cane") float Balance = 0;
};

// A full affine is kept because an FTransform cannot represent skeletal shear.
USTRUCT(BlueprintType)
struct CANERUNTIME_API FCaneBonePose
{
    GENERATED_BODY()
    UPROPERTY(BlueprintReadOnly, Category="Cane") FString BoneId;
    UPROPERTY(BlueprintReadOnly, Category="Cane") bool Active = false;
    UPROPERTY(BlueprintReadOnly, Category="Cane") FVector2D Origin = FVector2D::ZeroVector;
    UPROPERTY(BlueprintReadOnly, Category="Cane") FVector2D AxisX = FVector2D(1, 0);
    UPROPERTY(BlueprintReadOnly, Category="Cane") FVector2D AxisY = FVector2D(0, 1);
};

USTRUCT(BlueprintType)
struct CANERUNTIME_API FCaneTrack
{
    GENERATED_BODY()
    UPROPERTY(BlueprintReadOnly, Category="Cane") FString AnimationId;
    UPROPERTY(BlueprintReadOnly, Category="Cane") float ElapsedSeconds = 0;
    UPROPERTY(BlueprintReadOnly, Category="Cane") float AnimationSeconds = 0;
    UPROPERTY(BlueprintReadOnly, Category="Cane") float Weight = 1;
    UPROPERTY(BlueprintReadOnly, Category="Cane") float Rate = 1;
    UPROPERTY(BlueprintReadOnly, Category="Cane") bool Looping = false;
    UPROPERTY(BlueprintReadOnly, Category="Cane") int32 QueuedEntries = 0;
};

DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FCaneAnimationEvent, const FCaneEvent&, Event);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FCaneErrorEvent, const FString&, Message);
DECLARE_DYNAMIC_MULTICAST_DELEGATE(FCaneFrameEvent);
