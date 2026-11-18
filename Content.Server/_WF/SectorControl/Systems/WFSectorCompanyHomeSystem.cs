using System.Numerics;
using Content.Server._WF.SectorControl.Components;
using Content.Server.GameTicking;
using Content.Shared._Mono.Company;
using Content.Shared._WF.SectorControl;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SectorControl.Systems;

/// <summary>
/// Paints the origin hex for NanoTrasen and the hex of each СИВ, TSF and Renegade station for that company.
/// The colour stays while the source does, and a player beacon cannot paint over it.
/// </summary>
public sealed class WFSectorCompanyHomeSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    /// <summary>NanoTrasen. The company id is Colonial; the map shows the company name.</summary>
    private const string CentreCompany = "Colonial";

    private static readonly Dictionary<string, string> CompaniesByTag = new()
    {
        ["Imperial"] = "Imperial",
        ["TSF"] = "TSF",
        ["Renegates"] = "Renegates",
    };

    private EntityUid? _centre;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<WFSectorResetEvent>(OnSectorReset);
        SubscribeLocalEvent<WFSectorRetakeAttemptEvent>(OnRetake);
    }

    private void OnRoundStarted(RoundStartedEvent args)
    {
        EnsureCentre();
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _centre = null;
    }

    private void OnSectorReset(ref WFSectorResetEvent args)
    {
        EnsureCentre();
        var homes = new List<(EntityUid Source, ProtoId<CompanyPrototype> Company, Vector2 Position)>();
        var query = EntityQueryEnumerator<WFSectorCompanyHomeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var home, out var xform))
        {
            homes.Add((uid, home.Company, _transform.GetWorldPosition(xform)));
        }

        foreach (var (source, company, position) in homes)
        {
            Claim(source, company, position);
        }
    }

    /// <summary>Claims the hex a faction station was placed in, when the station is one of theirs.</summary>
    public void ClaimPoi(EntityUid grid, IReadOnlyList<string> tags)
    {
        EnsureCentre();
        foreach (var tag in tags)
        {
            if (!CompaniesByTag.TryGetValue(tag, out var company))
                continue;

            Claim(grid, company, _transform.GetWorldPosition(grid));
            return;
        }
    }

    /// <summary>A home source is the faction's hold. Parking a ship in the hex does not take the colour.</summary>
    private void OnRetake(ref WFSectorRetakeAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        foreach (var source in _territory.Sources(args.Map, args.Cell))
        {
            if (HasComp<WFSectorCompanyHomeComponent>(source))
            {
                args.Cancelled = true;
                return;
            }
        }
    }

    private void EnsureCentre()
    {
        var map = _territory.SectorMap;
        if (map == MapId.Nullspace || !_map.TryGetMap(map, out var mapUid) || mapUid is not { } uid)
            return;

        if (_centre is not { } centre || TerminatingOrDeleted(centre))
        {
            centre = Spawn(null, new EntityCoordinates(uid, Vector2.Zero));
            _meta.SetEntityName(centre, Loc.GetString("wf-sector-home-nt"));
            _centre = centre;
        }

        Claim(centre, CentreCompany, Vector2.Zero);
    }

    private void Claim(EntityUid source, ProtoId<CompanyPrototype> company, Vector2 position)
    {
        var map = _territory.SectorMap;
        if (map == MapId.Nullspace || !FactionFor(company, out var faction))
            return;

        var home = EnsureComp<WFSectorCompanyHomeComponent>(source);
        home.Company = company;
        var cell = _territory.CellOf(new MapCoordinates(position, map));
        _territory.TryClaim(map, cell, faction.ID, source);
    }

    private bool FactionFor(ProtoId<CompanyPrototype> company, out WFSectorFactionPrototype faction)
    {
        foreach (var proto in _prototypes.EnumeratePrototypes<WFSectorFactionPrototype>())
        {
            if (proto.PlayerBeacons && proto.Company is { } id && id == company)
            {
                faction = proto;
                return true;
            }
        }

        faction = default!;
        return false;
    }
}
