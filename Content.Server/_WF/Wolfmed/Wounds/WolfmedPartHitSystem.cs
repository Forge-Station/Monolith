using Content.Server.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>Reacts to each part hit before the wounds do: charring, crumbling to ash, do-after interrupts, a slipping strap.</summary>
// Called from Onyx's one-event-per-hit dispatcher. A burn already at its maximum escalates into charring, and a
// charred hand, foot, arm or leg that keeps cooking crumbles to ash; the head and torso never crumble. A big enough
// hit interrupts what the body is doing (WolfmedDoAfterInterruptSystem).
public sealed class WolfmedPartHitSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedCharringSystem _charring = default!;
    [Dependency] private WolfmedDoAfterInterruptSystem _interrupt = default!;
    [Dependency] private WolfmedTourniquetSlipSystem _tourniquetSlip = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private const string Heat = "Heat";

    /// <summary>
    /// Test seam: every hit as the dispatcher receives it, once, before anything reads it. Damage is Applied,
    /// Overflow what the ceilings discarded, Total what the wounds and the organ step are handed.
    /// </summary>
    public Action<EntityUid, PartDamageAppliedEvent>? Observer;

    /// <summary>Called once per hit by <c>OrganDamageSystem.OnPartDamageApplied</c>.</summary>
    public void OnHit(EntityUid part, PartDamageAppliedEvent hit)
    {
        Observer?.Invoke(part, hit);
        _interrupt.OnHit(hit); // M6 (OD18)
        _tourniquetSlip.OnHit(part, hit); // a hard hit knocks a makeshift tourniquet loose before the wounds see it

        var heat = hit.Total.DamageDict.GetValueOrDefault(Heat);
        if (heat <= FixedPoint2.Zero || TerminatingOrDeleted(part))
            return;

        Escalate(part, heat);
        TrackCrumble(part);
    }

    /// <summary>A burn at its maximum turns further Heat into charring instead of dropping it.</summary>
    private void Escalate(EntityUid part, FixedPoint2 heat)
    {
        var factor = _config.GetCVar(WolfmedCVars.CharEscalation);
        if (factor <= 0f)
            return;

        foreach (var wound in _wounds.GetWounds(part))
        {
            if (!_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
                prototype.MaximumSeverity == FixedPoint2.MaxValue ||
                !prototype.TryGetBehavior(prototype.MaximumSeverity, out WolfmedCharringBehavior charring) ||
                !prototype.DamageTypes.TryGetValue(Heat, out var settings))
                continue;

            var multiplier = settings.SeverityMultiplier;
            var excess = wound.Comp.Severity + heat * multiplier - prototype.MaximumSeverity;
            if (excess <= FixedPoint2.Zero)
                continue;

            _charring.TryChar(part, charring.Wound, excess * factor);
            return;
        }
    }

    private void TrackCrumble(EntityUid part)
    {
        if (CrumbleSeconds(part) is not { } seconds)
            return;

        var now = _timing.CurTime;
        if (!IsCharredThrough(part))
        {
            RemComp<WolfmedCharCrumbleComponent>(part);
            return;
        }

        var gap = TimeSpan.FromSeconds(_config.GetCVar(WolfmedCVars.CharCrumbleGapSeconds));
        var clock = EnsureComp<WolfmedCharCrumbleComponent>(part);
        if (clock.TopSince is null || now - clock.LastHeat > gap)
            clock.TopSince = now;
        clock.LastHeat = now;

        // Deferred to the next tick: this runs inside the part's own damage event.
        if (now - clock.TopSince >= TimeSpan.FromSeconds(seconds))
            clock.Crumble = true;
    }

    /// <summary>Seconds of charring at its maximum before this part crumbles, or null if it never does (head, torso).</summary>
    public float? CrumbleSeconds(EntityUid part)
    {
        if (!TryComp(part, out BodyPartComponent? body))
            return null;

        var seconds = _config.GetCVar(WolfmedCVars.CharCrumbleSeconds);
        return body.PartType switch
        {
            BodyPartType.Hand or BodyPartType.Foot => seconds,
            BodyPartType.Arm or BodyPartType.Leg => seconds * _config.GetCVar(WolfmedCVars.CharCrumbleLimbMultiplier),
            _ => null,
        };
    }

    /// <summary>
    /// True while the charring a burn on this part leaves (its <see cref="WolfmedCharringBehavior"/> wound) sits
    /// at that wound's maximum severity.
    /// </summary>
    public bool IsCharredThrough(EntityUid part)
    {
        var charIds = new HashSet<string>();
        var severities = new Dictionary<string, FixedPoint2>();
        foreach (var wound in _wounds.GetWounds(part))
        {
            var id = wound.Comp.Prototype.Id;
            severities[id] = FixedPoint2.Max(severities.GetValueOrDefault(id), wound.Comp.Severity);
            if (_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) &&
                prototype.MaximumSeverity != FixedPoint2.MaxValue &&
                prototype.TryGetBehavior(prototype.MaximumSeverity, out WolfmedCharringBehavior charring))
                charIds.Add(charring.Wound.Id);
        }

        foreach (var id in charIds)
        {
            if (severities.TryGetValue(id, out var severity) &&
                _prototypes.TryIndex<WoundPrototype>(id, out var charPrototype) &&
                severity >= charPrototype.MaximumSeverity)
                return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var due = new List<EntityUid>();
        var query = EntityQueryEnumerator<WolfmedCharCrumbleComponent>();
        while (query.MoveNext(out var uid, out var clock))
        {
            if (clock.Crumble)
                due.Add(uid);
        }

        foreach (var part in due)
            Crumble(part);
    }

    /// <summary>
    /// The part falls away as ash. Shitmed's burn path: no stump wound and so no bleeding (the fire cauterised
    /// it), organs dropped, the ordinary part-loss handling.
    /// </summary>
    private void Crumble(EntityUid part)
    {
        RemComp<WolfmedCharCrumbleComponent>(part);
        if (!TryComp(part, out BodyPartComponent? bodyPart) || bodyPart.Body is not { } body)
            return;

        var coordinates = Transform(body).Coordinates;
        if (_body.BurnPart(part, bodyPart))
        {
            _popup.PopupCoordinates(Loc.GetString("wolfmed-char-crumble", ("part", part)),
                coordinates, PopupType.LargeCaution);
        }
    }
}

/// <summary>The crumble clock of a charred part that keeps taking Heat (OD12). Server only.</summary>
[RegisterComponent]
public sealed partial class WolfmedCharCrumbleComponent : Component
{
    /// <summary>When the charring first sat at its maximum with Heat still arriving.</summary>
    [ViewVariables]
    public TimeSpan? TopSince;

    /// <summary>The last Heat the part took.</summary>
    [ViewVariables]
    public TimeSpan LastHeat;

    /// <summary>Set inside the damage event; the part goes on the next tick.</summary>
    [ViewVariables]
    public bool Crumble;
}
