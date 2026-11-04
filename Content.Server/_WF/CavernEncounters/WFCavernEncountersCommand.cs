using System.Linq;
using System.Numerics;
using Content.Server._WF.Caverns;
using Content.Server._WF.Planets;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.CavernEncounters;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.CavernEncounters;

/// <summary>Lists every world's planned and laid-out cavern encounters with their positions, and teleports to one.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFCavernEncountersCommand : LocalizedEntityCommands
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCavernEncounterSystem _encounters = default!;

    private const string SubGoto = "goto";
    private const string Prefix = "WFCavernEncounter";

    /// <inheritdoc/>
    public override string Command => "cavernencounters";

    /// <inheritdoc/>
    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 2 && args[0] == SubGoto)
        {
            if (!int.TryParse(args[1], out var number))
            {
                shell.WriteError(Loc.GetString("cmd-cavernencounters-bad-number", ("number", args[1])));
                return;
            }

            Goto(shell, number);
            return;
        }

        if (args.Length > 1)
        {
            shell.WriteError(Loc.GetString("cmd-cavernencounters-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        List(shell, args.Length == 1 ? args[0] : null);
    }

    /// <summary>One header per world, then one numbered row per encounter; the numbers are the ones goto takes.</summary>
    private void List(IConsoleShell shell, string? world)
    {
        var sites = AllSites();
        var shown = 0;

        foreach (var group in sites.Select((s, i) => (s, Number: i + 1)).GroupBy(e => e.s.Planet))
        {
            if (world != null && !string.Equals(group.Key, world, StringComparison.OrdinalIgnoreCase))
                continue;

            var all = group.ToList();
            shell.WriteLine(Loc.GetString("cmd-cavernencounters-world",
                ("planet", group.Key),
                ("count", all.Count),
                ("spawned", all.Count(e => e.s.Site.State == WFCavernEncounterState.Spawned)),
                ("skipped", all.Count(e => e.s.Site.State == WFCavernEncounterState.Skipped))));

            foreach (var (entry, number) in all)
            {
                shell.WriteLine(Loc.GetString("cmd-cavernencounters-row",
                    ("number", number),
                    ("encounter", ShortName(entry.Site.Encounter)),
                    ("state", entry.Site.State.ToString().ToLowerInvariant()),
                    ("planned", entry.Site.Planned.ToString()),
                    ("centre", entry.Site.Centre?.ToString() ?? Loc.GetString("cmd-cavernencounters-none"))));
            }

            shown++;
        }

        if (shown == 0)
        {
            shell.WriteLine(world == null
                ? Loc.GetString("cmd-cavernencounters-empty")
                : Loc.GetString("cmd-cavernencounters-unknown-world", ("planet", world)));
        }
    }

    /// <summary>Lays the encounter out if nobody has been near yet, then moves the caller to its room's edge.</summary>
    private void Goto(IConsoleShell shell, int number)
    {
        var sites = AllSites();
        if (number < 1 || number > sites.Count)
        {
            shell.WriteError(Loc.GetString("cmd-cavernencounters-bad-number", ("number", number)));
            return;
        }

        if (shell.Player?.AttachedEntity is not { } attached)
        {
            shell.WriteError(Loc.GetString("cmd-cavernencounters-no-entity"));
            return;
        }

        var (planet, cavern, site) = sites[number - 1];
        if (site.State == WFCavernEncounterState.Planned)
            _encounters.TryActivate(cavern, site);

        var radius = _proto.TryIndex(site.Encounter, out var encounter) ? encounter.Radius : 4;
        var centre = site.Centre ?? site.Planned;

        // The room's south edge, clear of a collapse round the centre.
        var position = new Vector2(centre.X + 0.5f, centre.Y + 0.5f - Math.Max(radius - 1, 0));
        _transform.SetCoordinates(attached, new EntityCoordinates(cavern, position));

        if (EntityManager.TryGetComponent<CEZPhysicsComponent>(attached, out var physics))
        {
            _zLevels.SetZPosition((attached, physics), 0f);
            _zLevels.SetZVelocity((attached, physics), 0f);
        }

        shell.WriteLine(Loc.GetString("cmd-cavernencounters-goto-done",
            ("number", number),
            ("encounter", ShortName(site.Encounter)),
            ("planet", planet),
            ("state", site.State.ToString().ToLowerInvariant())));

        _adminLogger.Add(LogType.AdminCommands, LogImpact.Medium,
            $"{shell.Player?.Name ?? "Server"} teleported to cavern encounter {site.Encounter} at {centre} on {EntityManager.ToPrettyString(cavern)}");
    }

    /// <summary>Every planned encounter of every world, ordered by world name and then as planned.</summary>
    private List<(string Planet, EntityUid Cavern, WFCavernEncounterSite Site)> AllSites()
    {
        var worlds = new List<(string Planet, EntityUid Cavern)>();
        var query = EntityManager.EntityQueryEnumerator<WFPlanetNetworkComponent>();

        while (query.MoveNext(out var network, out var comp))
        {
            if (EntityManager.TryGetComponent<WFCavernGroundComponent>(comp.GroundMap, out var ground)
                && EntityManager.HasComponent<WFCavernEncounterLayerComponent>(ground.Cavern))
                worlds.Add((PlanetName(network, comp), ground.Cavern));
        }

        return worlds
            .OrderBy(w => w.Planet, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.Cavern.Id)
            .SelectMany(w => _encounters.GetSites(w.Cavern).Select(site => (w.Planet, w.Cavern, site)))
            .ToList();
    }

    /// <summary>The name a world goes by: its sector body's, the name it was built under, or its surface without the prefix.</summary>
    private string PlanetName(EntityUid network, WFPlanetNetworkComponent comp)
    {
        if (comp.Planet is { } body && EntityManager.EntityExists(body))
            return EntityManager.GetComponent<MetaDataComponent>(body).EntityName;

        if (EntityManager.TryGetComponent<WFPlanetWeatherComponent>(network, out var weather)
            && !string.IsNullOrEmpty(weather.PlanetName))
            return weather.PlanetName;

        const string prefix = "WFSurface";
        var surface = comp.Surface.Id;
        return surface.StartsWith(prefix, StringComparison.Ordinal) ? surface[prefix.Length..] : surface;
    }

    /// <summary>An encounter id without its prefix.</summary>
    private static string ShortName(ProtoId<WFCavernEncounterPrototype> id)
    {
        return id.Id.StartsWith(Prefix, StringComparison.Ordinal) ? id.Id[Prefix.Length..] : id.Id;
    }

    /// <inheritdoc/>
    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                var options = AllSites().Select(s => s.Planet).Distinct().Order().Append(SubGoto);
                return CompletionResult.FromHintOptions(options, Loc.GetString("cmd-cavernencounters-hint-first"));
            case 2 when args[0] == SubGoto:
                return CompletionResult.FromHint(Loc.GetString("cmd-cavernencounters-hint-number"));
            default:
                return CompletionResult.Empty;
        }
    }
}
