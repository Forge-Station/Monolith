using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._HL.Silicons.Synths.Battery;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Synthetics;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Synthetics;

public sealed partial class WFChassisPanelSystem
{
    /// <summary>How long a reboot holds the frame down.</summary>
    public static readonly TimeSpan RebootTime = TimeSpan.FromSeconds(4);

    /// <summary>Share of the cell's capacity a reboot spends.</summary>
    public const float RebootCost = 0.1f;

    /// <summary>How often overclocked servos drain and heat.</summary>
    public static readonly TimeSpan OverclockTick = TimeSpan.FromSeconds(5);

    /// <summary>Extra drain while overclocked, as a multiple of the chassis's normal draw.</summary>
    public const float OverclockDrawMultiplier = 3f;

    /// <summary>Extra drain per tick, as a share of capacity, for a chassis with no draw rate to multiply.</summary>
    public const float OverclockFallbackDrain = 0.02f;

    /// <summary>Overheating added to each leg and the torso per tick.</summary>
    public static readonly FixedPoint2 OverclockHeat = FixedPoint2.New(4);

    /// <summary>Charge below which overclocking stops, or will not start.</summary>
    public const float OverclockMinCharge = 0.15f;

    /// <summary>Torso overheating at which overclocking stops: the wound's Overheated stage.</summary>
    public static readonly FixedPoint2 OverclockHeatLimit = FixedPoint2.New(45);

    public static readonly ProtoId<WoundPrototype> OverheatingWound = "WFWolfmedOverheatingWound";

    /// <summary>Chance per overclock tick of sparks with a cold core; it rises with the heat.</summary>
    public const float OverclockSparkChance = 0.15f;

    /// <summary>Torso overheating from which every overclock tick sparks.</summary>
    public static readonly FixedPoint2 OverclockSparkCertain = FixedPoint2.New(35);

    /// <summary>Torso overheating from which a tick can short a limb.</summary>
    public static readonly FixedPoint2 OverclockShortFrom = FixedPoint2.New(20);

    /// <summary>Heat past <see cref="OverclockShortFrom"/> at which a short is certain: the cut-off.</summary>
    public const float OverclockShortSpan = 25f;

    /// <summary>Severity of the short-circuit wound an overheated tick puts on a limb.</summary>
    public static readonly FixedPoint2 OverclockShortSeverity = FixedPoint2.New(8);

    /// <summary>Torso overheating from which each tick cooks the torso.</summary>
    public static readonly FixedPoint2 OverclockCookFrom = FixedPoint2.New(35);

    /// <summary>Heat damage to the torso per tick past <see cref="OverclockCookFrom"/>.</summary>
    public static readonly FixedPoint2 OverclockCookDamage = FixedPoint2.New(3);

    public static readonly ProtoId<WoundPrototype> ShortCircuitWound = "WFWolfmedShortCircuitWound";
    public static readonly EntProtoId SparkEffect = "EffectSparks";
    private static readonly ProtoId<DamageTypePrototype> CookDamageType = "Heat";

    private static readonly SoundSpecifier DetachSound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");
    private static readonly SoundSpecifier RebootStartSound = new SoundPathSpecifier("/Audio/Machines/quickbeep.ogg");
    private static readonly SoundSpecifier RebootDoneSound = new SoundPathSpecifier("/Audio/Machines/twobeep.ogg");
    private static readonly SoundSpecifier OverclockSound = new SoundPathSpecifier("/Audio/Machines/beep.ogg");
    private static readonly SoundSpecifier SparkSound = new SoundCollectionSpecifier("sparks");

    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WoundDamageRoutingSystem _damageRouting = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private WolfmedBodySystem _wolfmedBody = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private void OnIsolateMessage(Entity<WFChassisComponent> ent, ref WFChassisIsolatePartMessage args)
    {
        if (TryGetEntity(args.Part, out var part))
            SetIsolated(ent, part.Value, args.Isolate, args.Actor);
    }

    private void OnDetachMessage(Entity<WFChassisComponent> ent, ref WFChassisDetachPartMessage args)
    {
        if (TryGetEntity(args.Part, out var part))
            TryDetach(ent, part.Value, args.Actor);
    }

    private void OnRebootMessage(Entity<WFChassisComponent> ent, ref WFChassisRebootMessage args)
    {
        TryReboot(ent, args.Actor);
    }

    private void OnOverclockMessage(Entity<WFChassisComponent> ent, ref WFChassisOverclockMessage args)
    {
        SetOverclock(ent, args.On, args.Actor);
    }

    /// <summary>Isolates or restores one of this body's limbs from the panel.</summary>
    public bool SetIsolated(EntityUid body, EntityUid part, bool isolate, EntityUid user)
    {
        if (CompOrNull<BodyPartComponent>(part)?.Body != body)
            return false;

        if (!_isolation.TrySetUserIsolated(part, isolate, out var deferred))
        {
            if (isolate)
                _popup.PopupEntity(Loc.GetString("wf-chassis-isolate-refuse"), body, user);
            return false;
        }

        // Low power has the limb cut already: the choice is kept for when power returns.
        var done = isolate ? "wf-chassis-isolate-done" : "wf-chassis-restore-done";
        _popup.PopupEntity(Loc.GetString(deferred && !isolate ? "wf-chassis-restore-low-power" : done,
            ("part", part)), body, user);
        PushState(body);
        return true;
    }

    /// <summary>Releases an attached limb's coupling: it comes off whole, with no stump.</summary>
    public bool TryDetach(EntityUid body, EntityUid part, EntityUid user)
    {
        if (!TryComp(part, out BodyPartComponent? bodyPart) || bodyPart.Body != body ||
            !WFChassisIsolationSystem.CanIsolate(bodyPart))
        {
            _popup.PopupEntity(Loc.GetString("wf-chassis-detach-refuse"), body, user);
            return false;
        }

        // An isolated limb (or hand or foot under it) is restored first, so the marker never leaves on the limb.
        foreach (var (child, _) in _body.GetBodyPartChildren(part, bodyPart))
            _isolation.TryRestore(child);

        if (!_wolfmedBody.TryDetachPart(part))
        {
            _popup.PopupEntity(Loc.GetString("wf-chassis-detach-refuse"), body, user);
            return false;
        }

        _audio.PlayPvs(DetachSound, body);
        _popup.PopupEntity(Loc.GetString("wf-chassis-detach-done", ("part", part), ("target", body)), body, user);
        if (user != body)
            _popup.PopupEntity(Loc.GetString("wf-chassis-detach-done-owner", ("part", part)), body, body,
                PopupType.MediumCaution);

        PushState(body);
        return true;
    }

    /// <summary>Takes the frame down for a few seconds; on completion its sensor load and lockups are cleared.</summary>
    public bool TryReboot(EntityUid body, EntityUid user)
    {
        if (_shutdown.IsShutDown(body))
        {
            _popup.PopupEntity(Loc.GetString("wf-chassis-reboot-refuse-shutdown"), body, user);
            return false;
        }

        if (HasComp<WFChassisRebootComponent>(body))
        {
            _popup.PopupEntity(Loc.GetString("wf-chassis-reboot-refuse-busy"), body, user);
            return false;
        }

        if (TryComp(body, out WFChassisComponent? chassis) && chassis.Overclocked)
            SetOverclock(body, false, user, quiet: true);

        if (TryGetCell(body, out var cell))
            _battery.ChangeCharge(cell.Value.Owner, -cell.Value.Comp.MaxCharge * RebootCost, cell.Value.Comp);

        EnsureComp<WFChassisRebootComponent>(body).EndTime = _timing.CurTime + RebootTime;
        _stun.TryParalyze(body, RebootTime, true);
        _audio.PlayPvs(RebootStartSound, body);
        _popup.PopupEntity(Loc.GetString("wf-chassis-reboot-start", ("target", body)), body, PopupType.Medium);
        PushState(body);
        return true;
    }

    private void UpdateReboots(TimeSpan now)
    {
        var done = new List<EntityUid>();
        var query = EntityQueryEnumerator<WFChassisRebootComponent>();
        while (query.MoveNext(out var uid, out var reboot))
        {
            if (now >= reboot.EndTime)
                done.Add(uid);
        }

        foreach (var uid in done)
            FinishReboot(uid);
    }

    /// <summary>Every part's sensor reading is cleared, and any lockup ends.</summary>
    public void FinishReboot(EntityUid body)
    {
        RemComp<WFChassisRebootComponent>(body);
        if (TerminatingOrDeleted(body))
            return;

        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            _isolation.ClearRememberedPain(part);
            if (!TryComp(part, out PainComponent? pain))
                continue;

            // The reading drops to nothing; the wounds keep their floor, so fresh damage and worsening wounds build
            // it back up.
            _pain.SetPain((part, pain), FixedPoint2.Zero);
            _pain.RefreshWoundPain(part);
        }

        _statusEffects.TryRemoveStatusEffect(body, "Stun");
        _statusEffects.TryRemoveStatusEffect(body, "KnockedDown");
        _audio.PlayPvs(RebootDoneSound, body);
        _popup.PopupEntity(Loc.GetString("wf-chassis-reboot-done"), body, body);
        PushState(body);
    }

    /// <summary>Overclocks the servos, or runs them at rating again.</summary>
    public bool SetOverclock(EntityUid body, bool on, EntityUid user, bool quiet = false)
    {
        if (!TryComp(body, out WFChassisComponent? chassis) || chassis.Overclocked == on)
            return false;

        if (on && OverclockBlocker(body) is { } refusal)
        {
            _popup.PopupEntity(Loc.GetString(refusal), body, user);
            return false;
        }

        chassis.Overclocked = on;
        Dirty(body, chassis);

        if (on)
            EnsureComp<WFChassisOverclockComponent>(body).NextTick = _timing.CurTime + OverclockTick;
        else
            RemComp<WFChassisOverclockComponent>(body);

        _speed.RefreshMovementSpeedModifiers(body);
        if (!quiet)
        {
            _audio.PlayPvs(OverclockSound, body);
            _popup.PopupEntity(Loc.GetString(on ? "wf-chassis-overclock-on" : "wf-chassis-overclock-off"), body, user);
        }

        PushState(body);
        return true;
    }

    /// <summary>Why the servos can't (or can't stay) overclocked right now, as a loc key; null when they can.</summary>
    private string? OverclockBlocker(EntityUid body)
    {
        if (_shutdown.IsShutDown(body))
            return "wf-chassis-overclock-refuse-shutdown";

        if (HasComp<WFChassisRebootComponent>(body))
            return "wf-chassis-reboot-refuse-busy";

        if (HasComp<WFLowPowerComponent>(body))
            return "wf-chassis-overclock-refuse-low-power";

        if (TryGetCell(body, out var cell) && ChargeFraction(cell.Value.Comp) < OverclockMinCharge)
            return "wf-chassis-overclock-refuse-charge";

        if (TorsoHeat(body) >= OverclockHeatLimit)
            return "wf-chassis-overclock-refuse-heat";

        return null;
    }

    private void UpdateOverclock(TimeSpan now)
    {
        var stop = new List<(EntityUid Body, string Reason)>();
        var query = EntityQueryEnumerator<WFChassisOverclockComponent, WFChassisComponent>();
        while (query.MoveNext(out var uid, out var overclock, out _))
        {
            if (now >= overclock.NextTick)
            {
                overclock.NextTick = now + OverclockTick;
                Strain(uid);
            }

            if (OverclockBlocker(uid) is { } reason)
                stop.Add((uid, reason));
        }

        foreach (var (body, reason) in stop)
        {
            SetOverclock(body, false, body, quiet: true);
            _popup.PopupEntity(Loc.GetString("wf-chassis-overclock-auto-off", ("reason", Loc.GetString(reason))),
                body, body, PopupType.MediumCaution);
        }
    }

    /// <summary>One overclock tick: extra drain on the cell, heat in the legs and torso, and what the heat does.</summary>
    private void Strain(EntityUid body)
    {
        if (TryGetCell(body, out var cell))
        {
            var draw = CompOrNull<SynthBatteryComponent>(body)?.DrawRate ??
                       CompOrNull<SiliconComponent>(body)?.DrainPerSecond;
            var drain = draw is { } rate && rate > 0f
                ? rate * OverclockDrawMultiplier * (float) OverclockTick.TotalSeconds
                : cell.Value.Comp.MaxCharge * OverclockFallbackDrain;
            _battery.ChangeCharge(cell.Value.Owner, -drain, cell.Value.Comp);
        }

        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (bodyPart.PartType is BodyPartType.Leg or BodyPartType.Torso)
                _wounds.CreateOrMergeWound(part, OverheatingWound, OverclockHeat);
        }

        Overheat(body);
    }

    /// <summary>
    /// The hotter the core, the worse it gets: sparks more and more often (every tick near the cut-off), then
    /// shorts in the limbs (WolfmedShortCircuitSystem arcs and stuns on each), then the torso itself cooks.
    /// </summary>
    private void Overheat(EntityUid body)
    {
        var heat = TorsoHeat(body);
        var sparkChance = heat >= OverclockSparkCertain
            ? 1f
            : OverclockSparkChance + (1f - OverclockSparkChance) * heat.Float() / OverclockSparkCertain.Float();
        if (_random.Prob(Math.Clamp(sparkChance, 0f, 1f)))
        {
            Spawn(SparkEffect, _transform.GetMapCoordinates(body));
            _audio.PlayPvs(SparkSound, body, AudioParams.Default.WithVariation(0.1f));
        }

        if (heat > OverclockShortFrom &&
            _random.Prob(Math.Clamp((heat - OverclockShortFrom).Float() / OverclockShortSpan, 0f, 1f)))
        {
            var limbs = _body.GetBodyChildren(body)
                .Where(part => WFChassisIsolationSystem.CanIsolate(part.Component))
                .Select(part => part.Id)
                .ToList();
            if (limbs.Count > 0)
                _wounds.CreateOrMergeWound(_random.Pick(limbs), ShortCircuitWound, OverclockShortSeverity);
        }

        if (heat < OverclockCookFrom)
            return;

        foreach (var (torso, _) in _body.GetBodyChildrenOfType(body, BodyPartType.Torso))
        {
            _damageRouting.TryApplyPartDamage(body, torso, new DamageSpecifier(_prototypes.Index(CookDamageType), OverclockCookDamage));
            break;
        }
    }

    /// <summary>The torso's overheating severity, zero without the wound.</summary>
    private FixedPoint2 TorsoHeat(EntityUid body)
    {
        foreach (var (part, _) in _body.GetBodyChildrenOfType(body, BodyPartType.Torso))
        {
            foreach (var wound in _wounds.GetWounds(part))
            {
                if (wound.Comp.Prototype == OverheatingWound && wound.Comp.State is not WoundState.Healed and not WoundState.Scarred)
                    return wound.Comp.Severity;
            }
        }

        return FixedPoint2.Zero;
    }
}

/// <summary>A chassis mid-reboot: the frame is down until the end time, then its sensor load is cleared.</summary>
[RegisterComponent]
public sealed partial class WFChassisRebootComponent : Component
{
    [DataField]
    public TimeSpan EndTime;
}

/// <summary>Overclocked servos: when the next drain and heat tick lands.</summary>
[RegisterComponent]
public sealed partial class WFChassisOverclockComponent : Component
{
    [DataField]
    public TimeSpan NextTick;
}
