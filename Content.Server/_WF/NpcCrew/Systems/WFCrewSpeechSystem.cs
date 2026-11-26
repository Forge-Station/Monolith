using Content.Server._WF.NpcCrew.Components;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Short local action reports with per-speaker and per-line cooldowns.</summary>
public sealed partial class WFCrewSpeechSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Whether crew report their actions aloud.</summary>
    public bool Enabled;

    /// <summary>Says an action at most once per minute, with fifteen seconds between any two lines.</summary>
    public bool Say(EntityUid uid, string action)
    {
        // TODO: voicelines. Until then crew say nothing aloud about what they are doing.
        if (!Enabled)
            return false;

        if (!TryComp<WFCrewComponent>(uid, out var crew) || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid)
            || _timing.CurTime < crew.NextSpeech
            || crew.SpokenActions.TryGetValue(action, out var last) && _timing.CurTime < last + TimeSpan.FromSeconds(60))
            return false;
        crew.NextSpeech = _timing.CurTime + TimeSpan.FromSeconds(15);
        crew.SpokenActions[action] = _timing.CurTime;
        _chat.TrySendInGameICMessage(uid, Loc.GetString($"wf-crew-action-{action}"), InGameICChatType.Speak,
            hideChat: false, checkRadioPrefix: false);
        return true;
    }
}
