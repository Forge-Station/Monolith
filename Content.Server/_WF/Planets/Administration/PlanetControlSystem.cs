using System.Linq;
using Content.Server._CE.ZLevels.Core;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._WF.Planets.Administration;
using Content.Shared._WF.Planets;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Weather;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets.Administration;

/// <summary>The admin Planet Control panel: a world's clock, weather, gravity and sanction, set live.</summary>
public sealed partial class PlanetControlSystem : EntitySystem
{
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private WFPlanetWeatherSystem _weather = default!;
    [Dependency] private CEZLevelsSystem _zLevels = default!;

    /// <summary>Gravity an admin may set, in gees; zero would divide every lift ratio by nothing.</summary>
    public const float MinGravity = 0.05f;
    public const float MaxGravity = 10f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<PlanetControlListRequestEvent>(OnListRequest);
        SubscribeNetworkEvent<PlanetControlSetEvent>(OnSet);
    }

    private void OnListRequest(PlanetControlListRequestEvent ev, EntitySessionEventArgs args)
    {
        if (_adminManager.HasAdminFlag(args.SenderSession, AdminFlags.Admin))
            SendList(args.SenderSession);
    }

    private void OnSet(PlanetControlSetEvent ev, EntitySessionEventArgs args)
    {
        if (!_adminManager.HasAdminFlag(args.SenderSession, AdminFlags.Admin) || !TryGetEntity(ev.Planet, out var planet))
            return;

        Apply(planet.Value, ev, args.SenderSession);
        SendList(args.SenderSession);
    }

    public void SendList(ICommonSession session)
    {
        RaiseNetworkEvent(new PlanetControlListEvent(BuildList()), Filter.SinglePlayer(session));
    }

    public List<PlanetControlInfo> BuildList()
    {
        var list = new List<PlanetControlInfo>();
        var query = AllEntityQuery<WFSectorPlanetComponent>();

        while (query.MoveNext(out var uid, out var sector))
        {
            var info = new PlanetControlInfo
            {
                Planet = GetNetEntity(uid),
                Name = Name(uid),
                Sanctioned = sector.Sanctioned,
            };

            if (TryGetEntity(sector.Network, out var world) && TryComp<WFPlanetNetworkComponent>(world, out var network))
            {
                info.Built = true;
                _weather.TryGetState(world.Value, out info.MinuteOfDay, out info.DaySeconds, out info.Weather, out info.WeatherSeconds);

                if (TryComp<WFPlanetLayerComponent>(network.GroundMap, out var ground))
                    info.Gravity = ground.Gravity;
            }

            list.Add(info);
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>Applies one request. Returns what was changed, already worded for the admin log and the console.</summary>
    public string Apply(EntityUid planet, PlanetControlSetEvent ev, ICommonSession? admin)
    {
        if (!TryComp<WFSectorPlanetComponent>(planet, out var sector))
            return string.Empty;

        var changes = new List<string>();

        if (ev.Sanctioned is { } sanctioned && sector.Sanctioned != sanctioned)
        {
            sector.Sanctioned = sanctioned;
            Dirty(planet, sector);
            changes.Add($"sanctioned={sanctioned}");
        }

        if (TryGetEntity(sector.Network, out var world) && TryComp<WFPlanetNetworkComponent>(world, out var network))
        {
            if (ev.MinuteOfDay is { } minute && _weather.SetMinuteOfDay(world.Value, minute))
                changes.Add($"time={minute / 60:00}:{minute % 60:00}");

            if (ev.SetWeather)
            {
                ProtoId<WeatherPrototype>? weather = null;

                if (!string.IsNullOrEmpty(ev.Weather) && _proto.HasIndex<WeatherPrototype>(ev.Weather))
                    weather = ev.Weather;

                var seconds = Math.Clamp(ev.WeatherSeconds, 1f, 86400f);

                if (_weather.SetWeather(world.Value, weather, TimeSpan.FromSeconds(seconds)))
                    changes.Add($"weather={(weather?.Id ?? "clear")} for {seconds:F0}s");
            }

            if (ev.Gravity is { } gravity)
            {
                gravity = Math.Clamp(gravity, MinGravity, MaxGravity);

                foreach (var map in network.Layers.Concat(network.LowerLayers))
                {
                    if (!TryComp<WFPlanetLayerComponent>(map, out var layer))
                        continue;

                    layer.Gravity = gravity;
                    Dirty(map, layer);
                }

                // Lift is pooled and memoised against the old pull.
                _zLevels.WfInvalidateGravgenCapacity();
                changes.Add($"gravity={gravity:F2}g");
            }
        }

        if (changes.Count == 0)
            return string.Empty;

        var summary = string.Join(", ", changes);
        var actor = admin?.Name ?? "Console";
        _adminLogger.Add(LogType.AdminCommands, LogImpact.Medium, $"{actor} set planet {ToPrettyString(planet)}: {summary}");
        return summary;
    }
}
