// Forge-Change-full
using System.Linq;
using Content.Shared._EE.Contractors.Systems;
using Content.Shared._Forge.Contractors;
using Content.Shared._Forge.Contractors.Components;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Preferences;

namespace Content.Server._Forge.Contractors;

public sealed partial class PassportConsoleSystem : EntitySystem
{
    [Dependency] private SharedPassportSystem _passports = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly List<RegistryRecord> _records = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PassportIssuedEvent>(OnPassportIssued);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PassportConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<PassportConsoleComponent, PassportConsoleSelectMessage>(OnSelect);
        SubscribeLocalEvent<PassportConsoleComponent, PassportConsolePrintMessage>(OnPrint);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _records.Clear();
    }

    private void OnPassportIssued(PassportIssuedEvent ev)
    {
        var existing = _records.FindIndex(r => r.Pid == ev.Pid);
        var record = new RegistryRecord
        {
            Id = existing >= 0 ? _records[existing].Id : Guid.NewGuid(),
            Pid = ev.Pid,
            Name = ev.Profile.Name,
            JobId = ev.JobId ?? string.Empty,
            Profile = ev.Profile.Clone(),
        };

        if (existing >= 0)
            _records[existing] = record;
        else
            _records.Add(record);

        RefreshOpenConsoles();
    }

    private void OnUiOpened(Entity<PassportConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not PassportConsoleUiKey)
            return;

        if (ent.Comp.SelectedId == null && _records.Count > 0)
        {
            ent.Comp.SelectedId = _records
                .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .First()
                .Id;
        }

        UpdateUi(ent);
    }

    private void OnSelect(Entity<PassportConsoleComponent> ent, ref PassportConsoleSelectMessage args)
    {
        ent.Comp.SelectedId = args.Id;
        UpdateUi(ent);
    }

    private void OnPrint(Entity<PassportConsoleComponent> ent, ref PassportConsolePrintMessage args)
    {
        var id = args.Id;
        var actor = args.Actor;
        var record = _records.Find(r => r.Id == id);
        if (record == null)
            return;

        var coords = _transform.GetMapCoordinates(ent.Owner);
        var passport = _passports.TryCreatePassport(record.Profile, coords);
        if (passport == null)
        {
            _popup.PopupEntity(Loc.GetString("passport-console-print-failed"), ent.Owner, actor);
            return;
        }

        _hands.PickupOrDrop(actor, passport.Value, checkActionBlocker: false, dropNear: true);
        _popup.PopupEntity(Loc.GetString("passport-console-print-success", ("name", record.Name)), ent.Owner, actor);
    }

    private void RefreshOpenConsoles()
    {
        var query = EntityQueryEnumerator<PassportConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, PassportConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void UpdateUi(Entity<PassportConsoleComponent> ent)
    {
        var entries = _records
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new PassportConsoleListEntry
            {
                Id = r.Id,
                Pid = r.Pid,
                Name = r.Name,
                JobId = r.JobId,
                Nationality = r.Profile.Nationality,
            })
            .ToList();

        RegistryRecord? selected = null;
        if (ent.Comp.SelectedId is { } selectedId)
            selected = _records.Find(r => r.Id == selectedId);

        _ui.SetUiState(ent.Owner, PassportConsoleUiKey.Key,
            new PassportConsoleBoundUserInterfaceState(
                entries,
                selected?.Id,
                selected?.Profile,
                selected?.Pid ?? string.Empty));
    }

    private sealed class RegistryRecord
    {
        public Guid Id;
        public string Pid = string.Empty;
        public string Name = string.Empty;
        public string JobId = string.Empty;
        public HumanoidCharacterProfile Profile = default!;
    }
}
