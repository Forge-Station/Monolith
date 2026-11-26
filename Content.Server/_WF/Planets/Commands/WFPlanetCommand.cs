using System.Linq;
using System.Numerics;
using Content.Server._FarHorizons.StarSystem;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.Administration;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets.Commands;

/// <summary>Builds, lists and deletes planet networks, and seeds a star system for dev environments.</summary>
[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed partial class WFPlanetCommand : LocalizedEntityCommands
{
    [Dependency] private WFPlanetRegistrySystem _registry = default!;
    [Dependency] private WFPlanetNetworkSystem _networks = default!;
    [Dependency] private StarSystemMapSystem _starSystem = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;

    private const string SubList = "list";
    private const string SubBuild = "build";
    private const string SubDelete = "delete";
    private const string SubSpawn = "spawn";
    private const string SubSystem = "system";
    private const string SubTp = "tp";

    private static readonly string[] Subcommands = { SubList, SubBuild, SubDelete, SubSpawn, SubSystem, SubTp };

    /// <inheritdoc/>
    public override string Command => WolfgateAdminCommands.Planet;

    /// <inheritdoc/>
    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0 || !Subcommands.Contains(args[0]))
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        // Everything but the read-only listing needs the feature switched on.
        if (args[0] != SubList && !_cfg.GetCVar(PlanetCVars.PlanetNetworks))
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-disabled"));
            return;
        }

        if (args[0] != SubList && args.Length != 2)
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        switch (args[0])
        {
            case SubList:
                ExecuteList(shell);
                break;
            case SubBuild:
                ExecuteBuild(shell, args[1]);
                break;
            case SubDelete:
                ExecuteDelete(shell, args[1]);
                break;
            case SubSpawn:
                ExecuteSpawn(shell, args[1]);
                break;
            case SubSystem:
                ExecuteSystem(shell, args[1]);
                break;
            case SubTp:
                ExecuteTp(shell, args[1]);
                break;
        }
    }

    /// <summary>Prints every registered sector body with its surface and network, if any.</summary>
    private void ExecuteList(IConsoleShell shell)
    {
        var planets = _registry.GetPlanets();

        if (planets.Count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-wfplanet-empty"));
            return;
        }

        var none = Loc.GetString("cmd-wfplanet-row-none");

        foreach (var planet in planets)
        {
            var surface = planet.Comp.Surface?.Id ?? none;
            var network = planet.Comp.Network is { } net ? net.ToString() : none;

            shell.WriteLine(Loc.GetString("cmd-wfplanet-row",
                ("planet", EntityManager.GetComponent<MetaDataComponent>(planet.Owner).EntityName),
                ("surface", surface),
                ("network", network)));
        }
    }

    /// <summary>Builds the network of one registered sector body.</summary>
    private void ExecuteBuild(IConsoleShell shell, string name)
    {
        if (!_registry.TryGetPlanetByName(name, out var planet))
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-unknown-planet", ("planet", name)));
            return;
        }

        if (planet.Comp.Surface is null)
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-no-surface", ("planet", name)));
            return;
        }

        if (planet.Comp.Network is { } existing)
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-already-built", ("planet", name), ("network", existing.ToString())));
            return;
        }

        if (!_networks.TryBuildNetwork(planet, out var network))
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-build-failed", ("planet", name)));
            return;
        }

        ReportBuilt(shell, name, network);

        _adminLogger.Add(LogType.AdminCommands, LogImpact.High,
            $"{shell.Player?.Name ?? "Server"} built a planet network for {EntityManager.ToPrettyString(planet.Owner)}");
    }

    /// <summary>Builds a standalone network from a surface prototype, for dev environments with no sector planets.</summary>
    private void ExecuteSpawn(IConsoleShell shell, string surfaceId)
    {
        if (!_proto.TryIndex<WFPlanetSurfacePrototype>(surfaceId, out var surface))
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-unknown-surface", ("surface", surfaceId)));
            return;
        }

        var displayName = _proto.TryIndex(surface.Ground, out var groundProto)
            ? Loc.GetString(groundProto.MapName)
            : surface.ID;

        if (_networks.BuildNetwork(surface, Vector2.Zero, displayName, null) is not { } network)
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-build-failed", ("planet", surfaceId)));
            return;
        }

        ReportBuilt(shell, surfaceId, network);

        _adminLogger.Add(LogType.AdminCommands, LogImpact.High,
            $"{shell.Player?.Name ?? "Server"} built a standalone planet network from {surfaceId}");
    }

    /// <summary>Tears down the network of a sector body, or of a network entity given by id.</summary>
    private void ExecuteDelete(IConsoleShell shell, string target)
    {
        EntityUid network;

        if (_registry.TryGetPlanetByName(target, out var planet))
        {
            if (planet.Comp.Network is not { } net || !EntityManager.TryGetEntity(net, out var resolved))
            {
                shell.WriteError(Loc.GetString("cmd-wfplanet-not-built", ("planet", target)));
                return;
            }

            network = resolved.Value;
        }
        else if (int.TryParse(target, out var id)
                 && EntityManager.TryGetEntity(new NetEntity(id), out var standalone)
                 && EntityManager.HasComponent<WFPlanetNetworkComponent>(standalone))
        {
            network = standalone.Value;
        }
        else
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-unknown-planet", ("planet", target)));
            return;
        }

        _networks.DeleteNetwork(network);
        shell.WriteLine(Loc.GetString("cmd-wfplanet-deleted", ("planet", target)));

        _adminLogger.Add(LogType.AdminCommands, LogImpact.High,
            $"{shell.Player?.Name ?? "Server"} deleted the planet network {network}");
    }

    /// <summary>Seeds a star system onto the map the caller is standing on, so sector bodies exist to build from.</summary>
    private void ExecuteSystem(IConsoleShell shell, string systemId)
    {
        if (!_proto.TryIndex<StarSystemPrototype>(systemId, out var system))
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-unknown-system", ("system", systemId)));
            return;
        }

        if (!TryGetPlayerMap(shell, out var map))
            return;

        var comp = EntityManager.EnsureComponent<StarSystemMapComponent>(map);
        _starSystem.SetSystem((map, comp), system.ID);

        shell.WriteLine(Loc.GetString("cmd-wfplanet-system-set",
            ("system", system.ID),
            ("map", EntityManager.ToPrettyString(map).ToString())));

        _adminLogger.Add(LogType.AdminCommands, LogImpact.High,
            $"{shell.Player?.Name ?? "Server"} set star system {system.ID} on {EntityManager.ToPrettyString(map)}");
    }

    /// <summary>Moves the caller to the centre of a planet's orbit layer.</summary>
    private void ExecuteTp(IConsoleShell shell, string name)
    {
        if (!_registry.TryGetPlanetByName(name, out var planet))
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-unknown-planet", ("planet", name)));
            return;
        }

        if (planet.Comp.Network is not { } net
            || !EntityManager.TryGetEntity(net, out var network)
            || !EntityManager.TryGetComponent<WFPlanetNetworkComponent>(network, out var comp))
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-not-built", ("planet", name)));
            return;
        }

        if (shell.Player?.AttachedEntity is not { } attached)
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-no-map"));
            return;
        }

        _transform.SetCoordinates(attached, new EntityCoordinates(comp.OrbitMap, comp.Centre));
        shell.WriteLine(Loc.GetString("cmd-wfplanet-tp-done", ("planet", name)));

        _adminLogger.Add(LogType.AdminCommands, LogImpact.High,
            $"{shell.Player?.Name ?? "Server"} teleported to the orbit layer of {EntityManager.ToPrettyString(planet.Owner)}");
    }

    /// <summary>Writes the "built N layers" line for a freshly built network.</summary>
    private void ReportBuilt(IConsoleShell shell, string planet, EntityUid network)
    {
        var layers = EntityManager.TryGetComponent<WFPlanetNetworkComponent>(network, out var comp)
            ? comp.Layers.Count
            : 0;
        var orbit = comp is null ? network : comp.OrbitMap;

        shell.WriteLine(Loc.GetString("cmd-wfplanet-built",
            ("layers", layers),
            ("planet", planet),
            ("network", EntityManager.ToPrettyString(network).ToString()),
            ("orbit", EntityManager.ToPrettyString(orbit).ToString())));
    }

    /// <summary>Resolves the map the calling player is standing on.</summary>
    private bool TryGetPlayerMap(IConsoleShell shell, out EntityUid map)
    {
        map = EntityUid.Invalid;

        if (shell.Player?.AttachedEntity is not { } attached
            || EntityManager.GetComponent<TransformComponent>(attached).MapUid is not { } mapUid)
        {
            shell.WriteError(Loc.GetString("cmd-wfplanet-no-map"));
            return false;
        }

        map = mapUid;
        return true;
    }

    /// <inheritdoc/>
    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                return CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wfplanet-hint-sub"));
            case 2:
                switch (args[0])
                {
                    case SubBuild:
                    case SubDelete:
                    case SubTp:
                        var names = _registry.GetPlanets()
                            .Select(planet => EntityManager.GetComponent<MetaDataComponent>(planet.Owner).EntityName)
                            .OrderBy(name => name);
                        return CompletionResult.FromHintOptions(names, Loc.GetString("cmd-wfplanet-hint-planet"));
                    case SubSpawn:
                        return CompletionResult.FromHintOptions(
                            _proto.EnumeratePrototypes<WFPlanetSurfacePrototype>().Select(proto => proto.ID).Order(),
                            Loc.GetString("cmd-wfplanet-hint-surface"));
                    case SubSystem:
                        return CompletionResult.FromHintOptions(
                            _proto.EnumeratePrototypes<StarSystemPrototype>().Select(proto => proto.ID).Order(),
                            Loc.GetString("cmd-wfplanet-hint-system"));
                    default:
                        return CompletionResult.Empty;
                }
            default:
                return CompletionResult.Empty;
        }
    }
}
