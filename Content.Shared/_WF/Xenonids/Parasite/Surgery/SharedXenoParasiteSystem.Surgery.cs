using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Parasite;

// Larva removal, reused by the surgery steps, the autodoc and admin tools.
public abstract partial class SharedXenoParasiteSystem
{
    /// <summary>What a removal leaves when the larva has not yet hatched.</summary>
    public static readonly EntProtoId DeadLarvaItem = "RMCXenoEmbryo";

    /// <summary>Marks the larva's roots as cut, which the removal step requires. Server only.</summary>
    public bool TryCutLarvaRoots(EntityUid host)
    {
        if (_net.IsClient || !TryComp(host, out VictimInfectedComponent? infected) || infected.IsBursting)
            return false;

        infected.RootsCut = true;
        Dirty(host, infected);
        return true;
    }

    /// <summary>
    ///     Ends the infection: a hatched larva is freed beside the host, an unhatched one leaves a dead embryo.
    ///     Server only.
    /// </summary>
    /// <returns>False if the host is not infected or the larva is already bursting.</returns>
    public bool TryRemoveLarva(EntityUid host, EntityUid? surgeon = null)
    {
        if (_net.IsClient || !TryComp(host, out VictimInfectedComponent? infected) || infected.IsBursting)
            return false;

        var coords = _transform.GetMoverCoordinates(host);

        if (infected.SpawnedLarva != null)
        {
            if (_container.TryGetContainer(host, infected.LarvaContainerId, out var container))
            {
                foreach (var larva in container.ContainedEntities)
                    RemCompDeferred<BursterComponent>(larva);

                _container.EmptyContainer(container, destination: coords);
            }
        }
        else
        {
            SpawnAtPosition(DeadLarvaItem, coords);
        }

        RemComp<VictimInfectedComponent>(host);

        if (surgeon != null)
            _popup.PopupEntity(Loc.GetString("wf-xeno-surgery-larva-removed"), host, surgeon.Value, PopupType.Medium);

        return true;
    }
}
