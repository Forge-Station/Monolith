// WOLFGATE(Xenoarch) START: wizden's EntityTable upgrade, for the xenoarch trigger tables
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.EntityTable.EntitySelectors;

/// <summary>
/// Gets spawns from all of the child selectors
/// </summary>
public sealed partial class AllSelector : EntityTableSelectorWithChildrenBase
{
    protected override IEnumerable<EntProtoId> GetSpawnsImplementation(IRobustRandom rand,
        IEntityManager entMan,
        IPrototypeManager proto,
        EntityTableContext ctx)
    {
        using var scoped = ScopedConditions(ctx);

        foreach (var child in Children)
        {
            foreach (var spawn in child.GetSpawns(rand, entMan, proto, ctx))
            {
                yield return spawn;
            }
        }
    }

    protected override IEnumerable<(EntProtoId spawn, double)> ListSpawnsImplementation(IEntityManager entMan, IPrototypeManager proto, EntityTableContext ctx)
    {
        foreach (var child in Children)
        {
            foreach (var (spawn, prob) in child.ListSpawns(entMan, proto, ctx))
            {
                yield return (spawn, prob);
            }
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"All({string.Join(", ", Children)})";
    }
}
// WOLFGATE END
