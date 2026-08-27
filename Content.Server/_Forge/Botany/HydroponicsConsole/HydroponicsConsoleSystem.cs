using System.Linq;
using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Server.DeviceNetwork.Systems;
using Content.Shared._Forge.Botany.HydroponicsConsole;
using Content.Shared.Atmos;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Botany.HydroponicsConsole;

public sealed class HydroponicsConsoleSystem : EntitySystem
{
    [Dependency] private readonly DeviceListSystem _deviceList = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HydroponicsConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<HydroponicsConsoleComponent, DeviceListUpdateEvent>(OnDeviceListUpdated);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<HydroponicsConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (!_ui.IsUiOpen(uid, HydroponicsConsoleUiKey.Key))
                continue;

            if (_timing.CurTime < console.NextUpdate)
                continue;

            console.NextUpdate = _timing.CurTime + TimeSpan.FromSeconds(console.UpdateInterval);
            UpdateUi(uid);
        }
    }

    private void OnUiOpened(Entity<HydroponicsConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent.Owner);
    }

    private void OnDeviceListUpdated(Entity<HydroponicsConsoleComponent> ent, ref DeviceListUpdateEvent args)
    {
        UpdateUi(ent.Owner);
    }

    private void UpdateUi(EntityUid uid)
    {
        if (!_ui.HasUi(uid, HydroponicsConsoleUiKey.Key))
            return;

        var state = new HydroponicsConsoleBoundUserInterfaceState();
        foreach (var device in _deviceList.GetAllDevices(uid))
        {
            if (!TryComp<PlantHolderComponent>(device, out var holder))
                continue;

            var entry = BuildEntry(device, holder);
            state.Trays.Add(entry);
        }

        state.Trays.Sort((a, b) => string.Compare(a.TrayName, b.TrayName, StringComparison.Ordinal));
        _ui.SetUiState(uid, HydroponicsConsoleUiKey.Key, state);
    }

    private HydroponicsConsoleTrayEntry BuildEntry(EntityUid tray, PlantHolderComponent holder)
    {
        TryComp<DeviceNetworkComponent>(tray, out var net);
        TryComp<MetaDataComponent>(tray, out var meta);

        var entry = new HydroponicsConsoleTrayEntry
        {
            Entity = GetNetEntity(tray),
            Address = net?.Address ?? string.Empty,
            TrayName = meta?.EntityName ?? Name(tray),
            HasPlant = holder.Seed != null,
            Dead = holder.Dead,
            Harvest = holder.Harvest,
            Health = holder.Health,
            Endurance = holder.Seed?.Endurance ?? 0f,
            Water = holder.WaterLevel,
            Nutrition = holder.NutritionLevel,
            Toxins = holder.Toxins,
            Weeds = holder.WeedLevel,
            Pests = holder.PestLevel,
            Age = holder.Age,
            ImproperHeat = holder.ImproperHeat,
            ImproperPressure = holder.ImproperPressure,
            ImproperLight = holder.ImproperLight,
            MissingGas = holder.MissingGas > 0,
        };

        if (holder.Seed != null)
        {
            entry.PlantName = Loc.GetString(holder.Seed.DisplayName);
            entry.Radioactive = holder.Seed.Radioactive;
            entry.CarnivorousGrab = holder.Seed.CarnivorousGrab;
            entry.Chemicals = holder.Seed.Chemicals.Keys.ToArray();
            entry.Mutations = holder.Seed.Mutations.Select(m => m.Name).ToArray();
            entry.ConsumeGases = FormatGases(holder.Seed.ConsumeGasses);
            entry.ExudeGases = FormatGases(holder.Seed.ExudeGasses);
            entry.IdealHeat = holder.Seed.IdealHeat;
            entry.HeatTolerance = holder.Seed.HeatTolerance;
            entry.LowPressure = holder.Seed.LowPressureTolerance;
            entry.HighPressure = holder.Seed.HighPressureTolerance;
        }

        return entry;
    }

    private static string FormatGases(Dictionary<Gas, float> gases)
    {
        if (gases.Count == 0)
            return string.Empty;

        return string.Join(", ", gases.Select(g => $"{g.Key} ({g.Value:0.##})"));
    }
}
