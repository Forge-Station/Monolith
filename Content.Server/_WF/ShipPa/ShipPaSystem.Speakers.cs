using Content.Server.Power.Components;
using Content.Shared._WF.ShipPa;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Power;

namespace Content.Server._WF.ShipPa;

/// <summary>
/// Speaker condition: where a speaker sits, whether it has power, and how wrecked it sounds.
/// </summary>
public sealed partial class ShipPaSystem
{
    /// <summary>Distortion at which the speaker visibly reads as damaged.</summary>
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _gridSpeakers = new();
    private readonly Dictionary<EntityUid, EntityUid> _speakerGrids = new();

    private const float DamagedThreshold = 0.25f;

    /// <summary>
    /// Conservative audible reach for PA download prefetching. Only visits this grid's indexed speakers;
    /// includes currently unpowered/fallback speakers so repairs do not cause a late download.
    /// </summary>
    public float GetMaximumSpeakerRange(EntityUid grid)
    {
        var range = 0f;
        if (!_gridSpeakers.TryGetValue(grid, out var members))
            return range;

        foreach (var uid in members)
        {
            if (TryComp(uid, out ShipPaSpeakerComponent? speaker) && float.IsFinite(speaker.Range))
                range = Math.Max(range, speaker.Range);
        }

        return range;
    }

    private void InitializeSpeakers()
    {
        SubscribeLocalEvent<ShipPaSpeakerComponent, ComponentStartup>(OnSpeakerStartup);
        SubscribeLocalEvent<ShipPaSpeakerComponent, MapInitEvent>(OnSpeakerMapInit);
        SubscribeLocalEvent<ShipPaSpeakerComponent, ComponentShutdown>(OnSpeakerShutdown);
        SubscribeLocalEvent<ShipPaSpeakerComponent, AnchorStateChangedEvent>(OnSpeakerAnchorChanged);
        SubscribeLocalEvent<ShipPaSpeakerComponent, ReAnchorEvent>(OnSpeakerReAnchor);
        SubscribeLocalEvent<ShipPaSpeakerComponent, EntParentChangedMessage>(OnSpeakerParentChanged);
        SubscribeLocalEvent<ShipPaSpeakerComponent, PowerChangedEvent>(OnSpeakerPowerChanged);
        SubscribeLocalEvent<ShipPaSpeakerComponent, DamageChangedEvent>(OnSpeakerDamageChanged);
        SubscribeLocalEvent<ShipPaSpeakerComponent, BreakageEventArgs>(OnSpeakerBreakage);
        SubscribeLocalEvent<ShipPaSpeakerComponent, ExaminedEvent>(OnSpeakerExamined);
    }

    private void OnSpeakerStartup(Entity<ShipPaSpeakerComponent> ent, ref ComponentStartup args)
    {
        IndexSpeaker(ent);
    }

    private void IndexSpeaker(EntityUid uid, bool remove = false)
    {
        if (_speakerGrids.Remove(uid, out var previous))
        {
            if (_gridSpeakers.TryGetValue(previous, out var oldMembers))
            {
                oldMembers.Remove(uid);
                if (oldMembers.Count == 0)
                    _gridSpeakers.Remove(previous);
            }
            QueueRefresh(previous);
        }
        if (remove || !TryComp(uid, out TransformComponent? xform) || !xform.Anchored
            || xform.GridUid is not { } grid || TerminatingOrDeleted(uid))
            return;
        if (!_gridSpeakers.TryGetValue(grid, out var members))
            _gridSpeakers[grid] = members = new HashSet<EntityUid>();
        members.Add(uid);
        _speakerGrids[uid] = grid;
        QueueRefresh(grid);
    }

    private void OnSpeakerMapInit(Entity<ShipPaSpeakerComponent> ent, ref MapInitEvent args)
    {
        IndexSpeaker(ent);
        UpdateDamage(ent);
        UpdateAppearance(ent);
        QueueRefresh(GetSpeakerGrid(ent));
    }

    /// <summary>
    /// Re-indexes a speaker on a grid loaded from a saved copy: the load never raises MapInitEvent, and
    /// the startup index runs before the transform says which grid the speaker ended up on.
    /// </summary>
    public void ReindexSpeaker(Entity<ShipPaSpeakerComponent> ent)
    {
        IndexSpeaker(ent);
        UpdateDamage(ent);
        UpdateAppearance(ent);
        QueueRefresh(GetSpeakerGrid(ent));
    }

    private void OnSpeakerShutdown(Entity<ShipPaSpeakerComponent> ent, ref ComponentShutdown args)
    {
        DisableSpeaker(ent);
        IndexSpeaker(ent, remove: true);
    }

    private void OnSpeakerAnchorChanged(Entity<ShipPaSpeakerComponent> ent, ref AnchorStateChangedEvent args)
    {
        IndexSpeaker(ent);
        // An unanchored speaker is off the network, so nothing of its ship's may keep playing.
        if (!args.Anchored)
            DisableSpeaker(ent);

        UpdateAppearance(ent);
        QueueRefresh(args.Transform.GridUid);
    }

    private void OnSpeakerReAnchor(Entity<ShipPaSpeakerComponent> ent, ref ReAnchorEvent args)
    {
        DisableSpeaker(ent);
        IndexSpeaker(ent);
        QueueRefresh(args.OldGrid);
        QueueRefresh(args.Grid);
    }

    private void OnSpeakerParentChanged(Entity<ShipPaSpeakerComponent> ent, ref EntParentChangedMessage args)
    {
        DisableSpeaker(ent);
        IndexSpeaker(ent);
        QueueRefresh(args.OldParent);
        QueueRefresh(args.Transform.GridUid);
    }

    private void OnSpeakerPowerChanged(Entity<ShipPaSpeakerComponent> ent, ref PowerChangedEvent args)
    {
        // Publish loss immediately; the queued coverage refresh handles coming back online.
        if (!args.Powered)
            DisableSpeaker(ent);

        UpdateAppearance(ent);
        QueueRefresh(GetSpeakerGrid(ent));
    }

    private void OnSpeakerDamageChanged(EntityUid uid, ShipPaSpeakerComponent comp, DamageChangedEvent args)
    {
        UpdateDamage((uid, comp));
    }

    private void OnSpeakerBreakage(EntityUid uid, ShipPaSpeakerComponent comp, BreakageEventArgs args)
    {
        if (comp.Broken)
            return;

        comp.Broken = true;
        Dirty(uid, comp);

        DisableSpeaker((uid, comp));
        UpdateAppearance((uid, comp));
        QueueRefresh(GetSpeakerGrid(uid));
    }

    private void OnSpeakerExamined(Entity<ShipPaSpeakerComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Fallback)
            return;

        var key = ent.Comp.Broken
            ? "ship-pa-speaker-examine-broken"
            : !IsSpeakerPowered(ent)
                ? "ship-pa-speaker-examine-unpowered"
                : ent.Comp.Distortion > 0f
                    ? "ship-pa-speaker-examine-damaged"
                    : "ship-pa-speaker-examine-ok";

        args.PushMarkup(Loc.GetString(key));
    }

    /// <summary>
    /// True when the speaker is getting power, or doesn't want any.
    /// </summary>
    private bool IsSpeakerPowered(EntityUid uid)
    {
        return !TryComp(uid, out ApcPowerReceiverComponent? receiver) || receiver.Powered;
    }

    /// <summary>
    /// Recomputes distortion from damage and clears breakage once the speaker is whole again.
    /// Any repair path ends with the damage set to zero, so this covers welding and field repair alike.
    /// </summary>
    private void UpdateDamage(Entity<ShipPaSpeakerComponent> ent)
    {
        var total = TryComp(ent.Owner, out DamageableComponent? damageable) ? damageable.TotalDamage : FixedPoint2.Zero;
        var limit = ent.Comp.DistortionDamage;
        var distortion = limit <= FixedPoint2.Zero ? 0f : Math.Clamp(total.Float() / limit.Float(), 0f, 1f);

        var changed = false;

        if (Math.Abs(ent.Comp.Distortion - distortion) > 0.001f)
        {
            ent.Comp.Distortion = distortion;
            changed = true;
        }

        if (ent.Comp.Broken && total <= FixedPoint2.Zero)
        {
            ent.Comp.Broken = false;
            changed = true;
            QueueRefresh(GetSpeakerGrid(ent));
        }

        if (!changed)
            return;

        Dirty(ent);
        UpdateAppearance(ent);
    }

    /// <summary>
    /// Pushes the speaker's current state to its sprite.
    /// </summary>
    private void UpdateAppearance(Entity<ShipPaSpeakerComponent> ent)
    {
        if (!TryComp(ent.Owner, out AppearanceComponent? appearance))
            return;

        _appearance.SetData(ent.Owner, ShipPaSpeakerVisuals.State, GetVisualState(ent), appearance);
    }

    private ShipPaSpeakerState GetVisualState(Entity<ShipPaSpeakerComponent> ent)
    {
        if (ent.Comp.Broken)
            return ShipPaSpeakerState.Broken;

        if (!IsFunctional(ent))
            return ShipPaSpeakerState.Unpowered;

        if (ent.Comp.BroadcastingUntil is { } until && _timing.CurTime < until)
            return ShipPaSpeakerState.Broadcasting;

        if (ent.Comp.Distortion >= DamagedThreshold)
            return ShipPaSpeakerState.Damaged;

        return ShipPaSpeakerState.Idle;
    }

    /// <summary>
    /// Drops the broadcasting light once a one-shot has finished playing.
    /// </summary>
    private void ClearExpiredBroadcasts()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ShipPaSpeakerComponent>();

        while (query.MoveNext(out var uid, out var speaker))
        {
            if (speaker.BroadcastingUntil is not { } until || now < until)
                continue;

            speaker.BroadcastingUntil = null;
            UpdateAppearance((uid, speaker));
        }
    }

    private void DisableSpeaker(Entity<ShipPaSpeakerComponent> speaker)
    {
        speaker.Comp.Enabled = false;
        Dirty(speaker);
    }

    private EntityUid? GetSpeakerGrid(EntityUid uid)
    {
        return TryComp(uid, out TransformComponent? xform) ? xform.GridUid : null;
    }
}
