using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility; // WOLFGATE(Xenoarch)

namespace Content.Shared.EntityTable;

public sealed partial class EntityTableSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;

    // WOLFGATE(Xenoarch): wizden's EntityTable upgrade, context-aware lookups for the xenoarch trigger tables
    /// <summary>
    /// Iterates once through Entities that EntitySelector in EntityTableProto provides and picks first one.
    /// </summary>
    public EntProtoId? GetFirstOrNull(
        EntityTablePrototype entTableProto,
        IRobustRandom? rand = null,
        EntityTableContext? ctx = null
    )
    {
        return GetSpawns(entTableProto, rand, ctx).FirstOrNull();
    }

    /// <summary>
    /// Iterates once through Entities that EntitySelector provides and picks first one.
    /// </summary>
    public EntProtoId? GetFirstOrNull(
        EntityTableSelector? entTableProto,
        IRobustRandom? rand = null,
        EntityTableContext? ctx = null
    )
    {
        return GetSpawns(entTableProto, rand, ctx).FirstOrNull();
    }

    /// <summary>
    /// Compiles a random list of entity prototypes using constraints.
    /// </summary>
    public IEnumerable<EntProtoId> GetSpawns(EntityTablePrototype entTableProto, IRobustRandom? rand = null, EntityTableContext? ctx = null)
    {
        // convenient
        return GetSpawns(entTableProto.Table, rand, ctx);
    }

    /// <summary>
    /// Compiles a random list of entity prototypes using constraints.
    /// </summary>
    public IEnumerable<EntProtoId> GetSpawns(EntityTableSelector? table, IRobustRandom? rand = null, EntityTableContext? ctx = null)
    {
        if (table == null)
            return new List<EntProtoId>();

        rand ??= _random;
        ctx ??= new EntityTableContext();
        return table.GetSpawns(rand, EntityManager, ProtoMan, ctx);
    }

    // WOLFGATE(Xenoarch) START: the EntityTable upgrade keeps the old System.Random overload for existing callers
    /// <summary>
    /// Compiles a random list of entity prototypes, seeding the roll from a <see cref="System.Random"/>.
    /// </summary>
    public IEnumerable<EntProtoId> GetSpawns(EntityTableSelector? table, System.Random rand, EntityTableContext? ctx = null)
    {
        var robust = new RobustRandom();
        robust.SetSeed(rand.Next());
        return GetSpawns(table, robust, ctx);
    }
    // WOLFGATE END

    // WOLFGATE(Xenoarch): wizden's EntityTable upgrade, spawn listings
    /// <summary>
    /// Builds a list of all the spawns in an EntityTable as keys, and their modified weights as values.
    /// </summary>
    public IEnumerable<(EntProtoId spawn, double)> ListSpawns(EntityTablePrototype entTableProto, EntityTableContext? ctx = null)
    {
        return ListSpawns(entTableProto.Table, ctx);
    }

    /// <summary>
    /// Builds a list of all the spawns in an EntityTable as keys, and their modified weights as values.
    /// </summary>
    /// <param name="table">Table we're examining</param>
    /// <param name="ctx">Optional extra context</param>
    public IEnumerable<(EntProtoId spawn, double)> ListSpawns(EntityTableSelector? table, EntityTableContext? ctx = null)
    {
        if (table == null)
            return new List<(EntProtoId spawn, double)>();

        ctx ??= new EntityTableContext();
        return table.ListSpawns(EntityManager, ProtoMan, ctx);
    }
}
