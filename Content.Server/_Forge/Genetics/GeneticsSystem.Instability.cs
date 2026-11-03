using Content.Shared._Forge.Genetics;
using Content.Shared._Forge.Genetics.Components;
using Content.Shared.Damage;
using Content.Shared.Jittering;
using Robust.Shared.Random;

namespace Content.Server._Forge.Genetics;

public sealed partial class GeneticsSystem
{
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;

    private static readonly TimeSpan InstabilityInterval = TimeSpan.FromSeconds(12);

    private void InitializeInstability()
    {
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GenomeComponent>();
        while (query.MoveNext(out var uid, out var genome))
        {
            if (genome.NextInstabilityCheck > _timing.CurTime)
                continue;

            genome.NextInstabilityCheck = _timing.CurTime + InstabilityInterval;
            TickInstability(uid, genome);
        }

        var visualQuery = EntityQueryEnumerator<GeneticVisualsComponent>();
        while (visualQuery.MoveNext(out var visUid, out var visuals))
        {
            if (!visuals.Flicker || !_random.Prob(0.08f * frameTime * 12f))
                continue;

            _jitter.DoJitter(visUid, TimeSpan.FromSeconds(0.7), true);
        }
    }

    private void TickInstability(EntityUid uid, GenomeComponent genome)
    {
        RecalculateInstability(genome);
        if (genome.Instability <= genome.InstabilityThreshold)
            return;

        var overflow = genome.Instability - genome.InstabilityThreshold;
        var chance = Math.Clamp(overflow / 100f, 0.05f, 0.45f);
        if (!_random.Prob(chance))
            return;

        if (_random.Prob(0.5f))
        {
            var damage = new DamageSpecifier();
            damage.DamageDict["Cellular"] = 4 + overflow / 10f;
            _damageable.TryChangeDamage(uid, damage);
            _popup.PopupEntity(Loc.GetString("genetics-instability-cellular"), uid);
            return;
        }

        foreach (var proto in Prototypes.EnumeratePrototypes<GenePrototype>())
        {
            if (proto.Quality != GeneQuality.Bad || !IsGeneInRound(proto.ID))
                continue;

            if (genome.Genes.TryGetValue(proto.ID, out var existing) && existing.Active)
                continue;

            TryInjectGene(uid, proto.ID, genome);
            _popup.PopupEntity(Loc.GetString("genetics-instability-collapse"), uid);
            return;
        }
    }
}
