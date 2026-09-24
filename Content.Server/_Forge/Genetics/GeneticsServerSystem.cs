using Content.Server._Forge.Genetics.Components;

namespace Content.Server._Forge.Genetics;

/// <summary>
/// DNA discoveries live on a server machine and only apply to its grid,
/// the same way an R&amp;D server does not teach every station.
/// </summary>
public sealed class GeneticsServerSystem : EntitySystem
{
    public bool IsDiscovered(EntityUid context, string geneId)
    {
        foreach (var server in ServersOnSameGrid(context))
        {
            if (server.Comp.Discovered.Contains(geneId))
                return true;
        }

        return false;
    }

    /// <returns>True when at least one server on the grid recorded the gene.</returns>
    public bool Discover(EntityUid context, string geneId)
    {
        var any = false;
        foreach (var server in ServersOnSameGrid(context))
        {
            server.Comp.Discovered.Add(geneId);
            any = true;
        }

        return any;
    }

    public HashSet<string> GetDiscovered(EntityUid context)
    {
        var genes = new HashSet<string>();
        foreach (var server in ServersOnSameGrid(context))
            genes.UnionWith(server.Comp.Discovered);

        return genes;
    }

    private List<Entity<GeneticsServerComponent>> ServersOnSameGrid(EntityUid context)
    {
        var servers = new List<Entity<GeneticsServerComponent>>();
        if (!TryComp(context, out TransformComponent? xform) || xform.GridUid is not { } grid)
            return servers;

        var query = EntityQueryEnumerator<GeneticsServerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var server, out var serverXform))
        {
            if (serverXform.GridUid == grid)
                servers.Add((uid, server));
        }

        return servers;
    }
}
