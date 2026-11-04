using System.Numerics;
using Content.Server._WF.Planets;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._WF.PlanetCargoHubs;
using Content.Shared._WF.Planets;
using Content.Shared._WF.PlanetScan;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetCargoHubs;

/// <summary>Makes a planetside depot hard to miss: one announcement a round and an FTL beacon by its world.</summary>
public sealed partial class WFPlanetCargoHubSystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly EntProtoId BeaconProto = "WFCargoHubFtlBeacon";

    /// <summary>The share of the world's drawn radius the beacon sits south of its centre, so the two labels part.</summary>
    private const float BeaconOffsetShare = 0.5f;

    /// <summary>The beacon's offset from a world the sector's star system doesn't draw, in metres.</summary>
    private const float BeaconFallbackOffset = 200f;

    private void InitializeNotice()
    {
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<WFPlanetCargoHubComponent, ComponentShutdown>(OnHubShutdown);
    }

    private void OnRoundStarted(RoundStartedEvent ev) => AnnounceHubs();

    private void OnHubShutdown(Entity<WFPlanetCargoHubComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Beacon is { } beacon && !TerminatingOrDeleted(beacon))
            QueueDel(beacon);
    }

    /// <summary>A hub's station was just made: give it a beacon, and tell everyone if the round is already under way.</summary>
    private void OnHubMade(Entity<WFPlanetCargoHubComponent> hub)
    {
        if (WorldBody(hub.Comp.Ground) is { } body)
        {
            hub.Comp.World = Name(body);
            hub.Comp.Beacon = SpawnBeacon(hub, body);
        }

        if (_ticker.RunLevel == GameRunLevel.InRound)
            AnnounceHubs();
    }

    /// <summary>Announces every planetside depot not yet announced this round; returns how many it announced.</summary>
    public int AnnounceHubs()
    {
        var count = 0;
        var query = EntityQueryEnumerator<WFPlanetCargoHubComponent>();
        while (query.MoveNext(out var uid, out var hub))
        {
            if (hub.Announced || TerminatingOrDeleted(uid))
                continue;

            hub.Announced = true;
            count++;
            _chat.DispatchGlobalAnnouncement(Notice(hub), Name(uid), colorOverride: WFPlanetCargoHubSites.Colour);
        }

        return count;
    }

    /// <summary>"Cargo Depot A has set down on Merak; land on its berths."</summary>
    public string Notice(WFPlanetCargoHubComponent hub)
    {
        return Loc.GetString("wf-planet-cargo-hub-notice", ("depot", Loc.GetString(SiteName(hub.Index))), ("world", hub.World));
    }

    /// <summary>The note a trade crate bound for a planetside depot adds on examine, or null for any other destination.</summary>
    public string? CrateNote(EntityUid destination)
    {
        return TryComp<WFPlanetCargoHubComponent>(destination, out var hub) ? Notice(hub) : null;
    }

    /// <summary>The sector body a world's ground belongs to.</summary>
    private EntityUid? WorldBody(EntityUid ground)
    {
        var networks = EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (networks.MoveNext(out var network))
        {
            if (network.GroundMap == ground && network.Planet is { } body && !TerminatingOrDeleted(body))
                return body;
        }

        return null;
    }

    /// <summary>
    /// An FTL beacon on the sector map just inside the world's disc, named after the depot, so pilots find it in the
    /// destination list and on the map the way they find the space depots.
    /// </summary>
    private EntityUid? SpawnBeacon(Entity<WFPlanetCargoHubComponent> hub, EntityUid body)
    {
        var xform = Transform(body);
        if (xform.MapUid is not { } map)
            return null;

        var centre = _transform.GetWorldPosition(xform);
        var offset = BeaconFallbackOffset;
        if (TryComp<StarSystemMapComponent>(map, out var stars) && stars.StarSystem is { } system
            && TryComp<WFSectorPlanetComponent>(body, out var sector) && sector.Surface is { } surfaceId
            && _proto.TryIndex(surfaceId, out var surface))
        {
            foreach (var planet in system.Planets)
            {
                if ((planet.Position - xform.LocalPosition).LengthSquared() < 1f)
                    offset = WFPlanetScanReader.ScopeRadius(planet, surface) * BeaconOffsetShare;
            }
        }

        var beacon = Spawn(BeaconProto, new EntityCoordinates(map, centre - new Vector2(0f, offset)));
        _meta.SetEntityName(beacon, Loc.GetString("wf-planet-cargo-hub-beacon",
            ("depot", Loc.GetString(SiteName(hub.Comp.Index))), ("world", hub.Comp.World)));
        return beacon;
    }
}
