using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>The Wolfmed half of Onyx's <see cref="PainSystem"/>: one pain number per body.</summary>
// A body's pain is min(soft cap, Σ parts), suppression is shared out once across the parts, and the pain shock reads
// its lines from CVars. Adrenaline does not lower pain; it speeds the crawl and lifts the Downed do-after penalty.
public sealed class WolfmedBodyPainSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedBodySystem _body = default!;

    public FixedPoint2 ShockThreshold => FixedPoint2.New(_cfg.GetCVar(WolfmedCVars.PainShockThreshold));

    public FixedPoint2 ShockRearm => FixedPoint2.New(_cfg.GetCVar(WolfmedCVars.PainShockRearm));

    public TimeSpan AdrenalineTime => TimeSpan.FromSeconds(MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.AdrenalineSeconds)));

    public float AdrenalineCrawlMultiplier => MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.AdrenalineCrawlMultiplier));

    /// <summary>Pain a second a part sheds from the share no open wound backs (playtest 4).</summary>
    public FixedPoint2 LooseRecovery => FixedPoint2.New(MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.PainLooseRecovery)));

    /// <summary>Playtest 5: wolfmed.pain_scale, on every gain and every floor.</summary>
    public float PainScale => MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.PainScale));

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PainComponent, ComponentInit>(OnPainInit);
        SubscribeLocalEvent<WoundHostComponent, ModifyPainGainEvent>(OnModifyPainGain);
    }

    /// <summary>Playtest 5: every pain gain and every wound floor on a wound host is scaled by wolfmed.pain_scale.</summary>
    private void OnModifyPainGain(Entity<WoundHostComponent> body, ref ModifyPainGainEvent args)
    {
        args.Multiplier *= PainScale;
    }

    /// <summary>
    /// Playtest 5: the part's wound floor moved. A rise (a new wound, one getting worse) restarts the settle clock;
    /// a fall leaves it, so a wound closing does not put pain back.
    /// </summary>
    public void FloorChanged(EntityUid part, FixedPoint2 oldFloor, FixedPoint2 newFloor)
    {
        if (newFloor <= oldFloor)
            return;

        EnsureComp<WolfmedPainSettleComponent>(part).FloorRaisedAt = _timing.CurTime;
    }

    /// <summary>
    /// The share of the part's wound floor that still holds: all of it right after the floor rose, sliding to
    /// wolfmed.pain_floor_rest over wolfmed.pain_floor_settle_seconds, so the loose recovery can take a wound that is
    /// not getting worse below the pain it made.
    /// </summary>
    public FixedPoint2 SettledFloor(EntityUid part, FixedPoint2 floor)
    {
        if (floor <= FixedPoint2.Zero)
            return floor;

        var rest = Math.Clamp(_cfg.GetCVar(WolfmedCVars.PainFloorRest), 0f, 1f);
        var settle = _cfg.GetCVar(WolfmedCVars.PainFloorSettleSeconds);
        if (rest >= 1f)
            return floor;

        // A floor nobody raised through the wounds (set by hand, a test) holds whole.
        if (!TryComp(part, out WolfmedPainSettleComponent? settled))
            return floor;

        var since = (float) (_timing.CurTime - settled.FloorRaisedAt).TotalSeconds;
        var progress = settle <= 0f ? 1f : Math.Clamp(since / settle, 0f, 1f);
        return floor * (1f - (1f - rest) * progress);
    }

    /// <summary>
    /// Playtest 4: a limb holds less pain than the body. The part's soft cap comes from the wolfmed.part_pain_cap_*
    /// line for its type when the type has one; head and torso keep Onyx's cap, so they alone still reach Downed
    /// and the faint. Read once when the part gains its pain, so a changed line applies to bodies made after it.
    /// </summary>
    private void OnPainInit(Entity<PainComponent> ent, ref ComponentInit args)
    {
        if (!TryComp(ent, out BodyPartComponent? part))
            return;

        var cap = PartPainCap(part.PartType);
        if (cap > 0f)
            ent.Comp.SoftPainCap = FixedPoint2.New(cap);
    }

    /// <summary>The wolfmed.part_pain_cap_* line for a part type, or 0 when the type keeps its own cap.</summary>
    public float PartPainCap(BodyPartType type)
    {
        return type switch
        {
            BodyPartType.Arm => _cfg.GetCVar(WolfmedCVars.PartPainCapArm),
            BodyPartType.Hand => _cfg.GetCVar(WolfmedCVars.PartPainCapHand),
            BodyPartType.Leg => _cfg.GetCVar(WolfmedCVars.PartPainCapLeg),
            BodyPartType.Foot => _cfg.GetCVar(WolfmedCVars.PartPainCapFoot),
            _ => 0f,
        };
    }

    /// <summary>
    /// P13: the pain a body with feeling parts carries is the sum of the parts, capped at its soft cap,
    /// whatever anything tries to set it to. False for a part, or a body with no part that feels pain.
    /// </summary>
    public bool TryGetDerivedPain(EntityUid uid, out FixedPoint2 value)
    {
        value = FixedPoint2.Zero;
        if (HasComp<BodyPartComponent>(uid))
            return false;

        var any = false;
        foreach (var (part, _) in _body.GetBodyChildren(uid))
        {
            if (!TryComp(part, out PainComponent? pain))
                continue;

            any = true;
            value += pain.Value;
        }

        return any;
    }

    /// <summary>
    /// The share of the body's suppression one part carries: its pain over the sum of the parts, so the
    /// shares add up to 1 and the body's suppression is taken off once, not once per part.
    /// </summary>
    public float SuppressionShare(Entity<PainComponent> part, EntityUid body)
    {
        var sum = 0f;
        foreach (var (child, _) in _body.GetBodyChildren(body))
        {
            if (TryComp(child, out PainComponent? pain))
                sum += pain.Value.Float();
        }

        return sum > 0f ? part.Comp.Value.Float() / sum : 0f;
    }

    /// <summary>Adrenaline from a pain shock is running on this body.</summary>
    public bool HasAdrenaline(EntityUid body) =>
        TryComp(body, out PainShockTargetComponent? shock) && shock.AdrenalineEnds > _timing.CurTime;

    /// <summary>Called by the pain shock when its adrenaline starts or runs out.</summary>
    public void AdrenalineChanged(EntityUid body, bool started)
    {
        _movement.RefreshMovementSpeedModifiers(body);
        var ev = new WolfmedAdrenalineEvent(body, started);
        RaiseLocalEvent(ref ev);
    }
}

/// <summary>Broadcast when a pain shock's adrenaline starts or runs out. The patient is told both.</summary>
[ByRefEvent]
public readonly record struct WolfmedAdrenalineEvent(EntityUid Body, bool Started);
