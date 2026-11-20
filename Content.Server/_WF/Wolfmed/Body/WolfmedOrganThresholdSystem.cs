using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._Onyx.Body;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;

namespace Content.Server._WF.Wolfmed.Body;

/// <summary>Damages organs by the size of a hit rather than a roll, and knocks out on a heavy head blow.</summary>
/// <remarks>
/// Called once per hit, with the hit's Total, from the one marked line in Onyx's
/// <c>OrganDamageSystem.OnPartDamageApplied</c>; a part without reach lines keeps Onyx's roll. No subscription of
/// its own.
/// </remarks>
// A hit whose damage after armour exceeds its part's reach line for a type reaches the organs inside. The excess is
// split across the working organs: (hit - line) x the organ's multiplier for the type x its weight share x
// wolfmed.organ_damage_scale, capped per organ per hit. A destroyed organ leaves the split, so the rest take more.
public sealed class WolfmedOrganThresholdSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private OrganHealthSystem _organHealth = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;

    private const string Blunt = "Blunt";

    /// <summary>Test seam: every organ a hit reached, with the health it took.</summary>
    public Action<EntityUid, FixedPoint2>? Observer;

    /// <summary>Nesting depth of <see cref="WithoutOrganReach"/>.</summary>
    private int _noReach;

    /// <summary>
    /// M4 (plan §3.11, principle C): runs <paramref name="action"/> with the reach lines off. The overheat pulse cooks
    /// a chassis's parts for its pain; the core-heat route is the one way heat reaches the core, so the pulse must not
    /// be a second, hidden one through the torso's Heat line.
    /// </summary>
    public void WithoutOrganReach(Action action)
    {
        _noReach++;
        try
        {
            action();
        }
        finally
        {
            _noReach--;
        }
    }

    /// <summary>
    /// One hit on a part. Returns false when the part has no reach lines, which leaves it to Onyx's roll; true
    /// when this system owns the part's organ damage, whether or not the hit reached anything.
    /// </summary>
    public bool HandleHit(EntityUid part, DamageSpecifier total)
    {
        // M4: owned and reaching nothing, so Onyx's roll does not run for the pulse either.
        if (_noReach > 0)
            return true;

        if (!TryComp(part, out WolfmedBodyPartComponent? data) || data.OrganReach.Count == 0)
            return false;

        if (TerminatingOrDeleted(part) || !TryComp(part, out BodyPartComponent? bodyPart) ||
            bodyPart.Body is not { } body)
            return true;

        DamageOrgans(part, data, total);

        if (bodyPart.PartType == BodyPartType.Head)
            TryHeadBlow(body, total);

        return true;
    }

    /// <summary>What each reach line leaves over, per damage type. Empty when the hit reached nothing.</summary>
    public Dictionary<string, float> GetExcess(WolfmedBodyPartComponent data, DamageSpecifier total)
    {
        var excess = new Dictionary<string, float>();
        foreach (var (type, line) in data.OrganReach)
        {
            var amount = total.DamageDict.GetValueOrDefault(type);
            if (amount > line)
                excess[type] = (amount - line).Float();
        }

        return excess;
    }

    private void DamageOrgans(EntityUid part, WolfmedBodyPartComponent data, DamageSpecifier total)
    {
        var excess = GetExcess(data, total);
        if (excess.Count == 0)
            return;

        var organs = new List<(EntityUid Id, WolfmedOrganComponent Health, OrganDamageComponent Policy)>();
        var weight = 0f;
        foreach (var (organ, _) in _body.GetPartOrgans(part))
        {
            if (!TryComp(organ, out WolfmedOrganComponent? health) || health.Health <= FixedPoint2.Zero ||
                !TryComp(organ, out OrganDamageComponent? policy))
                continue;

            organs.Add((organ, health, policy));
            weight += MathF.Max(0f, policy.SelectionWeight);
        }

        if (organs.Count == 0 || weight <= 0f)
            return;

        var scale = MathF.Max(0f, _config.GetCVar(WolfmedCVars.OrganDamageScale));
        var cap = _config.GetCVar(WolfmedCVars.OrganHitCap);
        foreach (var (organ, health, policy) in organs)
        {
            var share = MathF.Max(0f, policy.SelectionWeight) / weight;
            var sum = 0f;
            foreach (var (type, over) in excess)
                sum += over * MathF.Max(0f, policy.DamageMultipliers.GetValueOrDefault(type));

            var damage = sum * share * scale;
            var organCap = health.HitCap ?? cap;
            if (organCap > 0f)
                damage = MathF.Min(damage, organCap);

            var applied = FixedPoint2.New(damage);
            if (applied <= FixedPoint2.Zero)
                continue;

            _organHealth.ChangeHealth((organ, health), -applied);
            Observer?.Invoke(organ, applied);
        }
    }

    /// <summary>A heavy enough blow to the head knocks the patient out for a few seconds (plan §3.6).</summary>
    private void TryHeadBlow(EntityUid body, DamageSpecifier total)
    {
        var line = _config.GetCVar(WolfmedCVars.HeadKnockoutBlunt);
        if (line <= 0f || total.DamageDict.GetValueOrDefault(Blunt).Float() < line)
            return;

        _consciousness.StartHeadBlow(body);
    }
}
