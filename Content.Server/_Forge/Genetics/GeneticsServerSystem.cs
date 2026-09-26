using Content.Server._Forge.Genetics.Components;
using Content.Server.DeviceLinking.Systems;

namespace Content.Server._Forge.Genetics;

/// <summary>
/// A console writes discoveries only to the DNA servers linked to it.
/// The link is made with a multitool or a network configurator, not by sharing a grid.
/// </summary>
public sealed partial class GeneticsServerSystem : EntitySystem
{
    [Dependency] private DeviceLinkSystem _deviceLink = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticsServerComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<GeneticsServerComponent> ent, ref ComponentInit args)
    {
        _deviceLink.EnsureSinkPorts(ent, GeneticsServerComponent.Port);
    }

    public bool IsDiscovered(EntityUid context, string geneId)
    {
        foreach (var server in ResolveServers(context))
        {
            if (server.Comp.Discovered.Contains(geneId))
                return true;
        }

        return false;
    }

    /// <returns>
    /// False only when a console is in the chain and none of its linked servers could record the gene.
    /// A body outside a scanner does not need a server.
    /// </returns>
    public bool Discover(EntityUid context, string geneId)
    {
        var servers = ResolveServers(context);
        if (servers.Count == 0)
            return FindConsole(context) == null;

        foreach (var server in servers)
            server.Comp.Discovered.Add(geneId);

        return true;
    }

    public HashSet<string> GetDiscovered(EntityUid context)
    {
        var genes = new HashSet<string>();
        foreach (var server in ResolveServers(context))
            genes.UnionWith(server.Comp.Discovered);

        return genes;
    }

    private List<Entity<GeneticsServerComponent>> ResolveServers(EntityUid context)
    {
        var servers = new List<Entity<GeneticsServerComponent>>();
        if (FindConsole(context) is not { } console)
            return servers;

        foreach (var serverUid in console.Comp.Servers)
        {
            if (TryComp<GeneticsServerComponent>(serverUid, out var server))
                servers.Add((serverUid, server));
        }

        return servers;
    }

    private Entity<GeneticsConsoleComponent>? FindConsole(EntityUid context)
    {
        if (TryComp<GeneticsConsoleComponent>(context, out var console))
            return (context, console);

        if (TryComp<DnaModifierScannerComponent>(context, out var scanner))
            return ConsoleOf(scanner);

        if (!TryComp(context, out TransformComponent? xform))
            return null;

        if (TryComp<DnaModifierScannerComponent>(xform.ParentUid, out var parentScanner))
            return ConsoleOf(parentScanner);

        return null;
    }

    private Entity<GeneticsConsoleComponent>? ConsoleOf(DnaModifierScannerComponent scanner)
    {
        if (scanner.ConnectedConsole is not { } consoleUid)
            return null;

        return TryComp<GeneticsConsoleComponent>(consoleUid, out var console)
            ? (consoleUid, console)
            : null;
    }
}
