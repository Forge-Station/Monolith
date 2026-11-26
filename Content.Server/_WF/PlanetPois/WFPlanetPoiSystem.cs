using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Caverns;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Parallax;
using Content.Server.Popups;
using Content.Server.Procedural;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.Ghost;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Parallax.Biomes;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetPois;

/// <summary>Places pinned points of interest and connecting roads before terrain, with discoverable orbit signals.</summary>
public sealed partial class WFPlanetPoiSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private DungeonSystem _dungeon = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedRoofSystem _roof = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFPlanetPreloadSystem _preload = default!;

    /// <summary>The orbit-layer marker over a site.</summary>
    public const string SignalProto = "WFPoiSignal";

    private static readonly TimeSpan RevealInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextReveal;

    public override void Initialize()
    {
        base.Initialize();
        // Ahead of the cavern mouths, so a fixed city can hold its spot before they claim the ground.
        SubscribeLocalEvent<WFPlanetPreloadStartingEvent>(OnPreloadStarting, before: new[] { typeof(WFCavernMouthSystem) });
        SubscribeLocalEvent<WFPlanetPreloadedEvent>(OnPreloaded);
    }

    private void OnPreloadStarting(ref WFPlanetPreloadStartingEvent args)
    {
        if (!TryComp<WFPlanetLayerComponent>(args.Ground, out var layer)
            || !TryGetEntity(layer.Network, out var network)
            || !TryComp<WFPlanetNetworkComponent>(network, out var comp)
            || !_proto.TryIndex(comp.Surface, out var surface))
            return;

        var forced = _cfg.GetCVar(PlanetCVars.PoiTable);
        var tableId = forced.Length > 0 ? new ProtoId<WFPlanetPoiTablePrototype>(forced) : surface.Pois;

        if (tableId is not { } id)
            return;
        if (!_proto.TryIndex(id, out var table))
        {
            var error = $"Unknown POI table {id}; correct the surface prototype or poi_table override.";
            EnsureComp<WFPlanetPoiComponent>(args.Ground).GenerationError = error;
            Log.Error(error);
            args.Holds.Add(Task.FromException(new InvalidOperationException(error)));
            return;
        }

        args.Holds.Add(PlaceAsync(args.Ground, comp, args.Centre, args.Radius, table, args.Claims));
    }

    /// <summary>The marker on the orbit layer over a site, unidentified until someone on the ground reaches it.</summary>
    private EntityUid SpawnSignal(EntityUid ground, EntityUid orbit, Vector2i origin, WFPlanetPoiEntry entry, WFPlanetPoiTablePrototype table)
    {
        if (TerminatingOrDeleted(orbit))
            return EntityUid.Invalid;

        var signal = Spawn(SignalProto, new EntityCoordinates(orbit, new Vector2(origin.X + 0.5f, origin.Y + 0.5f)));
        _meta.SetEntityName(signal, Loc.GetString("wf-poi-unknown-signal"));

        var comp = EnsureComp<WFPoiSignalComponent>(signal);
        comp.Ground = ground;
        comp.Origin = origin;
        comp.Name = entry.Name;
        comp.RevealRange = table.RevealRange;
        return signal;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextReveal)
            return;

        _nextReveal = _timing.CurTime + RevealInterval;

        var query = EntityQueryEnumerator<WFPoiSignalComponent>();
        while (query.MoveNext(out var uid, out var signal))
        {
            if (signal.Revealed)
                continue;

            var site = new Vector2(signal.Origin.X + 0.5f, signal.Origin.Y + 0.5f);

            foreach (var session in _players.Sessions)
            {
                // Ghosts and admin observers only look; a site is found by someone there in body.
                if (session.AttachedEntity is not { } player
                    || HasComp<GhostComponent>(player)
                    || Transform(player).MapUid != signal.Ground
                    || Vector2.DistanceSquared(_transform.GetWorldPosition(player), site) > signal.RevealRange * signal.RevealRange)
                    continue;

                Reveal((uid, signal), player);
                break;
            }
        }
    }

    /// <summary>Names the signal after its site and tells the finder.</summary>
    public void Reveal(Entity<WFPoiSignalComponent> signal, EntityUid? finder)
    {
        var name = Loc.GetString(signal.Comp.Name);
        signal.Comp.Revealed = true;
        _meta.SetEntityName(signal, name);

        if (finder is { } player)
            _popup.PopupEntity(Loc.GetString("wf-poi-signal-identified", ("name", name)), player, player);
    }
}
