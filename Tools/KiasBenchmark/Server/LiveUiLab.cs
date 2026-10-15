using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Damage.Systems;
using Content.Server.GameTicking;
using Content.Server.Power.EntitySystems;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Mind;
using Content.Shared.Power.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Benchmark;

public sealed class KiasLiveUiStartCommand : IConsoleCommand
{
    public string Command => "kias_lab_ui_server";
    public string Description => "Prepare the isolated live programmer fixture.";
    public string Help => Command;
    public void Execute(IConsoleShell shell, string argStr, string[] args) => IoCManager.Resolve<IEntityManager>().System<KiasLiveUiLabSystem>().Start();
}

public sealed class KiasLiveUiLabSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    private EntityUid _grid, _programmer, _actor, _card;
    private bool _started, _opened;
    private TimeSpan _next, _reinsert;
    private string _output = string.Empty;
    private readonly List<object> _writes = new();
    private readonly HashSet<string> _writtenNames = new();
    public void Start()
    {
        if (_started) throw new InvalidOperationException("Live fixture is already active.");
        _output = Environment.GetEnvironmentVariable("KIAS_LAB_LIVE_OUTPUT") ?? throw new InvalidOperationException("KIAS_LAB_LIVE_OUTPUT required.");
        Directory.CreateDirectory(_output);
        var maps = EntityManager.System<SharedMapSystem>();
        maps.CreateMap(out var map, runMapInit: false);
        if (!EntityManager.System<MapLoaderSystem>().TryLoadGrid(map, new ResPath("/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"), out var loaded)) throw new InvalidOperationException("Briar did not load.");
        _grid = loaded!.Value.Owner;
        foreach (var uid in Devices<KiasControllerCardComponent>()) Comp<KiasControllerCardComponent>(uid).Enabled = false;
        _programmer = Spawn("KiasControllerProgrammer", new EntityCoordinates(_grid, new Vector2(-2.5f, 5.5f)));
        _card = Spawn("KiasProgrammableController", new EntityCoordinates(_grid, new Vector2(-2.5f, 5.5f)));
        if (!EntityManager.System<ItemSlotsSystem>().TryInsert(_programmer, KiasControllerProgrammerComponent.SlotId, _card, null)) throw new InvalidOperationException("Card insertion failed.");
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
                _actor = Spawn("MobHuman", new EntityCoordinates(_grid, new Vector2(-2.5f, 4.5f)));
                EntityManager.System<GodmodeSystem>().EnableGodmode(_actor);
                EnsureComp<ShipOwnershipComponent>(_grid).OwnerUserId = session.UserId;
                var minds = EntityManager.System<SharedMindSystem>();
                minds.TransferTo(minds.CreateMind(session.UserId), _actor);
                if (session.Status == SessionStatus.Connected) _players.JoinGame(session);
                else EntityManager.System<GameTicker>().PlayerJoinGame(session, silent: true);
            }
            if (!_opened && EntityManager.System<KiasSystem>().IsOnline(_programmer))
            {
                if (!EntityManager.System<UserInterfaceSystem>().TryOpenUi(_programmer, KiasControllerUiKey.Programmer, _actor)) throw new InvalidOperationException("Live BUI open failed.");
                _opened = true;
                File.WriteAllText(Path.Combine(_output, "server-ready.json"), JsonSerializer.Serialize(new { status = "READY", grid = _grid.ToString(), programmer = _programmer.ToString(), actualSession = true, powered = true }));
            }
            if (!_opened) return;
            var programmer = Comp<KiasControllerProgrammerComponent>(_programmer);
            var stored = Comp<KiasControllerCardComponent>(_card);
            if (!programmer.DraftDirty && stored.Program.Name.StartsWith("Живой GUI ") && _writtenNames.Add(stored.Program.Name))
            {
                var program = stored.Program;
                var number = program.Nodes.First(node => node.Kind == KiasNodeKind.NumberConstant).Config.Number;
                var group = program.Nodes.First(node => node.Kind == KiasNodeKind.All && node.Profile == "Speaker");
                if (number != .25 || group.Room != "Тестовая комната" || group.Group != "Тестовая группа") throw new InvalidOperationException("The actual written card did not preserve inspector values.");
                var adapter = program.Nodes.Single(node => node.Kind == KiasNodeKind.Specific && node.Profile == "DeviceAdapter");
                if (adapter.Binding is not { } target || MetaData(target).EntityPrototype?.ID != "KiasDeviceAdapter"
                    || !program.Wires.Any(wire => wire.FromPort == "Manual" && wire.ToNode == adapter.Id && wire.ToPort == "On")
                    || !program.Nodes.Any(node => node.Kind == KiasNodeKind.EnumConstant && node.Config.EnumDomain == KiasEnumDomain.AudioChannel && node.Config.Enum == 2))
                    throw new InvalidOperationException("The written card did not preserve the actual binding, connection or inferred enum.");
                _writes.Add(new { name = program.Name, actualCard = _card.ToString(), number, group.Room, group.Group, nodes = program.Nodes.Count, wires = program.Wires.Count,
                    actualAdapterBinding = target.ToString(), manualConnection = true, inferredAudioChannel = 2 });
                File.WriteAllText(Path.Combine(_output, "server-writes.json"), JsonSerializer.Serialize(new { status = "RECORDED", actualNetworkBui = true, writes = _writes }, new JsonSerializerOptions { WriteIndented = true }));
            }
            if (programmer.Card == null)
            {
                if (_reinsert == TimeSpan.Zero) _reinsert = _timing.CurTime + TimeSpan.FromSeconds(2);
                if (_timing.CurTime >= _reinsert)
                {
                    if (!EntityManager.System<ItemSlotsSystem>().TryInsert(_programmer, KiasControllerProgrammerComponent.SlotId, _card, _actor)) throw new InvalidOperationException("Native reinsert failed.");
                    _reinsert = TimeSpan.Zero;
                }
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(_output, "server-failed.json"), JsonSerializer.Serialize(new { error = error.ToString() }));
            _started = false;
        }
    }
}
