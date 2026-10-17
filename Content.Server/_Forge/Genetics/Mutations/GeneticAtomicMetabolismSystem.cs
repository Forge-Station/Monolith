using Content.Shared._Forge.Genetics.Mutations;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Radiation.Components;
using Content.Shared.Radiation.Events;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Genetics.Mutations;

/// <summary>
/// Converts Radiation damage in the body into Brute healing and hunger.
/// Self-emission is undone so the glow does not create free radiation to metabolize.
/// </summary>
public sealed partial class GeneticAtomicMetabolismSystem : EntitySystem
{
    private static readonly string[] BruteTypes = ["Blunt", "Slash", "Piercing"];
    private const string Radiation = "Radiation";
    private static readonly FixedPoint2 RadiationClearThreshold = FixedPoint2.New(2);
    private static readonly TimeSpan MetabolizeInterval = TimeSpan.FromSeconds(1);

    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticAtomicMetabolismComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GeneticAtomicMetabolismComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GeneticAtomicMetabolismComponent, OnIrradiatedEvent>(OnIrradiated);
    }

    private void OnStartup(Entity<GeneticAtomicMetabolismComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.NextMetabolize = _timing.CurTime + MetabolizeInterval;

        if (HasComp<RadiationSourceComponent>(ent.Owner))
            return;

        var source = EnsureComp<RadiationSourceComponent>(ent.Owner);
        source.Intensity = ent.Comp.RadiationIntensity;
        source.Slope = ent.Comp.RadiationSlope;
        source.Enabled = true;
        ent.Comp.AddedRadiationSource = true;
        Dirty(ent);
    }

    private void OnShutdown(Entity<GeneticAtomicMetabolismComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.AddedRadiationSource)
            RemComp<RadiationSourceComponent>(ent.Owner);
    }

    /// <summary>
    /// Undo radiation damage from our own glow so self-emission cannot fuel infinite healing.
    /// External radiation still accumulates as Radiation damage.
    /// </summary>
    private void OnIrradiated(Entity<GeneticAtomicMetabolismComponent> ent, ref OnIrradiatedEvent args)
    {
        if (!TryComp<RadiationSourceComponent>(ent.Owner, out var source) || !source.Enabled)
            return;

        // At distance 0 the source contributes up to Intensity rads/sec.
        var selfRads = Math.Min(args.TotalRads, source.Intensity * args.FrameTime);
        if (selfRads <= 0)
            return;

        var undo = new DamageSpecifier();
        undo.DamageDict[Radiation] = -selfRads;
        _damageable.TryChangeDamage(ent.Owner, undo, ignoreResistances: true, interruptsDoAfters: false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<GeneticAtomicMetabolismComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var meta, out var damageable))
        {
            if (meta.NextMetabolize > _timing.CurTime)
                continue;

            meta.NextMetabolize = _timing.CurTime + MetabolizeInterval;
            if (_mobState.IsDead(uid))
                continue;

            if (!damageable.Damage.DamageDict.TryGetValue(Radiation, out var rad) || rad <= FixedPoint2.Zero)
                continue;

            // Tiny leftover radiation — snap to zero so it cannot drip forever.
            if (rad < RadiationClearThreshold)
            {
                var clear = new DamageSpecifier();
                clear.DamageDict[Radiation] = -rad;
                _damageable.TryChangeDamage(uid, clear, ignoreResistances: true, interruptsDoAfters: false);
                continue;
            }

            var healBudget = (float) rad * meta.HealPerRad;
            var heal = new DamageSpecifier();
            var healed = 0f;

            foreach (var type in BruteTypes)
            {
                if (healBudget <= 0)
                    break;

                if (!damageable.Damage.DamageDict.TryGetValue(type, out var amount) || amount <= 0)
                    continue;

                var portion = Math.Min((float) amount, healBudget);
                heal.DamageDict[type] = -portion;
                healBudget -= portion;
                healed += portion;
            }

            if (healed <= 0)
                continue;

            // Spend radiation proportional to healed brute (1 rad → HealPerRad HP).
            var radSpent = FixedPoint2.New(healed / meta.HealPerRad);
            heal.DamageDict[Radiation] = -FixedPoint2.Min(rad, radSpent);

            _damageable.TryChangeDamage(uid, heal, ignoreResistances: true, interruptsDoAfters: false);

            if (HasComp<HungerComponent>(uid))
                _hunger.ModifyHunger(uid, (float) radSpent * meta.NutritionPerRad);
        }
    }
}
