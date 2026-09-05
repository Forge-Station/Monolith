using System.Collections.Generic;
using Content.Server._Forge.BlackMarket.Components;
using Content.Shared._Forge.BlackMarket.Prototypes;
using Content.Shared._NF.CrateMachine.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.CrateMachine;

public sealed partial class CrateMachineSystem
{
    [Dependency] private MetaDataSystem _meta = default!;

    partial void DeliverBlackMarket(EntityUid uid, CrateMachineComponent comp)
    {
        if (!TryComp<BlackMarketCrateSpawnerComponent>(uid, out var spawner) || spawner.Pending is not { } pending)
            return;

        var crate = SpawnCrate(uid, pending.Crate);
        _meta.SetEntityName(crate, Loc.GetString(pending.DisplayName));

        var coords = Transform(crate).Coordinates;
        foreach (var entry in pending.Contents)
        {
            for (var i = 0; i < entry.Amount; i++)
            {
                var item = Spawn(entry.Proto, coords);
                InsertIntoCrate(item, crate);
            }
        }

        spawner.Pending = null;
    }

    public void QueueBlackMarketDelivery(
        EntityUid machine,
        EntProtoId cratePrototype,
        BlackMarketContractPrototype contract)
    {
        var spawner = EnsureComp<BlackMarketCrateSpawnerComponent>(machine);
        spawner.Pending = new BlackMarketPendingCrateDelivery
        {
            Crate = cratePrototype,
            DisplayName = contract.Name,
            Contents = new List<BlackMarketContractContentEntry>(contract.Contents),
        };
    }
}
