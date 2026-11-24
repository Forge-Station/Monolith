using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.Bed.Sleep;
using Content.Shared.Chat;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>
/// Nodding off on a heavy opioid high: every so often the eyes shut for a few seconds while the body stays up and
/// conscious, drifting at a crawl. The client closes the screen and muffles sound; others sometimes see it in chat.
/// An overdose nods more often and for longer, on top of its real sleep. A neuroware host running an opioid program
/// stalls the same way.
/// </summary>
public sealed class WFNodSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFIntoxicationSystem _intoxication = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>Chance a nod is shown to others as an emote.</summary>
    public const float EmoteChance = 0.35f;

    private const int WakeLines = 3;
    private const int EmoteLines = 3;

    private readonly record struct NodPace(Vector2 Interval, Vector2 Duration);

    private static readonly NodPace Heavy = new(new Vector2(30f, 90f), new Vector2(1.5f, 3f));
    private static readonly NodPace Overdose = new(new Vector2(15f, 30f), new Vector2(2.5f, 4f));

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFIntoxicationComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            Tick((uid, comp), now);
        }
    }

    /// <summary>Ends a nod that is over, and starts one that is due.</summary>
    public void Tick(Entity<WFIntoxicationComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (comp.NodEnd != TimeSpan.Zero && now >= comp.NodEnd)
            Wake(ent);

        if (comp.NodEnd != TimeSpan.Zero)
            return;

        NodPace pace;
        switch (_intoxication.FamilyPhase(comp, WFDrugFamily.Opioid))
        {
            case WFDrugPhase.Heavy:
                pace = Heavy;
                break;
            case WFDrugPhase.Overdose:
                pace = Overdose;
                break;
            default:
                comp.NextNod = TimeSpan.Zero;
                return;
        }

        if (comp.NextNod == TimeSpan.Zero)
        {
            comp.NextNod = now + Seconds(pace.Interval);
            return;
        }

        if (now < comp.NextNod)
            return;

        if (!_mobState.IsAlive(ent) || HasComp<SleepingComponent>(ent) || HasComp<ForcedSleepingComponent>(ent))
        {
            comp.NextNod = now + Seconds(pace.Interval);
            return;
        }

        comp.NodStart = now;
        comp.NodEnd = now + Seconds(pace.Duration);
        comp.NextNod = comp.NodEnd + Seconds(pace.Interval);
        Dirty(ent);
        _movement.RefreshMovementSpeedModifiers(ent);

        if (_random.Prob(EmoteChance))
        {
            _chat.TrySendInGameICMessage(ent, Loc.GetString($"{Prefix(ent)}emote-{_random.Next(1, EmoteLines + 1)}"),
                InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
        }
    }

    private void Wake(Entity<WFIntoxicationComponent> ent)
    {
        ent.Comp.NodStart = TimeSpan.Zero;
        ent.Comp.NodEnd = TimeSpan.Zero;
        Dirty(ent);
        _movement.RefreshMovementSpeedModifiers(ent);
        _popup.PopupEntity(Loc.GetString($"{Prefix(ent)}wake-{_random.Next(1, WakeLines + 1)}"), ent, ent);
    }

    /// <summary>A neuroware host's nod is a stall in standby, and reads like one.</summary>
    private string Prefix(EntityUid uid)
    {
        return _intoxication.IsNeurowareHost(uid) ? "wf-nod-host-" : "wf-nod-";
    }

    private TimeSpan Seconds(Vector2 range)
    {
        return TimeSpan.FromSeconds(_random.NextFloat(range.X, range.Y));
    }
}
