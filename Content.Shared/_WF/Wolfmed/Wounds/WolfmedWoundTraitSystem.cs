using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Reads the Wolfmed wound behaviors (<see cref="WolfmedArterialBleedBehavior"/>,
/// <see cref="WolfmedInfectionRiskBehavior"/>) off a live wound, and enforces the one rule that belongs
/// nowhere else: an arterial bleed refuses treatment until the bleeding itself has been stopped.
/// </summary>
public sealed class WolfmedWoundTraitSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private Content.Shared.Body.Systems.SharedBodySystem _body = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private WoundSystem _wounds = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundComponent, WoundTreatmentAttemptEvent>(OnTreatmentAttempt);
        SubscribeLocalEvent<WoundComponent, WoundCreatedEvent>(OnWoundCreated);
        SubscribeLocalEvent<WoundComponent, WoundChangedEvent>(OnWoundChanged);
        SubscribeLocalEvent<WoundComponent, WoundRemovedEvent>(OnWoundRemoved);
    }

    // Onyx refreshes movement speed off fracture events, which a limb-penalty wound does not raise.
    // The relay is W3's extension point: the directed subscriptions for these three events are all taken,
    // so other Wolfmed systems (concussion, organ contusion) answer the broadcast instead.
    private void OnWoundCreated(Entity<WoundComponent> wound, ref WoundCreatedEvent args)
    {
        RefreshLimb(wound, args.Part);
        Relay(WolfmedWoundLifecycle.Created, args.Part, wound, FixedPoint2.Zero);
    }

    private void OnWoundChanged(Entity<WoundComponent> wound, ref WoundChangedEvent args)
    {
        RefreshLimb(wound, args.Part);
        Relay(WolfmedWoundLifecycle.Changed, args.Part, wound, args.OldSeverity);
    }

    private void OnWoundRemoved(Entity<WoundComponent> wound, ref WoundRemovedEvent args)
    {
        RefreshLimb(wound, args.Part);
        Relay(WolfmedWoundLifecycle.Removed, args.Part, wound, wound.Comp.Severity);
    }

    private void Relay(WolfmedWoundLifecycle kind, EntityUid part, Entity<WoundComponent> wound, FixedPoint2 old)
    {
        var severity = kind == WolfmedWoundLifecycle.Removed ? FixedPoint2.Zero : wound.Comp.Severity;
        var relayed = new WolfmedWoundLifecycleEvent(kind, part, wound, wound.Comp.Prototype, old, severity);
        RaiseLocalEvent(ref relayed);
    }

    private void RefreshLimb(Entity<WoundComponent> wound, EntityUid part)
    {
        if (!TryGetBehavior(wound.AsNullable(), out WolfmedLimbPenaltyBehavior _) ||
            CompOrNull<BodyPartComponent>(part)?.Body is not { } body)
            return;

        _movement.RefreshMovementSpeedModifiers(body);
    }

    private void OnTreatmentAttempt(Entity<WoundComponent> wound, ref WoundTreatmentAttemptEvent args)
    {
        if (args.Cancelled || !TryGetBehavior(wound.AsNullable(), out WolfmedArterialBleedBehavior behavior) ||
            !behavior.RequiresStoppedBleedToTreat)
            return;

        // No bleeding component, or a rate already held at zero by a tourniquet, clamp or cautery: the
        // wound can be closed. Anything still pumping is refused, dressings included.
        if (TryComp(wound, out WoundBleedingComponent? bleeding) && bleeding.CurrentRate > 0f)
            args.Cancelled = true;
    }

    /// <summary>
    /// Whether the part is living tissue. The organic/mechanical selector W6 keys on: infection, necrosis
    /// and every W1-W5 wound belong to parts that answer true, mechanical wounds to parts that answer false.
    /// </summary>
    /// <remarks>
    /// Read off the part profile's <c>TreatmentCapabilities</c> rather than off a species, so a cybernetic
    /// limb on a human answers false and a cloned organic limb on an IPC would answer true. A part with no
    /// profile is treated as organic, which is what every non-Wolfmed caller expects.
    /// </remarks>
    public bool IsOrganic(Entity<WoundableComponent?> part) =>
        !Resolve(part, ref part.Comp, false) ||
        !_prototypes.TryIndex(part.Comp.Profile, out var profile) ||
        profile.TreatmentCapabilities.Count == 0 ||
        profile.TreatmentCapabilities.Contains(TreatmentCapability.Biological);

    /// <summary>Whether the part is a chassis: repairable with tools, and never flesh.</summary>
    public bool IsMechanical(Entity<WoundableComponent?> part) =>
        Resolve(part, ref part.Comp, false) &&
        _prototypes.TryIndex(part.Comp.Profile, out var profile) &&
        !profile.TreatmentCapabilities.Contains(TreatmentCapability.Biological) &&
        (profile.TreatmentCapabilities.Contains(TreatmentCapability.Mechanical) ||
         profile.TreatmentCapabilities.Contains(TreatmentCapability.Electrical));

    /// <summary>The wound's behavior of this type at its current severity, if its prototype declares one.</summary>
    public bool TryGetBehavior<T>(Entity<WoundComponent?> wound, out T behavior) where T : WoundBehavior
    {
        behavior = null!;
        return Resolve(wound, ref wound.Comp, false) &&
               _prototypes.TryIndex(wound.Comp.Prototype, out var prototype) &&
               prototype.TryGetBehavior(wound.Comp.Severity, out behavior);
    }

    /// <summary>
    /// How much more likely this wound is to become infected than an ordinary one. 1 for everything that
    /// says nothing. W5's infection timer is the intended reader.
    /// </summary>
    public float GetInfectionRisk(Entity<WoundComponent?> wound) =>
        TryGetBehavior(wound, out WolfmedInfectionRiskBehavior behavior) ? behavior.RiskMultiplier : 1f;

    /// <summary>The highest infection risk among the wounds a part is carrying.</summary>
    public float GetPartInfectionRisk(Entity<WoundableComponent?> part)
    {
        var risk = 1f;
        foreach (var wound in _wounds.GetWounds(part))
            risk = Math.Max(risk, GetInfectionRisk(wound.Owner));

        return risk;
    }

    /// <summary>
    /// How fast the tissue under this wound is dying, and how long it has to stay that way before it is
    /// gone. 0 for anything that does not declare the risk. W5's necrosis timer is the intended reader.
    /// </summary>
    public float GetNecrosisRisk(Entity<WoundComponent?> wound, out TimeSpan onset)
    {
        if (TryGetBehavior(wound, out WolfmedNecrosisRiskBehavior behavior))
        {
            onset = behavior.Onset;
            return behavior.RiskMultiplier;
        }

        onset = TimeSpan.Zero;
        return 0f;
    }

    /// <summary>
    /// M6 (P30): how long the part's tissue has left, from the wound that kills it soonest: each wound's onset
    /// divided by its risk multiplier. Zero when nothing on the part carries a necrosis risk.
    /// </summary>
    public TimeSpan GetPartNecrosisOnset(Entity<WoundableComponent?> part)
    {
        var soonest = TimeSpan.Zero;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred)
                continue;

            var risk = GetNecrosisRisk(wound.Owner, out var onset);
            if (risk <= 0f || onset <= TimeSpan.Zero)
                continue;

            var effective = onset / risk;
            if (soonest == TimeSpan.Zero || effective < soonest)
                soonest = effective;
        }

        return soonest;
    }

    /// <summary>
    /// The worst limb penalty the part's wounds impose, or false when none of them impose one.
    /// <paramref name="mobility"/> picks the movement multiplier (which scales down) over the
    /// manipulation duration multiplier (which scales up).
    /// </summary>
    public bool TryGetLimbPenalty(Entity<WoundableComponent?> part, bool mobility, out float modifier)
    {
        modifier = 1f;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                !TryGetBehavior(wound.Owner, out WolfmedLimbPenaltyBehavior behavior))
                continue;

            var value = mobility ? behavior.MovementModifier : behavior.ManipulationModifier;
            modifier = mobility ? Math.Min(modifier, value) : Math.Max(modifier, value);
        }

        return modifier != 1f;
    }

    /// <summary>
    /// Playtest 2: the worst wound penalty across the body's hands and arms (manipulation, highest) or legs and feet
    /// (movement, lowest), and whether a burn is behind it. 1 when nothing slows them.
    /// </summary>
    public float GetBodyLimbPenalty(EntityUid body, bool mobility, out bool burn)
    {
        burn = false;
        var worst = 1f;
        if (!TryComp(body, out WoundHostComponent? host))
            return worst;

        var types = mobility ? host.MobilityParts : host.ManipulationParts;
        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (!types.Contains(bodyPart.PartType))
                continue;

            foreach (var wound in _wounds.GetWounds(part))
            {
                if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                    !TryGetBehavior(wound.Owner, out WolfmedLimbPenaltyBehavior behavior))
                    continue;

                var value = mobility ? behavior.MovementModifier : behavior.ManipulationModifier;
                if (mobility ? value >= worst : value <= worst)
                    continue;

                worst = value;
                burn = _prototypes.TryIndex(wound.Comp.Prototype, out var proto) &&
                       WolfmedWoundCategories.Resolve(proto) == WolfmedWoundCategory.Burn;
            }
        }

        return worst;
    }

    /// <summary>Whether a tourniquet on this part can do anything for this wound.</summary>
    public bool CanTourniquet(Entity<WoundComponent?> wound, EntityUid part)
    {
        if (!TryGetBehavior(wound, out WolfmedArterialBleedBehavior behavior))
            return true;

        return TryComp(part, out BodyPartComponent? bodyPart) &&
               behavior.TourniquetableParts.Contains(bodyPart.PartType);
    }

    /// <summary>Whether a tourniquet on this part would stop any of the bleeding it is currently doing.</summary>
    public bool CanTourniquetPart(Entity<WoundableComponent?> part)
    {
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (TryComp(wound, out WoundBleedingComponent? bleeding) && bleeding.CurrentRate > 0f &&
                CanTourniquet(wound.Owner, part))
                return true;
        }

        return false;
    }
}
