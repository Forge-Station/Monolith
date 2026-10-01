using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;

namespace Content.Shared._WF.Wolfmed.Body;

/// <summary>Reads Wolfmed part data off Shitmed body parts, with a no-wounds default.</summary>
public sealed class WolfmedBodyPartSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private static readonly WolfmedBodyPartComponent None = new();

    /// <summary>Wolfmed data for a part; a zeroed default when the part has none.</summary>
    public WolfmedBodyPartComponent Get(EntityUid part) => CompOrNull<WolfmedBodyPartComponent>(part) ?? None;

    /// <summary>
    /// The systemic damage types with a ceiling. Bloodloss used to be left out so a vital loss could deal its fixed
    /// lethal figure through it; BRAIN made that death the life system's (LostVitalPart), and nothing in the model
    /// reads Bloodloss since, while the bloodstream keeps adding it for as long as a body is kept alive on low blood
    /// (playtest 5: "someone got 800 O2 damage", the airloss group's Bloodloss).
    /// </summary>
    private static readonly string[] AirlossTypes = { "Asphyxiation", "Bloodloss" };

    /// <summary>
    /// The ceiling for one systemic damage type, or null when it has none. Suffocation and bloodloss count without
    /// bound otherwise: nothing routes them to a part, so the body cap never sees them.
    /// </summary>
    public FixedPoint2? AirlossCeiling(string type)
    {
        if (System.Array.IndexOf(AirlossTypes, type) < 0)
            return null;

        var cap = _config.GetCVar(WolfmedCVars.AirlossCap);
        return cap > 0f ? FixedPoint2.New(cap) : null;
    }

    /// <summary>
    /// M3 (P27): whether a blast may take this part off. The head only while <c>wolfmed.blast_dismember_head</c>
    /// is on, whichever path the blast would sever it by: Onyx's per-part explosion roll or an ordinary finishing
    /// hit. Read by one marked line in Onyx's <c>AmputationSystem.HandlePartDamageApplied</c>.
    /// </summary>
    public bool BlastMaySever(EntityUid part) =>
        _config.GetCVar(WolfmedCVars.BlastDismemberHead) ||
        CompOrNull<Content.Shared.Body.Part.BodyPartComponent>(part)?.PartType != Content.Shared.Body.Part.BodyPartType.Head;

    private readonly HashSet<EntityUid> _ceilingBypass = new();

    /// <summary>
    /// Runs <paramref name="action"/> with the ambient ceilings off for <paramref name="body"/>: the admin part
    /// command has no origin, and an admin has to be able to destroy a limb or a head to test it (P31).
    /// </summary>
    public void WithCeilingBypass(EntityUid body, Action action)
    {
        var added = _ceilingBypass.Add(body);
        try
        {
            action();
        }
        finally
        {
            if (added)
                _ceilingBypass.Remove(body);
        }
    }

    /// <summary>
    /// The per-part ceiling for damage nobody dealt: wolfmed.ambient_part_cap_fraction of the part's lowest
    /// destruction threshold, or null when the part has none (the torso, which keeps its own cap).
    /// </summary>
    public FixedPoint2? AmbientCeiling(EntityUid part)
    {
        var fraction = _config.GetCVar(WolfmedCVars.AmbientPartCapFraction);
        if (fraction <= 0f)
            return null;

        var ev = new WolfmedPartDestructionThresholdEvent();
        RaiseLocalEvent(part, ref ev);
        return ev.Threshold is { } threshold ? threshold * fraction : null;
    }

    /// <summary>Trims ambient part damage to the part and corpse ceilings and returns what it trimmed.</summary>
    // The caller only sends damage with no attacker behind it; attacks and explosions never reach here. The hit still
    // carries the trimmed amount as overflow. The part's own ceiling keeps a fire from destroying a limb or the head;
    // on a dead body the corpse ceiling caps the body's total.
    public DamageSpecifier ClampToBodyCap(EntityUid body, EntityUid part, DamageSpecifier damage)
    {
        var trimmed = new DamageSpecifier();
        if (_ceilingBypass.Contains(body))
            return trimmed;

        if (AmbientCeiling(part) is { } ceiling && TryComp(part, out DamageableComponent? partDamage))
            Trim(damage, ceiling - PositiveTotal(partDamage.Damage), trimmed);

        var cap = _config.GetCVar(WolfmedCVars.BodyDamageCap);
        if (cap > 0f && _mobState.IsDead(body) && TryComp(body, out DamageableComponent? bodyDamage))
            Trim(damage, FixedPoint2.New(cap) - bodyDamage.TotalDamage, trimmed);

        return trimmed;
    }

    private static FixedPoint2 PositiveTotal(DamageSpecifier damage)
    {
        var total = FixedPoint2.Zero;
        foreach (var amount in damage.DamageDict.Values)
        {
            if (amount > FixedPoint2.Zero)
                total += amount;
        }

        return total;
    }

    /// <summary>Scales the positive entries of <paramref name="damage"/> down to <paramref name="room"/>, adding the cut to <paramref name="trimmed"/>.</summary>
    private static void Trim(DamageSpecifier damage, FixedPoint2 room, DamageSpecifier trimmed)
    {
        var incoming = PositiveTotal(damage);
        room = FixedPoint2.Max(room, FixedPoint2.Zero);
        if (incoming <= FixedPoint2.Zero || incoming <= room)
            return;

        var scale = room.Float() / incoming.Float();
        foreach (var (type, amount) in new Dictionary<string, FixedPoint2>(damage.DamageDict))
        {
            if (amount <= FixedPoint2.Zero)
                continue;

            var kept = amount * scale;
            trimmed.DamageDict[type] = trimmed.DamageDict.GetValueOrDefault(type) + (amount - kept);
            if (kept <= FixedPoint2.Zero)
                damage.DamageDict.Remove(type);
            else
                damage.DamageDict[type] = kept;
        }
    }
}

/// <summary>Asks a body part for its lowest destruction threshold (server: its Destructible triggers).</summary>
[ByRefEvent]
public record struct WolfmedPartDestructionThresholdEvent(FixedPoint2? Threshold = null);
