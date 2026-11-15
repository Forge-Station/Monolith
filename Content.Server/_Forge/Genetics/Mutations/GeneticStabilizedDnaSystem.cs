using System.Linq;
using Content.Shared._Forge.Genetics;
using Content.Shared._Forge.Genetics.Components;
using Content.Shared._Forge.Genetics.Mutations;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Genetics.Mutations;

public sealed class GeneticStabilizedDnaSystem : EntitySystem
{
    private const string SelfGeneId = "GeneStabilizedDna";
    private const string Cellular = "Cellular";

    [Dependency] private readonly GeneticsSystem _genetics = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticStabilizedDnaComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<GeneticStabilizedDnaComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.NextHeal = _timing.CurTime + ent.Comp.Interval;

        if (!TryComp<GenomeComponent>(ent.Owner, out var genome))
            return;

        var stripped = 0;
        foreach (var (geneId, state) in genome.Genes.ToList())
        {
            if (!state.Active || geneId == SelfGeneId)
                continue;

            if (!_prototypes.TryIndex<GenePrototype>(geneId, out var proto))
                continue;

            if (proto.Quality is not (GeneQuality.Good or GeneQuality.Bad))
                continue;

            if (_genetics.TryDeactivateGene(ent.Owner, geneId, genome))
                stripped++;
        }

        if (stripped > 0)
            _popup.PopupEntity(Loc.GetString("gene-stabilized-stripped", ("count", stripped)), ent.Owner, ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<GeneticStabilizedDnaComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var regen, out var damageable))
        {
            if (regen.NextHeal > _timing.CurTime)
                continue;

            regen.NextHeal = _timing.CurTime + regen.Interval;
            if (_mobState.IsDead(uid))
                continue;

            if (!damageable.Damage.DamageDict.TryGetValue(Cellular, out var amount) || amount <= 0)
                continue;

            var heal = new DamageSpecifier();
            heal.DamageDict[Cellular] = -Math.Min((float) amount, regen.HealPerTick);
            _damageable.TryChangeDamage(uid, heal, ignoreResistances: true, interruptsDoAfters: false);
        }
    }
}
