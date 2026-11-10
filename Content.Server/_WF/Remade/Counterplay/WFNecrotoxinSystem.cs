using Content.Server.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Counterplay;

/// <summary>
/// Necrotoxin poisons the Remade through their blood and keeps a dosed corpse from ever turning. Crew take it far more gently,
/// through the reagent's own metabolism.
/// </summary>
public sealed class WFNecrotoxinSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private TagSystem _tags = default!;

    public const string Reagent = "WFNecrotoxin";

    private const string BloodSolution = "chemicals";
    private static readonly ProtoId<TagPrototype> RemadeTag = "WFRemade";
    private static readonly ProtoId<DamageTypePrototype> Poison = "Poison";
    private static readonly TimeSpan PoisonRate = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CorpseRate = TimeSpan.FromSeconds(2);

    /// <summary>Damage a second per unit in a Remade's blood, up to <see cref="DamageUnitsCap"/> units.</summary>
    public const float DamagePerUnit = 0.5f;

    public const float DamageUnitsCap = 6f;

    /// <summary>Units its blood sheds a second.</summary>
    public const float ClearPerSecond = 0.4f;

    /// <summary>Units a corpse needs in it to be safe from turning.</summary>
    public const float CorpseDose = 1f;

    private TimeSpan _nextPoison;
    private TimeSpan _nextCorpse;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now >= _nextPoison)
        {
            _nextPoison = now + PoisonRate;
            PoisonRemade();
        }

        if (now >= _nextCorpse)
        {
            _nextCorpse = now + CorpseRate;
            ProtectCorpses();
        }
    }

    /// <summary>Units of necrotoxin in a body's blood.</summary>
    public float Dose(EntityUid uid)
    {
        return _solutions.TryGetSolution(uid, BloodSolution, out _, out var solution)
            ? solution.GetTotalPrototypeQuantity(Reagent).Float()
            : 0f;
    }

    private void PoisonRemade()
    {
        var query = EntityQueryEnumerator<BloodstreamComponent, TagComponent>();
        while (query.MoveNext(out var uid, out _, out var tags))
        {
            if (!_tags.HasTag(tags, RemadeTag)
                || _mobState.IsDead(uid)
                || !_solutions.TryGetSolution(uid, BloodSolution, out var blood, out var solution))
            {
                continue;
            }

            var units = solution.GetTotalPrototypeQuantity(Reagent).Float();
            if (units <= 0f)
                continue;

            _solutions.RemoveReagent(blood.Value, Reagent, FixedPoint2.New(Math.Min(units, ClearPerSecond)));
            var damage = new DamageSpecifier(_proto.Index(Poison), FixedPoint2.New(Math.Min(units, DamageUnitsCap) * DamagePerUnit));
            _damageable.TryChangeDamage(uid, damage, ignoreResistances: true, interruptsDoAfters: false, canSever: false);
        }
    }

    /// <summary>Marks any dosed corpse, crew or animal, so it can't turn.</summary>
    private void ProtectCorpses()
    {
        var query = EntityQueryEnumerator<BloodstreamComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            if (!_mobState.IsDead(uid) || HasComp<WFNecrotoxinProtectedComponent>(uid))
                continue;

            if (Dose(uid) >= CorpseDose)
                AddComp<WFNecrotoxinProtectedComponent>(uid);
        }
    }
}
