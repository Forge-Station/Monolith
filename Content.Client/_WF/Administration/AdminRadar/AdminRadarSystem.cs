using Content.Shared._WF.Administration.AdminRadar;
using Content.Shared._WF.PlanetMap;
using Content.Shared._WF.PlanetSignals;
using Content.Shared.Ghost;
using Robust.Shared.Timing;

namespace Content.Client._WF.Administration.AdminRadar;

/// <summary>
/// Holds the admin mass scanner's settings and its list of players.
/// </summary>
public sealed partial class AdminRadarSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SitesRequestInterval = TimeSpan.FromSeconds(5);

    private List<AdminRadarPlayer> _players = new();
    private TimeSpan _nextRequest;
    private List<AdminRadarWorldSites> _worlds = new();
    private TimeSpan _nextSitesRequest;
    private readonly Dictionary<(NetEntity Ground, Vector2i Position), string> _names = new();

    /// <summary>
    /// Whether the scanner draws grids, beacons and planet sites as they are, whatever their IFF hides or nobody has
    /// identified yet. Kept here, like
    /// <see cref="ShowPlayers"/>, so it survives the window closing.
    /// </summary>
    public bool TrueIff = true;

    /// <summary>
    /// Whether the scanner marks the players on its map.
    /// </summary>
    public bool ShowPlayers;

    /// <summary>
    /// The players as the server last listed them.
    /// </summary>
    public IReadOnlyList<AdminRadarPlayer> Players => _players;

    /// <summary>
    /// A new player list arrived.
    /// </summary>
    public event Action? PlayersUpdated;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<AdminRadarPlayersEvent>(OnPlayers);
        SubscribeNetworkEvent<AdminRadarSitesEvent>(OnSites);
    }

    private void OnPlayers(AdminRadarPlayersEvent ev)
    {
        _players = ev.Players;
        PlayersUpdated?.Invoke();
    }

    private void OnSites(AdminRadarSitesEvent ev)
    {
        _worlds = ev.Worlds;
    }

    /// <summary>
    /// Asks the server what every planet site really is, at most every few seconds unless forced.
    /// </summary>
    public void RequestSites(bool force = false)
    {
        if (!force && _timing.RealTime < _nextSitesRequest)
            return;

        _nextSitesRequest = _timing.RealTime + SitesRequestInterval;
        RaiseNetworkEvent(new AdminRadarSitesRequestEvent());
    }

    /// <summary>
    /// Swaps every world map's and signal list's sites for the real ones for one radar draw, noting what was there.
    /// The radar code reads them straight off the components, as it does IFF; nothing is dirtied, and
    /// <see cref="RestoreSites"/> must get the same record back before the draw returns.
    /// </summary>
    public void RevealSites(AdminRadarSwappedSites swapped)
    {
        _names.Clear();
        foreach (var world in _worlds)
        {
            if (!TryGetEntity(world.Orbit, out var orbit) || !TryComp<WFPlanetMapComponent>(orbit, out var map))
                continue;

            swapped.Maps.Add((map, map.Sites));
            map.Sites = world.Sites;
            foreach (var site in world.Sites)
                _names[(world.Ground, site.Position)] = site.Name;
        }

        if (_names.Count == 0)
            return;

        // With the map off, the signal overlay marks the sites; it names them from the signal list.
        var query = AllEntityQuery<WFPlanetSignalsComponent>();
        while (query.MoveNext(out var signals))
        {
            var named = new List<WFPlanetSignal>(signals.Signals.Count);
            foreach (var signal in signals.Signals)
            {
                named.Add(signal.Kind == WFPlanetSignalKind.Site && signal.Name.Length == 0
                    && _names.TryGetValue((signal.Map, signal.Position), out var name)
                    ? signal with { Name = name }
                    : signal);
            }

            swapped.Signals.Add((signals, signals.Signals));
            signals.Signals = named;
        }
    }

    /// <summary>
    /// Puts back the sites <see cref="RevealSites"/> swapped out.
    /// </summary>
    public void RestoreSites(AdminRadarSwappedSites swapped)
    {
        foreach (var (map, sites) in swapped.Maps)
            map.Sites = sites;
        foreach (var (signals, list) in swapped.Signals)
            signals.Signals = list;

        swapped.Maps.Clear();
        swapped.Signals.Clear();
    }

    /// <summary>
    /// Asks the server where the players are, at most once a second unless forced.
    /// </summary>
    public void RequestPlayers(bool force = false)
    {
        if (!force && _timing.RealTime < _nextRequest)
            return;

        _nextRequest = _timing.RealTime + RequestInterval;
        RaiseNetworkEvent(new AdminRadarPlayersRequestEvent());
    }

    /// <summary>
    /// Warps the local ghost to a player, the way the ghost warp menu does.
    /// </summary>
    public void JumpTo(NetEntity player)
    {
        RaiseNetworkEvent(new GhostWarpToTargetRequestEvent(player));
    }
}

/// <summary>
/// The site lists <see cref="AdminRadarSystem.RevealSites"/> swapped out, kept to put back.
/// </summary>
public sealed class AdminRadarSwappedSites
{
    public readonly List<(WFPlanetMapComponent Map, List<WFPlanetMapSite> Sites)> Maps = new();
    public readonly List<(WFPlanetSignalsComponent Signals, List<WFPlanetSignal> List)> Signals = new();
}
