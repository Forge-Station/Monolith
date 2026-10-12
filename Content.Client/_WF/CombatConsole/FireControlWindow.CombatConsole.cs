using System.Linq;
using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WF.CombatConsole;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    private readonly Button[] _wfRecall = new Button[WFWeaponGroups.Count];
    private readonly Button[] _wfSave = new Button[WFWeaponGroups.Count];
    private WFCombatConsoleState? _wfCombat;
    private Control _wfBatteryControls = default!;
    private Control _wfCountermeasures = default!;
    private Button _wfStore = default!;
    private Button _wfTypeSelect = default!;
    private Control _wfTypeChoices = default!;
    private Button _wfAutomatic = default!;
    private Button _wfDispense = default!;
    private Label _wfFlareStatus = default!;
    private Label _wfSelection = default!;
    private Label _wfThreats = default!;
    private readonly WFThreatAnnunciator _wfAnnunciator = new();

    /// <summary>Forwards validated console settings through the existing bound interface.</summary>
    public event Action<BoundUserInterfaceMessage>? CombatMessage;

    private void WfInitializeCombatConsole()
    {
        MinSize = new Vector2(960, 600);
        FitWindow(this, new Vector2(1180, 780));
        SelectAllButton.Text = Loc.GetString("wf-gunnery-all");
        UnselectAllButton.Text = Loc.GetString("wf-gunnery-clear");
        SelectBallisticButton.Text = Loc.GetString("wf-console-ballistic");
        SelectEnergyButton.Text = Loc.GetString("wf-console-energy");
        SelectMissileButton.Text = Loc.GetString("wf-console-missile");
        foreach (var button in new[] { SelectAllButton, UnselectAllButton, SelectBallisticButton, SelectEnergyButton, SelectMissileButton })
        {
            button.Margin = new Thickness(0);
            button.AddStyleClass("WfCompact");
        }

        _wfStore = Button("wf-gunnery-store", true);
        _wfStore.Name = "CockpitGroupStore";
        _wfStore.AddStyleClass("WfCompact");
        _wfStore.HorizontalExpand = false;
        _wfStore.SetWidth = 58;
        _wfStore.Disabled = true;
        _wfStore.ToolTip = Loc.GetString("wf-gunnery-store-help");
        _wfStore.OnToggled += _ => WfShowGroupMode();
        var groups = Row();
        groups.SeparationOverride = 3;
        for (var i = 0; i < WFWeaponGroups.Count; i++)
        {
            var slot = i;
            var recall = _wfRecall[i] = new Button { HorizontalExpand = true, Name = $"CockpitGroupRecall{i}" };
            recall.AddStyleClass("WfCompact");
            recall.Text = Loc.GetString("wf-gunnery-group", ("slot", i + 1), ("count", 0));
            var save = _wfSave[i] = new Button
            {
                HorizontalExpand = true,
                Name = $"CockpitGroupSave{i}",
                Text = Loc.GetString("wf-gunnery-save-slot", ("slot", i + 1)),
                Visible = false,
                ToolTip = Loc.GetString("wf-console-group-save-help"),
            };
            save.AddStyleClass("WfCompact");
            save.AddStyleClass("WfGroup");
            recall.Disabled = save.Disabled = true;
            recall.OnPressed += _ => WfRecallGroup(slot);
            save.OnPressed += _ =>
            {
                CombatMessage?.Invoke(new WFSaveWeaponGroupMessage(slot,
                    WeaponsList.Where(p => p.Value.Pressed && !(_wfCombat?.FlareLaunchers.Contains(p.Key) ?? false))
                        .Select(p => p.Key).ToList()));
                _wfStore.Pressed = false;
                WfShowGroupMode();
            };
            var holder = new Control { HorizontalExpand = true, SetHeight = 32 };
            holder.AddChild(recall);
            holder.AddChild(save);
            groups.AddChild(holder);
        }
        groups.AddChild(_wfStore);

        _wfTypeSelect = Button("wf-gunnery-type", true);
        _wfTypeSelect.Name = "WfWeaponTypeSelect";
        _wfTypeSelect.AddStyleClass("WfCompact");
        _wfTypeChoices = Row(SelectBallisticButton, SelectEnergyButton, SelectMissileButton);
        _wfTypeChoices.Name = "WfWeaponTypes";
        _wfTypeChoices.Visible = false;
        _wfTypeSelect.OnToggled += args => _wfTypeChoices.Visible = args.Pressed;
        foreach (var button in new[] { SelectBallisticButton, SelectEnergyButton, SelectMissileButton })
        {
            button.OnPressed += _ =>
            {
                _wfTypeSelect.Pressed = false;
                _wfTypeChoices.Visible = false;
            };
        }
        var selection = Row(SelectAllButton, UnselectAllButton, _wfTypeSelect);
        selection.SeparationOverride = 3;
        _wfSelection = Label("wf-console-selection", Muted);
        _wfSelection.ClipText = true;
        _wfSelection.HorizontalExpand = true;
        _wfThreats = Label("wf-gunnery-threat-offline", Muted);
        _wfThreats.Name = "WfMissileAlert";
        _wfThreats.ClipText = true;
        _wfThreats.HorizontalExpand = true;

        _wfAutomatic = Button("wf-console-auto-safe", true);
        _wfAutomatic.Name = "CockpitAutomaticFlares";
        _wfAutomatic.AddStyleClass("WfCompact");
        _wfAutomatic.Visible = false;
        _wfAutomatic.Disabled = true;
        _wfAutomatic.ToolTip = Loc.GetString("wf-console-auto-help");
        _wfAutomatic.OnToggled += args => CombatMessage?.Invoke(new WFAutomaticFlaresMessage(args.Pressed));
        _wfDispense = Button("wf-console-dispense");
        _wfDispense.Name = "CockpitDispenseFlares";
        _wfDispense.AddStyleClass("WfDispense");
        _wfDispense.AddStyleClass("WfCompact");
        _wfDispense.Visible = false;
        _wfDispense.Disabled = true;
        _wfDispense.OnPressed += _ => CombatMessage?.Invoke(new WFDispenseFlaresMessage());
        _wfFlareStatus = Label("wf-gunnery-flare-offline", Muted);
        _wfFlareStatus.Name = "CockpitFlareSupply";
        _wfFlareStatus.ClipText = true;
        _wfFlareStatus.HorizontalExpand = true;
        _wfFlareStatus.MouseFilter = MouseFilterMode.Pass;
        var countermeasures = Column(_wfFlareStatus, Row(_wfAutomatic, _wfDispense));
        countermeasures.SeparationOverride = 2;
        countermeasures.HorizontalExpand = true;
        countermeasures.Name = "WfCountermeasurePanel";
        countermeasures.Visible = false;
        _wfCountermeasures = countermeasures;

        var head = Column(groups, selection, _wfTypeChoices, _wfSelection);
        head.SeparationOverride = 5;
        var tail = Column(_wfThreats, countermeasures);
        tail.SeparationOverride = 5;
        var controls = new WFWeaponBank(head, ControllablesBox, tail);
        _wfBatteryControls = controls;
        var battery = Panel("wf-gunnery-battery", controls, true);
        battery.Name = "WfWeaponBattery";
        battery.SetWidth = 324;
        battery.HorizontalExpand = false;
        var scope = Scope("wf-console-fire-scope", NavRadar, new Vector2(240, 120));
        var instruments = Row(battery, scope, WfCreateShieldPanel()); // WOLFGATE(ShipShields): the gunner's shield control, on the right.
        instruments.VerticalExpand = true;
        RefreshButton.Name = "CockpitGunneryRefresh";
        RefreshButton.Text = Loc.GetString("wf-gunnery-link");
        RefreshButton.AddStyleClass("WfCompact");
        RefreshButton.Margin = new Thickness(0);
        RefreshButton.HorizontalExpand = false;
        RefreshButton.SetWidth = 72;
        ServerStatus.HorizontalExpand = true;
        ServerStatus.HorizontalAlignment = HAlignment.Stretch;
        ServerStatus.ClipText = true;
        var header = Row(Label("wf-console-fire-title", Accent), ServerStatus, RefreshButton);
        foreach (var button in new[] { IFFToggle, IFFDetailedToggle, DockToggle })
        {
            button.SetWidth = 72;
            button.AddStyleClass("WfCompact");
            button.Margin = new Thickness(0);
        }
        var footer = Row(IFFToggle, IFFDetailedToggle, DockToggle);
        footer.HorizontalExpand = false;
        footer.HorizontalAlignment = HAlignment.Right;
        var body = Column(header, instruments, footer);
        body.Margin = new Thickness(10);
        body.HorizontalExpand = body.VerticalExpand = true;
        ContentsContainer.DisposeAllChildren();
        ContentsContainer.AddChild(body);
        Install(this);
        OnWeaponSelectionChanged += WfUpdateSelection;
        WfUpdateSelection();
    }

    private void WfRecallGroup(int slot)
    {
        if (_wfCombat == null)
            return;
        var selected = _wfCombat.Groups[slot].ToHashSet();
        foreach (var (uid, button) in WeaponsList)
            button.Pressed = selected.Contains(uid);
        OnWeaponSelectionChanged?.Invoke();
        UpdateAllWeaponButtonTexts();
    }

    private void WfShowGroupMode()
    {
        for (var i = 0; i < WFWeaponGroups.Count; i++)
        {
            _wfRecall[i].Visible = !_wfStore.Pressed;
            _wfSave[i].Visible = _wfStore.Pressed;
        }
    }

    private void WfUpdateSelection()
    {
        foreach (var (uid, button) in WeaponsList)
        {
            if (_wfCombat?.FlareLaunchers.Contains(uid) == true)
                button.Pressed = false;
        }
        var selected = WeaponsList.Where(p => p.Value.Pressed).Select(p => p.Key).ToHashSet();
        _wfSelection.Text = Loc.GetString("wf-gunnery-selection", ("count", selected.Count));
        for (var i = 0; i < WFWeaponGroups.Count; i++)
            _wfRecall[i].Pressed = _wfCombat != null && _wfCombat.Groups[i].Count > 0 &&
                                   selected.SetEquals(_wfCombat.Groups[i]);
    }

    private void WfUpdateCombatConsole(FireControlConsoleBoundInterfaceState state)
    {
        if (_wfAnnunciator.Update(state.Combat.Threats, IsOpen && state.Connected))
            IoCManager.Resolve<IEntityManager>().System<WFConsoleAudio>().Warn();
        _wfCombat = state.Combat;
        var combat = state.Combat;
        _weaponTypes.Clear();
        foreach (var (uid, type) in combat.WeaponTypes)
            _weaponTypes[uid] = type;
        SelectBallisticButton.Disabled = !_weaponTypes.Values.Contains(ShipGunType.Ballistic);
        SelectEnergyButton.Disabled = !_weaponTypes.Values.Contains(ShipGunType.Energy);
        SelectMissileButton.Disabled = !_weaponTypes.Values.Contains(ShipGunType.Missile);
        foreach (var (uid, button) in WeaponsList)
        {
            WfStyleWeapon(button, uid);
            var available = !combat.FlareLaunchers.Contains(uid);
            ControllablesBox.SetWeapon(button, uid, available);
            if (!available)
                button.Pressed = false;
        }
        for (var i = 0; i < WFWeaponGroups.Count; i++)
        {
            _wfRecall[i].Text = Loc.GetString("wf-gunnery-group", ("slot", i + 1), ("count", combat.Groups[i].Count));
            _wfRecall[i].ToolTip = Loc.GetString("wf-gunnery-group-help", ("slot", i + 1), ("count", combat.Groups[i].Count));
            _wfRecall[i].Disabled = !state.Connected || combat.Groups[i].Count == 0;
            _wfSave[i].Disabled = !state.Connected;
        }
        _wfStore.Disabled = !state.Connected;
        if (!state.Connected)
        {
            _wfStore.Pressed = false;
            WfShowGroupMode();
        }
        _wfCountermeasures.Visible = _wfAutomatic.Visible = _wfDispense.Visible = combat.FlareLaunchers.Count > 0;
        _wfAutomatic.Pressed = combat.Automatic;
        _wfAutomatic.Disabled = !state.Connected || combat.FlareLaunchers.Count == 0;
        _wfAutomatic.Text = Loc.GetString(combat.Automatic ? "wf-console-auto-armed" : "wf-console-auto-safe");
        _wfDispense.Disabled = !state.Connected || combat.FlareLaunchers.Count == 0 ||
                              combat.Ammunition <= 0 && !combat.UnlimitedSupply || combat.Cooldown > 0;
        var supplied = combat.Ammunition > 0 || combat.UnlimitedSupply;
        var flareState = !state.Connected ? "wf-gunnery-flare-offline" :
            !supplied ? "wf-gunnery-flare-empty" : combat.Cooldown > 0 ? "wf-gunnery-flare-rearming" : "wf-gunnery-flare-ready";
        _wfFlareStatus.Text = Loc.GetString(flareState,
            ("ammo", combat.UnlimitedSupply ? Loc.GetString("wf-gunnery-flare-unlimited") : combat.Ammunition.ToString()),
            ("seconds", (int) Math.Ceiling(combat.Cooldown)));
        _wfFlareStatus.FontColorOverride = !state.Connected ? Muted : !supplied ? Red : combat.Cooldown > 0 ? Accent : Cream;
        _wfFlareStatus.ToolTip = combat.FlareLaunchers.Count == 0 ? Loc.GetString("wf-console-no-launchers") :
            Loc.GetString(combat.UnlimitedSupply ? "wf-console-flare-autoloader-status" : "wf-console-flare-status",
                ("count", combat.FlareLaunchers.Count), ("ammo", combat.Ammunition), ("seconds", (int) Math.Ceiling(combat.Cooldown)));
        _wfThreats.Text = Loc.GetString(!state.Connected ? "wf-gunnery-threat-offline" :
            combat.Threats == 0 ? "wf-gunnery-threat-clear" : "wf-gunnery-threat-incoming", ("count", combat.Threats));
        _wfThreats.FontColorOverride = !state.Connected ? Muted : combat.Threats == 0 ? Muted : Red;
        ServerStatus.FontColorOverride = state.Connected ? Green : Red;
        WfUpdateSelection();
    }
}
