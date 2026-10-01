using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Targeting;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>
/// Injuries a wound host is meant to spawn with. A corpse prototype carries them as preset damage (SalvageHumanCorpse
/// and its family), and a medical bounty deals them at startup, before the body has parts. Either way they were lost:
/// the body's damage is re-projected from its parts at map init, and the Dead threshold that made these bodies corpses
/// does not decide a wound host's state. Once the body is built they are laid on it: wounds spread over every part,
/// Bloodloss as missing blood, the bleeding stopped (a body found like this stopped bleeding a while ago), and a total
/// at the body's Dead threshold is a Wolfmed death, as the threshold would have made it.
/// </summary>
public sealed class WolfmedSpawnInjurySystem : EntitySystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WoundDamageRoutingSystem _routing = default!;

    private const string Bloodloss = "Bloodloss";

    /// <summary>How unevenly the injuries fall across the parts: each part's share varies by up to this fraction.</summary>
    private const float Variation = 0.5f;

    /// <summary>The parts the injuries fall on: the six big ones, so a hand or a foot does not dilute a cut to nothing.</summary>
    private const TargetBodyPart InjuredParts =
        TargetBodyPart.Head | TargetBodyPart.Torso | TargetBodyPart.Arms | TargetBodyPart.Legs;

    // Bodies taking their spawn injuries right now: nothing hit them, so the hit sounds and sprays stay quiet.
    private readonly HashSet<EntityUid> _applying = new();

    public override void Initialize()
    {
        base.Initialize();
        // After the body builds its parts and the projection resets the body's damage to them.
        SubscribeLocalEvent<WolfmedConsciousnessComponent, MapInitEvent>(OnMapInit,
            after: [typeof(SharedBodySystem), typeof(WoundDamageProjectionSystem)]);
    }

    /// <summary>True while a body is taking its spawn injuries, for the systems that answer a hit with a sound.</summary>
    public bool IsApplying(EntityUid body) => _applying.Contains(body);

    /// <summary>
    /// Injuries for a wound host from something that runs before its body exists: laid on at map init, or at once on
    /// a body that is already built. False for anything that is not a wound host; the caller deals those itself.
    /// </summary>
    public bool Defer(EntityUid body, DamageSpecifier damage)
    {
        if (!HasComp<WoundHostComponent>(body))
            return false;

        if (MetaData(body).EntityLifeStage >= EntityLifeStage.MapInitialized)
        {
            Apply(body, damage);
            return true;
        }

        var pending = EnsureComp<WolfmedSpawnInjuryComponent>(body);
        pending.Damage += damage;
        return true;
    }

    private void OnMapInit(Entity<WolfmedConsciousnessComponent> body, ref MapInitEvent args)
    {
        var damage = new DamageSpecifier();
        if (MetaData(body).EntityPrototype is { } prototype &&
            prototype.TryGetComponent(out DamageableComponent? preset, _factory))
            damage += preset.Damage;

        if (TryComp(body, out WolfmedSpawnInjuryComponent? pending))
        {
            damage += pending.Damage;
            RemComp<WolfmedSpawnInjuryComponent>(body);
        }

        if (damage.GetTotal() > FixedPoint2.Zero)
            Apply(body, damage);
    }

    /// <summary>Lays the injuries on a built wound host; see the class summary.</summary>
    public void Apply(EntityUid body, DamageSpecifier damage)
    {
        if (!HasComp<WoundHostComponent>(body) || !_applying.Add(body))
            return;

        try
        {
            var injuries = new DamageSpecifier(damage);
            if (injuries.DamageDict.Remove(Bloodloss, out var bloodloss) && bloodloss > FixedPoint2.Zero &&
                TryComp(body, out BloodstreamComponent? blood))
            {
                var share = Math.Clamp(bloodloss.Float() * _cfg.GetCVar(WolfmedCVars.SpawnBloodlossBlood), 0f, 1f);
                FixedPoint2 volume = blood.BloodMaxVolume;
                _bloodstream.TryModifyBloodLevel(body, FixedPoint2.New(-volume.Float() * share), blood);
            }

            // Nobody dealt these, so each part's ambient ceiling holds: no limb comes off a body at spawn.
            if (injuries.GetTotal() > FixedPoint2.Zero)
                _routing.TryApplyDistributedDamage(body, injuries, InjuredParts,
                    DamageDistribution.SplitWithVariation, ignoreResistances: true, interruptsDoAfters: false,
                    variation: Variation);

            _bleeding.StopBodyBleeding(body);
            // Nor is it still burning: a diona catches fire from any heat it takes.
            if (TryComp(body, out FlammableComponent? flammable))
                _flammable.Extinguish(body, flammable);
        }
        finally
        {
            _applying.Remove(body);
        }

        if (_life.OwnsDeath(body) &&
            _thresholds.TryGetThresholdForState(body, MobState.Dead, out var dead) &&
            damage.GetTotal() >= dead.Value)
            _life.Kill(body);
    }
}
