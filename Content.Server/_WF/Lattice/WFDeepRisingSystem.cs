using Content.Server._WF.Remade.Betrayal;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.EUI;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Lattice;

/// <summary>
/// In the Deep the dead don't stay down: a body lying there long enough turns Remade, unless its ship or the body is
/// sheltered. A player's body offers them the risen Remade; if they say no, or nobody owned it, it is an NPC.
/// </summary>
public sealed class WFDeepRisingSystem : EntitySystem
{
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFLatticeSystem _lattice = default!;
    [Dependency] private WFRemadeConversionSystem _conversion = default!;

    private static readonly TimeSpan ScanRate = TimeSpan.FromSeconds(5);

    /// <summary>When each body was first seen dead in a rising band.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _lying = new();

    /// <summary>Bodies set turning, with the mind they belonged to and whether that player is asked first.</summary>
    private readonly Dictionary<EntityUid, (EntityUid? Mind, bool Ask)> _turning = new();

    private readonly HashSet<EntityUid> _seen = new();
    private TimeSpan _nextScan;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeRisenEvent>(OnRisen);
        SubscribeLocalEvent<Content.Shared.GameTicking.RoundRestartCleanupEvent>(_ =>
        {
            _lying.Clear();
            _turning.Clear();
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextScan)
            return;

        _nextScan = now + ScanRate;
        _seen.Clear();
        var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var state, out var xform))
        {
            if (state.CurrentState != MobState.Dead || _turning.ContainsKey(uid) || !Rises(uid, xform))
                continue;

            _seen.Add(uid);
            if (!_lying.TryGetValue(uid, out var since))
            {
                _lying[uid] = now;
                continue;
            }

            if (_lattice.BandOf(_transform.GetMapCoordinates(uid, xform), out var profile) == null || profile == null
                || now - since < TimeSpan.FromSeconds(profile.RiseDelay))
                continue;

            var mind = FindMind(uid);
            if (_conversion.TryStartConversion(uid))
                _turning[uid] = (mind, true);
        }

        // Bodies moved out, sheltered, revived or gone start over.
        foreach (var uid in new List<EntityUid>(_lying.Keys))
        {
            if (!_seen.Contains(uid))
                _lying.Remove(uid);
        }

        // A conversion that stopped without rising (the body revived or moved off) drops its claim.
        foreach (var uid in new List<EntityUid>(_turning.Keys))
        {
            if (TerminatingOrDeleted(uid) || !HasComp<WFRemadeConvertingComponent>(uid))
                _turning.Remove(uid);
        }
    }

    /// <summary>Whether a dead body here would rise: in a rising band, on no sheltered ship, wearing no shelter.</summary>
    private bool Rises(EntityUid uid, TransformComponent xform)
    {
        if (_lattice.BandOf(_transform.GetMapCoordinates(uid, xform), out var profile) == null || profile == null)
            return false;

        if (_lattice.Locate(xform.MapUid!.Value, _transform.GetWorldPosition(xform)).Band < profile.RiseBand)
            return false;

        if (xform.GridUid is { } grid && TryComp<WFLatticeShelterComponent>(grid, out var ship) && ship.StopsTaking)
            return false;

        return !(TryComp<WFLatticeShelterComponent>(uid, out var worn) && worn.StopsTaking) && _conversion.CanConvert(uid);
    }

    /// <summary>
    /// Sets a body turning, and gives its player the Remade it rises as: straight away, or only if they say yes when
    /// <paramref name="ask"/> is set. False if the body can't turn.
    /// </summary>
    public bool Raise(EntityUid body, bool ask)
    {
        if (_turning.ContainsKey(body))
            return true;

        var mind = FindMind(body);
        if (!_conversion.TryStartConversion(body))
            return false;

        _turning[body] = (mind, ask);
        return true;
    }

    /// <summary>The player mind a body belonged to: still in it, or out in its ghost.</summary>
    public EntityUid? FindMind(EntityUid body)
    {
        if (_mind.TryGetMind(body, out var inside, out var mind) && mind.UserId != null)
            return inside;

        var net = GetNetEntity(body);
        var minds = EntityQueryEnumerator<MindComponent>();
        while (minds.MoveNext(out var uid, out var candidate))
        {
            if (candidate.OriginalOwnedEntity == net && candidate.UserId != null
                && (candidate.OwnedEntity == null || HasComp<GhostComponent>(candidate.OwnedEntity)))
                return uid;
        }

        return null;
    }

    private void OnRisen(ref WFRemadeRisenEvent ev)
    {
        if (!_turning.Remove(ev.Corpse, out var claim) || claim.Mind is not { } owner
            || !TryComp<MindComponent>(owner, out var mind) || mind.UserId is not { } user
            || !_players.TryGetSessionById(user, out var session))
            return;

        if (claim.Ask)
        {
            Offer(session, owner, ev.Remade);
            return;
        }

        _mind.TransferTo(owner, ev.Remade, mind: mind);
        _popup.PopupEntity(Loc.GetString("wf-deep-rise-joined"), ev.Remade, ev.Remade, PopupType.LargeCaution);
    }

    /// <summary>Asks the body's player whether they rise with it. A no, or no answer, leaves it to the Unmooring.</summary>
    private void Offer(ICommonSession session, EntityUid mindId, EntityUid remade)
    {
        var eui = new WolfmedChoiceEui(new WolfmedChoiceEuiState(
            Loc.GetString("wf-deep-rise-title"),
            Loc.GetString("wf-deep-rise-text"),
            Loc.GetString("wf-deep-rise-accept"),
            Loc.GetString("wf-deep-rise-deny")), accepted =>
        {
            if (!accepted || TerminatingOrDeleted(remade) || !TryComp<MindComponent>(mindId, out var mind)
                || mind.OwnedEntity is { } owned && !HasComp<GhostComponent>(owned))
                return;

            _mind.TransferTo(mindId, remade, mind: mind);
            _popup.PopupEntity(Loc.GetString("wf-deep-rise-joined"), remade, remade, PopupType.LargeCaution);
        });

        _eui.OpenEui(eui, session);
    }
}
