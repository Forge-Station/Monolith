using System.Collections.Generic;
using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Starlight.Actions.Stasis;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Stasis;

/// <summary>
/// Starlight's Avali stasis on a wound host. The stock system heals and shields the flat damage total, which a wound
/// host no longer reads, and its bleed stop is refused by the wound projection. Here stasis holds every bleed for as
/// long as it lasts, closes wounds at topical strength (the component's per-second amounts, spent over the body's
/// wounds), thins the parts' stored damage by the same amounts, and lets a hit keep only
/// wolfmed.stasis_damage_factor of itself before it becomes a wound. It never touches a fracture: a broken bone is
/// surgery's (owner's rule).
/// </summary>
public sealed class WolfmedStasisSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WoundDamageRoutingSystem _routing = default!;
    [Dependency] private WoundSystem _wounds = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StasisComponent, BeforeDamageChangedEvent>(OnBeforeDamage,
            after: [typeof(WoundDamageRoutingSystem)]);
    }

    /// <summary>
    /// The routed pass of a hit on a body in stasis keeps the factor's share. The stock system healed back half of
    /// the flat total after the fact, which on a wound host thinned the part and left the wound whole.
    /// </summary>
    private void OnBeforeDamage(Entity<StasisComponent> body, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !body.Comp.IsInStasis || !HasComp<WoundHostComponent>(body))
            return;

        // Scaled in place, which the damage system reads back. This runs on routing's inner pass, whose specifier is a
        // copy, so the caller's own object (the respirator's, a wound behaviour's) is never touched.
        var factor = _cfg.GetCVar(WolfmedCVars.StasisDamageFactor);
        foreach (var (type, amount) in args.Damage.DamageDict.ToArray())
        {
            if (amount > FixedPoint2.Zero)
                args.Damage.DamageDict[type] = amount * factor;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<StasisComponent, WoundHostComponent>();
        while (query.MoveNext(out var uid, out var stasis, out _))
        {
            if (TryComp(uid, out WolfmedStasisHoldComponent? hold))
            {
                if (!stasis.IsInStasis)
                {
                    RemComp<WolfmedStasisHoldComponent>(uid);
                    RefreshBleeding(uid);
                    continue;
                }
            }
            else if (stasis.IsInStasis)
            {
                hold = AddComp<WolfmedStasisHoldComponent>(uid);
                RefreshBleeding(uid);
            }
            else
                continue;

            hold.Accumulated += frameTime;
            if (hold.Accumulated < 1f)
                continue;

            hold.Accumulated -= 1f;
            Tick(uid, stasis);
        }
    }

    /// <summary>Every bleeding wound on the body recomputed against the hold, and the body's total with it.</summary>
    private void RefreshBleeding(EntityUid body)
    {
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            foreach (var wound in _wounds.GetWounds(part).ToArray())
            {
                if (TryComp(wound, out WoundBleedingComponent? bleeding))
                    _bleeding.RefreshWound((wound.Owner, bleeding));
            }
        }

        _bleeding.RefreshBody(body);
    }

    /// <summary>One second of stasis: stored damage thinned across the parts, wounds closed at topical strength.</summary>
    private void Tick(EntityUid body, StasisComponent stasis)
    {
        var heal = new DamageSpecifier();
        foreach (var (type, amount) in Amounts(stasis))
        {
            if (amount > 0f)
                heal.DamageDict[type] = FixedPoint2.New(-amount);
        }

        if (heal.Empty)
            return;

        _routing.TryApplyDamage(body, heal, origin: body, ignoreResistances: true, healWounds: false);
        foreach (var (type, amount) in Amounts(stasis))
        {
            if (amount > 0f)
                CloseWounds(body, type, FixedPoint2.New(amount));
        }
    }

    /// <summary>The component's healing a second, by damage type. Stasis ends at Unconscious, so only the standing values apply.</summary>
    private static IEnumerable<(string Type, float Amount)> Amounts(StasisComponent stasis)
    {
        yield return ("Blunt", stasis.StasisBluntHealPerSecond);
        yield return ("Slash", stasis.StasisSlashingHealPerSecond);
        yield return ("Piercing", stasis.StasisPiercingHealPerSecond);
        yield return ("Heat", stasis.StasisHeatHealPerSecond);
        yield return ("Cold", stasis.StasisColdHealPerSecond);
    }

    /// <summary>
    /// A budget of one damage type spent on the body's wounds at topical strength, first part first. A fracture is
    /// never touched; a wound nothing topical closes, or one that refuses (a lodged round), is passed over.
    /// </summary>
    private void CloseWounds(EntityUid body, string type, FixedPoint2 budget)
    {
        var damageType = new ProtoId<DamageTypePrototype>(type);
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            foreach (var wound in _wounds.GetWounds(part).ToArray())
            {
                if (budget <= FixedPoint2.Zero)
                    return;

                if (HasComp<WoundFractureComponent>(wound) ||
                    !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
                    prototype.HealingMultiplier <= 0f ||
                    !prototype.DamageTypes.TryGetValue(damageType, out var settings) ||
                    settings.SeverityMultiplier <= 0f)
                    continue;

                var severity = FixedPoint2.Min(budget * settings.SeverityMultiplier, wound.Comp.Severity);
                if (severity <= FixedPoint2.Zero || !_wounds.TreatWound(wound.Owner, severity))
                    continue;

                budget -= severity / FixedPoint2.New(settings.SeverityMultiplier);
            }
        }
    }
}
