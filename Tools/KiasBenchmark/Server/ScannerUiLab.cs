using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Content.Server._Forge.KIAS;
using Content.Server.Damage.Systems;
using Content.Server.GameTicking;
using Content.Server.Power.EntitySystems;
using Content.Shared._Forge.KIAS;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Mind;
using Content.Shared.Power.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Benchmark;

public sealed class KiasScannerUiStartCommand : IConsoleCommand
{
    public string Command => "kias_lab_scanner_server";
    public string Description => "Prepare real Briar scanner UI acceptance.";
    public string Help => Command;
    public void Execute(IConsoleShell shell, string argStr, string[] args) => IoCManager.Resolve<IEntityManager>().System<KiasScannerUiLabSystem>().Start();
}

public sealed class KiasScannerUiLabSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    private EntityUid _grid, _actor;
    private bool _started, _opened;
    private int _index;
    private TimeSpan _next;
    private string _output = string.Empty;
    private EntityUid[] _targets = Array.Empty<EntityUid>();
    private static readonly Vector2[] Positions = { new(-10.5f, 2.5f), new(-4.5f, 2.5f), new(-.5f, 35.5f), new(3.5f, 35.5f) };
    public void Start()
    {
        _output = Environment.GetEnvironmentVariable("KIAS_LAB_SCANNER_OUTPUT") ?? throw new InvalidOperationException("Scanner output required.");
        var maps = EntityManager.System<SharedMapSystem>();
        maps.CreateMap(out var map, runMapInit: false);
        if (!EntityManager.System<MapLoaderSystem>().TryLoadGrid(map, new ResPath("/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"), out var loaded)) throw new InvalidOperationException("Briar did not load.");
        _grid = loaded!.Value.Owner;
        _targets = Positions.Select(position => Devices<KiasRoomScannerComponent>().Single(uid => Vector2.DistanceSquared(Transform(uid).LocalPosition, position) < .01f)).ToArray();
        Charge();
        maps.InitializeMap(map);
        _started = true;
        File.WriteAllText(Path.Combine(_output, "server-bootstrap.json"), "{\"status\":\"WAITING_FOR_REAL_CLIENT\"}");
    }
    private EntityUid[] Devices<T>() where T : Component
    {
        var result = new List<EntityUid>();
        var query = EntityManager.AllEntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform)) if (transform.GridUid == _grid) result.Add(uid);
        return result.ToArray();
    }
    private void Charge()
    {
        foreach (var uid in Devices<BatteryComponent>()) EntityManager.System<BatterySystem>().SetCharge(uid, Comp<BatteryComponent>(uid).MaxCharge);
    }
    public override void Update(float frameTime)
    {
        if (!_started || _timing.CurTime < _next) return;
        _next = _timing.CurTime + TimeSpan.FromSeconds(.5);
        try
        {
            Charge();
            if (!_actor.Valid)
            {
                var session = _players.Sessions.FirstOrDefault();
                if (session == null || session.Data.ContentDataUncast == null) return;
                _actor = Spawn("MobHuman", new EntityCoordinates(_grid, Positions[0] - Vector2.UnitY));
                EntityManager.System<GodmodeSystem>().EnableGodmode(_actor);
                EnsureComp<ShipOwnershipComponent>(_grid).OwnerUserId = session.UserId;
                var minds = EntityManager.System<SharedMindSystem>();
                minds.TransferTo(minds.CreateMind(session.UserId), _actor);
                if (session.Status == SessionStatus.Connected) _players.JoinGame(session);
                else EntityManager.System<GameTicker>().PlayerJoinGame(session, silent: true);
            }
            if (_index == _targets.Length) return;
            var target = _targets[_index];
            var ui = EntityManager.System<UserInterfaceSystem>();
            if (!_opened)
            {
                var kias = EntityManager.System<KiasSystem>();
                if (!kias.IsOnline(target) || kias.TopologyPending(_grid)) return;
                var transform = Transform(target);
                var position = transform.LocalPosition + transform.LocalRotation.RotateVec(-Vector2.UnitY);
                EntityManager.System<SharedTransformSystem>().SetCoordinates(_actor, new EntityCoordinates(_grid, position));
                if (!ui.TryOpenUi(target, KiasUiKey.Scanner, _actor)) throw new InvalidOperationException("Actual scanner BUI open failed.");
                _opened = true;
                var geometry = EntityManager.System<KiasDisplaySystem>().BuildCoverage(_grid, target);
                if (_index == 0 && (geometry.RoomStatus != "Ok" || geometry.TileCount != 9 || geometry.DoorCount != 4)) throw new InvalidOperationException("Briar (-11,2) geometry mismatch.");
                if (_index >= 2 && (geometry.RoomStatus != "ExteriorSector" || geometry.TileCount == 0)) throw new InvalidOperationException("Exterior scanner geometry mismatch.");
                File.WriteAllText(Path.Combine(_output, $"server-case-{_index}.json"), JsonSerializer.Serialize(new { index = _index, target = target.ToString(), position = new { X = transform.LocalPosition.X, Y = transform.LocalPosition.Y }, powered = true, geometry }, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
            }
            if (!File.Exists(Path.Combine(_output, $"client-case-{_index}.json"))) return;
            ui.CloseUi(target, KiasUiKey.Scanner, _actor);
            _opened = false;
            _index++;
            if (_index == _targets.Length) File.WriteAllText(Path.Combine(_output, "server-completed.json"), "{\"status\":\"PASS\",\"cases\":4,\"authorMapChanged\":false}");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(_output, "server-failed.json"), JsonSerializer.Serialize(new { error = error.ToString(), index = _index }));
            _started = false;
        }
    }
}
