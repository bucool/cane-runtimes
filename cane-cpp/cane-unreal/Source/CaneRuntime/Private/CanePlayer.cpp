#include "CanePlayer.h"
#include "CaneSkeletonAsset.h"
#include "cane/runtime_player.hpp"
#include <optional>
#include <stdexcept>

struct UCanePlayer::FState
{
    std::unique_ptr<cane::RuntimePlayer> Player;
    std::optional<cane::RuntimeFrame> Frame;
};
namespace
{
uint32_t TrackIndex(int32 Index) { if (Index < 0) throw std::invalid_argument("Track index must be nonnegative."); return static_cast<uint32_t>(Index); }
FCaneEvent ProjectEvent(const cane::RuntimeEvent& Source)
{
    FCaneEvent Event;
    Event.Kind = static_cast<ECaneEventKind>(Source.kind);
    Event.Track = static_cast<int32>(Source.track_index);
    const auto Text = [](const std::optional<std::string>& Value) { return Value ? FString(UTF8_TO_TCHAR(Value->c_str())) : FString(); };
    Event.AnimationId = Text(Source.animation_id); Event.EventId = Text(Source.event_id); Event.Name = Text(Source.name);
    Event.HasTimelineTime = Source.timeline_time_seconds.has_value(); Event.TimelineTime = Source.timeline_time_seconds.value_or(0);
    Event.HasInteger = Source.integer_value.has_value(); Event.Integer = Source.integer_value.value_or(0);
    Event.HasNumber = Source.number_value.has_value(); Event.Number = Source.number_value.value_or(0);
    Event.HasText = Source.string_value.has_value(); Event.Text = Text(Source.string_value); Event.AudioId = Text(Source.audio_id);
    Event.Volume = Source.volume; Event.Balance = Source.balance;
    return Event;
}
}
void UCanePlayer::FStateDeleter::operator()(FState* Value) const { delete Value; }
UCanePlayer::UCanePlayer() : State(new FState()) {}
UCanePlayer::~UCanePlayer() = default;
bool UCanePlayer::IsReady() const { return State && State->Player != nullptr; }
const cane::RuntimeFrame* UCanePlayer::GetNativeFrame() const { check(IsInGameThread()); return State->Frame ? &*State->Frame : nullptr; }
const cane::RuntimePlayer* UCanePlayer::GetNativePlayer() const { check(IsInGameThread()); return State->Player.get(); }
bool UCanePlayer::Fail(const FString& Message)
{
    LastError = Message;
    if (!bReportingError)
    {
        TGuardValue<bool> Busy(bBusy, true);
        TGuardValue<bool> Reporting(bReportingError, true);
        OnError.Broadcast(Message);
    }
    return false;
}
bool UCanePlayer::Initialize(UCaneSkeletonAsset* InAsset)
{
    check(IsInGameThread());
    if (bBusy) return Fail(TEXT("Cannot replace a Cane player during frame/event callbacks."));
    TGuardValue<bool> Guard(bBusy, true);
    if (!InAsset) return Fail(TEXT("A Cane skeleton asset is required."));
    FString Error;
    const auto Data = InAsset->LoadData(Error);
    if (!Data) return Fail(Error);
    try
    {
        auto Candidate = std::make_unique<cane::RuntimePlayer>(*Data);
        auto Frame = Candidate->frame();
        State->Player = std::move(Candidate); State->Frame = std::move(Frame); Asset = InAsset;
        LastError.Reset(); Publish(); return true;
    }
    catch (const std::exception& Exception) { return Fail(UTF8_TO_TCHAR(Exception.what())); }
}
void UCanePlayer::Publish(const cane::EventBatch* Replay)
{
    State->Frame = State->Player->frame();
    const auto Events = State->Player->drain_events();
    OnFrame.Broadcast();
    for (const auto& Event : Events.events()) OnAnimationEvent.Broadcast(ProjectEvent(Event));
    if (Replay) for (const auto& Event : Replay->events()) OnAnimationEvent.Broadcast(ProjectEvent(Event));
}
bool UCanePlayer::Run(TFunctionRef<void(cane::RuntimePlayer&)> Edit, bool bApply)
{
    check(IsInGameThread());
    if (bBusy) return Fail(TEXT("Cane callbacks cannot mutate the player. Schedule the change for the next game tick."));
    if (!IsReady()) return Fail(TEXT("Initialize the Cane player first."));
    TGuardValue<bool> Guard(bBusy, true);
    try
    {
        Edit(*State->Player);
        if (bApply) static_cast<void>(State->Player->apply());
        LastError.Reset(); Publish(); return true;
    }
    catch (const std::exception& Exception) { return Fail(UTF8_TO_TCHAR(Exception.what())); }
}
bool UCanePlayer::EditNative(TFunctionRef<void(cane::RuntimePlayer&)> Edit) { return Run(Edit, true); }
bool UCanePlayer::Advance(float Seconds) { return Run([&](auto& P) { static_cast<void>(P.advance(Seconds)); }, false); }
bool UCanePlayer::Play(const FString& Id, int32 Track, bool Loop, float Mix) { return Run([&](auto& P) { P.play_animation(TrackIndex(Track), TCHAR_TO_UTF8(*Id), Loop, Mix); }, true); }
bool UCanePlayer::Queue(const FString& Id, int32 Track, bool Loop, float Delay, float Mix) { return Run([&](auto& P) { P.queue_animation(TrackIndex(Track), TCHAR_TO_UTF8(*Id), Loop, Delay, Mix); }, true); }
bool UCanePlayer::Stop(int32 Track) { return Run([&](auto& P) { P.stop_track(TrackIndex(Track)); }, true); }
bool UCanePlayer::Fade(int32 Track, float Seconds) { return Run([&](auto& P) { P.fade_out_track(TrackIndex(Track), Seconds); }, true); }
bool UCanePlayer::Reset() { return Run([](auto& P) { static_cast<void>(P.reset()); }, false); }
bool UCanePlayer::Seek(float Seconds, float Step)
{
    check(IsInGameThread());
    if (bBusy) return Fail(TEXT("Cane callbacks cannot seek the player."));
    if (!IsReady()) return Fail(TEXT("Initialize the Cane player first."));
    TGuardValue<bool> Guard(bBusy, true);
    try { const auto Result = State->Player->seek(Seconds, Step); LastError.Reset(); Publish(&Result.events); return true; }
    catch (const std::exception& Exception) { return Fail(UTF8_TO_TCHAR(Exception.what())); }
}
bool UCanePlayer::SetTrackOptions(int32 Track, float Weight, float Rate, bool Loop)
{
    return Run([&](auto& P) { cane::TrackOptions O; O.blend_weight = Weight; O.playback_rate = Rate; O.looping = Loop; P.set_track_options(TrackIndex(Track), O); }, true);
}
bool UCanePlayer::SetSkins(const TArray<FString>& Ids)
{
    return Run([&](auto& P) { std::vector<std::string> Values; for (const auto& Id : Ids) Values.emplace_back(TCHAR_TO_UTF8(*Id)); P.set_skins(Values); }, true);
}
bool UCanePlayer::SetAttachment(const FString& Slot, const FString& Id)
{
    return Run([&](auto& P) { const std::string Attachment(TCHAR_TO_UTF8(*Id)); P.set_slot_attachment_override(TCHAR_TO_UTF8(*Slot), Id.IsEmpty() ? std::nullopt : std::optional<std::string_view>(Attachment)); }, true);
}
bool UCanePlayer::RestoreAttachment(const FString& Slot) { return Run([&](auto& P) { P.clear_slot_attachment_override(TCHAR_TO_UTF8(*Slot)); }, true); }
bool UCanePlayer::SetRoot(FVector2D Position, float Angle, FVector2D Scale, bool Teleport)
{
    return Run([&](auto& P) { cane::RegionLocal R; R.x = static_cast<float>(Position.X); R.y = static_cast<float>(Position.Y); R.rotation_degrees = Angle; R.scale_x = static_cast<float>(Scale.X); R.scale_y = static_cast<float>(Scale.Y); P.set_root_transform(R, Teleport ? cane::PhysicsHostMotion::teleport : cane::PhysicsHostMotion::move); }, true);
}
bool UCanePlayer::ResetPhysics() { return Run([](auto& P) { P.reset_physics(); }, true); }
bool UCanePlayer::GetBone(const FString& Id, FCaneBonePose& Pose)
{
    check(IsInGameThread()); Pose = {};
    if (!IsReady()) return Fail(TEXT("Initialize the Cane player first."));
    try { const auto B = State->Frame->bone(TCHAR_TO_UTF8(*Id)); Pose.BoneId = Id; Pose.Active = B.active;
        Pose.Origin = FVector2D(B.matrix.tx, B.matrix.ty); Pose.AxisX = FVector2D(B.matrix.a, B.matrix.b); Pose.AxisY = FVector2D(B.matrix.c, B.matrix.d); return true; }
    catch (const std::exception& Exception) { return Fail(UTF8_TO_TCHAR(Exception.what())); }
}
bool UCanePlayer::GetTrack(int32 Track, FCaneTrack& Result)
{
    check(IsInGameThread()); Result = {};
    if (!IsReady()) return Fail(TEXT("Initialize the Cane player first."));
    try { const auto T = State->Player->query_track(TrackIndex(Track)); if (!T) return false;
        Result.AnimationId = T->animation_id ? UTF8_TO_TCHAR(T->animation_id->c_str()) : TEXT("");
        Result.ElapsedSeconds = T->playback_time; Result.AnimationSeconds = T->animation_time;
        Result.Weight = T->blend_weight; Result.Rate = T->playback_rate; Result.Looping = T->looping;
        Result.QueuedEntries = static_cast<int32>(T->queued_entry_count); return true; }
    catch (const std::exception& Exception) { return Fail(UTF8_TO_TCHAR(Exception.what())); }
}
