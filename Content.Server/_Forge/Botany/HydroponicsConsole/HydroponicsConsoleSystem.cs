using System.Linq;
using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Labels;
using Content.Server.Popups;
using Content.Shared._Forge.Botany;
using Content.Shared._Forge.Botany.HydroponicsConsole;
using Content.Shared.Atmos;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Systems;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Botany.HydroponicsConsole;

public sealed class HydroponicsConsoleSystem : EntitySystem
{
    [Dependency] private readonly DeviceListSystem _deviceList = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly BotanySystem _botany = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly PlantHolderSystem _plantHolder = default!;
    [Dependency] private readonly LabelSystem _label = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HydroponicsConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<HydroponicsConsoleComponent, DeviceListUpdateEvent>(OnDeviceListUpdated);
        SubscribeLocalEvent<HydroponicsConsoleComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<BotanyCultivarDiskComponent, ExaminedEvent>(OnDiskExamined);

        Subs.BuiEvents<HydroponicsConsoleComponent>(HydroponicsConsoleUiKey.Key, subs =>
        {
            subs.Event<HydroponicsConsoleSaveCultivarMessage>(OnSaveCultivar);
            subs.Event<HydroponicsConsoleRenameCultivarMessage>(OnRenameCultivar);
            subs.Event<HydroponicsConsoleDeleteCultivarMessage>(OnDeleteCultivar);
            subs.Event<HydroponicsConsolePrintPacketMessage>(OnPrintPacket);
            subs.Event<HydroponicsConsoleEjectDiskMessage>(OnEjectDisk);
            subs.Event<HydroponicsConsoleCycleLightMessage>(OnCycleLight);
            subs.Event<HydroponicsConsoleRenameTrayMessage>(OnRenameTray);
        });
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
            UpdateUi(uid, console);
        }
    }

    private void OnUiOpened(Entity<HydroponicsConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent.Owner, ent.Comp);
    }

    private void OnDeviceListUpdated(Entity<HydroponicsConsoleComponent> ent, ref DeviceListUpdateEvent args)
    {
        UpdateUi(ent.Owner, ent.Comp);
    }

    private void OnInteractUsing(Entity<HydroponicsConsoleComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<BotanyCultivarDiskComponent>(args.Used, out var disk))
            return;

        args.Handled = true;

        if (disk.Seed == null)
        {
            _popup.PopupEntity(Loc.GetString("hydroponics-console-disk-blank"), ent, args.User);
            return;
        }

        if (ent.Comp.Journal.Count >= HydroponicsConsoleComponent.MaxJournalEntries)
        {
            _popup.PopupEntity(Loc.GetString("hydroponics-console-journal-full"), ent, args.User);
            return;
        }

        ent.Comp.Journal.Add(new BotanyCultivarStored
        {
            LineName = string.IsNullOrWhiteSpace(disk.LineName)
                ? Loc.GetString(disk.Seed.DisplayName)
                : disk.LineName.Trim(),
            Seed = disk.Seed.Clone()
        });

        _popup.PopupEntity(Loc.GetString("hydroponics-console-disk-imported", ("name", disk.LineName)), ent, args.User);
        UpdateUi(ent.Owner, ent.Comp);
    }

    private void OnDiskExamined(Entity<BotanyCultivarDiskComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Seed == null)
        {
            args.PushMarkup(Loc.GetString("botany-cultivar-disk-blank"));
            return;
        }

        args.PushMarkup(Loc.GetString("botany-cultivar-disk-contents",
            ("name", ent.Comp.LineName),
            ("plant", Loc.GetString(ent.Comp.Seed.DisplayName))));
    }

    private void OnSaveCultivar(Entity<HydroponicsConsoleComponent> ent, ref HydroponicsConsoleSaveCultivarMessage args)
    {
        if (!TryGetLinkedTray(ent.Owner, args.Tray, out var tray, out var holder) || holder.Seed == null)
        {
            _popup.PopupEntity(Loc.GetString("hydroponics-console-save-empty"), ent, args.Actor);
            return;
        }

        if (ent.Comp.Journal.Count >= HydroponicsConsoleComponent.MaxJournalEntries)
        {
            _popup.PopupEntity(Loc.GetString("hydroponics-console-journal-full"), ent, args.Actor);
            return;
        }

        var lineName = SanitizeLineName(args.LineName, Loc.GetString(holder.Seed.DisplayName));
        ent.Comp.Journal.Add(new BotanyCultivarStored
        {
            LineName = lineName,
            Seed = holder.Seed.Clone()
        });

        _popup.PopupEntity(Loc.GetString("hydroponics-console-save-ok", ("name", lineName)), ent, args.Actor);
        UpdateUi(ent.Owner, ent.Comp);
    }

    private void OnRenameCultivar(Entity<HydroponicsConsoleComponent> ent, ref HydroponicsConsoleRenameCultivarMessage args)
    {
        if (!TryGetEntry(ent.Comp, args.Index, out var stored) || stored.Seed == null)
            return;

        stored.LineName = SanitizeLineName(args.LineName, Loc.GetString(stored.Seed.DisplayName));
        UpdateUi(ent.Owner, ent.Comp);
    }

    private void OnDeleteCultivar(Entity<HydroponicsConsoleComponent> ent, ref HydroponicsConsoleDeleteCultivarMessage args)
    {
        if (args.Index < 0 || args.Index >= ent.Comp.Journal.Count)
            return;

        ent.Comp.Journal.RemoveAt(args.Index);
        UpdateUi(ent.Owner, ent.Comp);
    }

    private void OnPrintPacket(Entity<HydroponicsConsoleComponent> ent, ref HydroponicsConsolePrintPacketMessage args)
    {
        if (!TryGetEntry(ent.Comp, args.Index, out var stored) || stored.Seed == null)
            return;

        var packet = _botany.SpawnSeedPacket(stored.Seed.Clone(), Transform(args.Actor).Coordinates, args.Actor);
        _label.Label(packet, stored.LineName);
        _popup.PopupEntity(Loc.GetString("hydroponics-console-print-packet-ok", ("name", stored.LineName)), ent, args.Actor);
    }

    private void OnEjectDisk(Entity<HydroponicsConsoleComponent> ent, ref HydroponicsConsoleEjectDiskMessage args)
    {
        if (!TryGetEntry(ent.Comp, args.Index, out var stored) || stored.Seed == null)
            return;

        var disk = Spawn("BotanyCultivarDisk", Transform(args.Actor).Coordinates);
        var diskComp = EnsureComp<BotanyCultivarDiskComponent>(disk);
        diskComp.LineName = stored.LineName;
        diskComp.Seed = stored.Seed.Clone();
        _hands.TryPickupAnyHand(args.Actor, disk);
        _popup.PopupEntity(Loc.GetString("hydroponics-console-eject-disk-ok", ("name", stored.LineName)), ent, args.Actor);
    }

    private void OnCycleLight(Entity<HydroponicsConsoleComponent> ent, ref HydroponicsConsoleCycleLightMessage args)
    {
        if (!TryGetLinkedTray(ent.Owner, args.Tray, out var tray, out var holder))
            return;

        holder.LightMode = holder.LightMode switch
        {
            HydroponicsLightMode.Ambient => HydroponicsLightMode.Day,
            HydroponicsLightMode.Day => HydroponicsLightMode.Shade,
            _ => HydroponicsLightMode.Ambient
        };

        holder.UpdateSpriteAfterUpdate = true;
        _plantHolder.UpdateSprite(tray, holder);
        _popup.PopupEntity(Loc.GetString("plant-holder-light-mode-set",
            ("mode", Loc.GetString($"plant-holder-light-{holder.LightMode.ToString().ToLowerInvariant()}"))),
            tray, args.Actor);
        UpdateUi(ent.Owner, ent.Comp);
    }

    private void OnRenameTray(Entity<HydroponicsConsoleComponent> ent, ref HydroponicsConsoleRenameTrayMessage args)
    {
        if (!TryGetLinkedTray(ent.Owner, args.Tray, out var tray, out _))
            return;

        var fallback = MetaData(tray).EntityName;
        var name = SanitizeLineName(args.TrayName, fallback);
        _meta.SetEntityName(tray, name);
        _popup.PopupEntity(Loc.GetString("hydroponics-console-rename-tray-ok", ("name", name)), ent, args.Actor);
        UpdateUi(ent.Owner, ent.Comp);
    }

    private void UpdateUi(EntityUid uid, HydroponicsConsoleComponent? console = null)
    {
        if (!Resolve(uid, ref console) || !_ui.HasUi(uid, HydroponicsConsoleUiKey.Key))
            return;

        var state = new HydroponicsConsoleBoundUserInterfaceState();
        foreach (var device in _deviceList.GetAllDevices(uid))
        {
            if (!TryComp<PlantHolderComponent>(device, out var holder))
                continue;

            state.Trays.Add(BuildEntry(device, holder));
        }

        state.Trays.Sort((a, b) => string.Compare(a.TrayName, b.TrayName, StringComparison.Ordinal));

        for (var i = 0; i < console.Journal.Count; i++)
        {
            var stored = console.Journal[i];
            if (stored.Seed == null)
                continue;

            state.Journal.Add(BuildRecord(i, stored));
        }

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
            LightMode = holder.LightMode,
        };

        if (holder.Seed != null)
        {
            entry.PlantName = Loc.GetString(holder.Seed.DisplayName);
            entry.Radioactive = holder.Seed.Radioactive;
            entry.CarnivorousGrab = holder.Seed.CarnivorousGrab;
            entry.CarnivorousPestEater = holder.Seed.CarnivorousPestEater;
            entry.GeneLocked = holder.Seed.GeneLocked;
            entry.Chemicals = holder.Seed.Chemicals.Keys.ToArray();
            entry.Mutations = holder.Seed.Mutations.Select(m => m.Name).ToArray();
            entry.ConsumeGases = FormatGases(holder.Seed.ConsumeGasses);
            entry.ExudeGases = FormatGases(holder.Seed.ExudeGasses);
            entry.IdealHeat = holder.Seed.IdealHeat;
            entry.HeatTolerance = holder.Seed.HeatTolerance;
            entry.LowPressure = holder.Seed.LowPressureTolerance;
            entry.HighPressure = holder.Seed.HighPressureTolerance;
            entry.IdealLight = holder.Seed.IdealLight;
            entry.LightTolerance = holder.Seed.LightTolerance;
        }

        return entry;
    }

    private static HydroponicsCultivarRecord BuildRecord(int index, BotanyCultivarStored stored)
    {
        var seed = stored.Seed!;
        return new HydroponicsCultivarRecord
        {
            Index = index,
            LineName = stored.LineName,
            SpeciesName = Robust.Shared.Localization.Loc.GetString(seed.DisplayName),
            Potency = seed.Potency,
            Yield = seed.Yield,
            IdealHeat = seed.IdealHeat,
            IdealLight = seed.IdealLight,
            Radioactive = seed.Radioactive,
            CarnivorousGrab = seed.CarnivorousGrab,
            CarnivorousPestEater = seed.CarnivorousPestEater,
            GeneLocked = seed.GeneLocked,
            Ligneous = seed.Ligneous,
            Chemicals = seed.Chemicals.Keys.ToArray(),
            Mutations = seed.Mutations.Select(m => m.Name).ToArray(),
            ConsumeGases = FormatGases(seed.ConsumeGasses),
            ExudeGases = FormatGases(seed.ExudeGasses),
        };
    }

    private bool TryGetLinkedTray(EntityUid console, NetEntity netTray, out EntityUid tray, out PlantHolderComponent holder)
    {
        tray = EntityUid.Invalid;
        holder = default!;
        if (!TryGetEntity(netTray, out var resolved))
            return false;

        tray = resolved.Value;
        if (!TryComp(tray, out PlantHolderComponent? resolvedHolder) || resolvedHolder == null)
            return false;

        holder = resolvedHolder;
        return _deviceList.GetAllDevices(console).Contains(tray);
    }

    private static bool TryGetEntry(HydroponicsConsoleComponent console, int index, out BotanyCultivarStored stored)
    {
        stored = default!;
        if (index < 0 || index >= console.Journal.Count)
            return false;

        stored = console.Journal[index];
        return true;
    }

    private static string SanitizeLineName(string name, string fallback)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = fallback;
        return name.Length <= 32 ? name : name[..32];
    }

    private static string FormatGases(Dictionary<Gas, float> gases)
    {
        if (gases.Count == 0)
            return string.Empty;

        return string.Join(", ", gases.Select(g => $"{g.Key} ({g.Value:0.##})"));
    }
}
