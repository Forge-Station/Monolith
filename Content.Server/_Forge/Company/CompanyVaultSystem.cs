using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Popups;
using Content.Server._Mono.Company;
using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Content.Shared.GameTicking;
using Content.Shared.Paper;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Timing.Components;
using Content.Shared.Timing.Systems;
using Content.Shared.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Company;

/// <summary>
/// One safe per company. Items are written to the database, so they are still there next shift.
/// The grid size is stored with them; <see cref="TryResize"/> is how a later upgrade grows it.
/// </summary>
public sealed class CompanyVaultSystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private CompanyManager _companies = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PaperSystem _paper = default!;

    private readonly Dictionary<string, EntityUid> _vaults = new();
    private readonly Dictionary<string, (int Width, int Height)> _sizes = new();
    private readonly Dictionary<string, SemaphoreSlim> _gates = new();
    private bool _shuttingDown;

    public override void Initialize()
    {
        SubscribeLocalEvent<CompanyVaultComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<CompanyVaultComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<CompanyVaultComponent, BoundUIClosedEvent>(OnUiClosed);
        // Broadcast, not a StorageComponent subscription: that one already belongs to SharedStorageSystem.
        SubscribeLocalEvent<ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<ContainerIsRemovingAttemptEvent>(OnRemoveAttempt);
        SubscribeAllEvent<StorageInteractWithItemEvent>(OnReadItem);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        _ = LoadSizes();
    }

    public int GetWidth(string companyId)
    {
        return _sizes.TryGetValue(companyId, out var size) ? size.Width : CompanyOrgLimits.VaultGrid;
    }

    public int GetHeight(string companyId)
    {
        return _sizes.TryGetValue(companyId, out var size) ? size.Height : CompanyOrgLimits.VaultGrid;
    }

    public async Task Open(EntityUid actor, ICommonSession session, string companyId)
    {
        if (!IsMember(session, companyId))
        {
            _popup.PopupEntity(Loc.GetString("company-vault-denied"), actor, actor);
            return;
        }

        var gate = Gate(companyId);
        await gate.WaitAsync();
        try
        {
            if (!_vaults.TryGetValue(companyId, out var vault) || !Exists(vault))
            {
                vault = Spawn("CompanyVault", MapCoordinates.Nullspace);
                var comp = EnsureComp<CompanyVaultComponent>(vault);
                comp.CompanyId = companyId;
                comp.Restoring = true;
                var record = await _db.GetCompanyVault(companyId);
                comp.Width = record.Width;
                comp.Height = record.Height;
                ApplyGrid(vault, record.Width, record.Height);
                _sizes[companyId] = (record.Width, record.Height);
                Restore(vault, comp, record.Items);
                comp.Restoring = false;
                _vaults[companyId] = vault;
            }

            var vaultComp = Comp<CompanyVaultComponent>(vault);
            if (vaultComp.OpenedBy is { } opener && opener != actor && Exists(opener))
            {
                _popup.PopupEntity(Loc.GetString("company-vault-busy"), actor, actor);
                return;
            }

            vaultComp.Writable = _companies.HasPermission(session, companyId, CompanyPermission.Vault);
            _transform.SetCoordinates(vault, new EntityCoordinates(actor, Vector2.Zero));
            vaultComp.OpenedBy = actor;
            _storage.OpenStorageUI(vault, actor, silent: true);
            if (!vaultComp.Writable)
                _popup.PopupEntity(Loc.GetString("company-vault-readonly"), actor, actor);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open the company vault for {companyId}: {ex}");
            if (Exists(actor))
                _popup.PopupEntity(Loc.GetString("company-vault-failed"), actor, actor);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Changes the safe's grid. Items already inside stay put. A later company upgrade calls this.
    /// </summary>
    public async Task<bool> TryResize(string companyId, int width, int height)
    {
        width = Math.Clamp(width, 1, CompanyOrgLimits.VaultGridMax);
        height = Math.Clamp(height, 1, CompanyOrgLimits.VaultGridMax);

        var gate = Gate(companyId);
        await gate.WaitAsync();
        try
        {
            if (_vaults.TryGetValue(companyId, out var vault) &&
                Exists(vault) &&
                TryComp(vault, out CompanyVaultComponent? comp))
            {
                comp.Width = width;
                comp.Height = height;
                ApplyGrid(vault, width, height);
                var items = ReadItems(vault);
                items.AddRange(comp.Unloaded);
                _sizes[companyId] = (width, height);
                await _db.SetCompanyVault(companyId, width, height, items);
                return true;
            }

            var record = await _db.GetCompanyVault(companyId);
            _sizes[companyId] = (width, height);
            await _db.SetCompanyVault(companyId, width, height, record.Items);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to resize the company vault for {companyId}: {ex}");
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task LoadSizes()
    {
        try
        {
            foreach (var proto in _prototypes.EnumeratePrototypes<CompanyPrototype>())
            {
                if (proto.ID == "None")
                    continue;

                var record = await _db.GetCompanyVault(proto.ID);
                _sizes[proto.ID] = (record.Width, record.Height);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load company vault sizes: {ex}");
        }
    }

    private void OnInserted(EntityUid uid, CompanyVaultComponent comp, EntInsertedIntoContainerMessage args)
    {
        OnContentsChanged(uid, comp, args);
    }

    private void OnRemoved(EntityUid uid, CompanyVaultComponent comp, EntRemovedFromContainerMessage args)
    {
        OnContentsChanged(uid, comp, args);
    }

    private void OnContentsChanged(EntityUid uid, CompanyVaultComponent comp, ContainerModifiedMessage args)
    {
        if (comp.Restoring || _shuttingDown || args.Container.ID != StorageComponent.ContainerId)
            return;

        _ = Save(uid, comp);
    }

    private void OnUiClosed(EntityUid uid, CompanyVaultComponent comp, BoundUIClosedEvent args)
    {
        if (!StorageComponent.StorageUiKey.Key.Equals(args.UiKey) || args.Actor != comp.OpenedBy)
            return;

        comp.OpenedBy = null;
        comp.Writable = false;
        if (Exists(uid))
            _transform.SetMapCoordinates(uid, MapCoordinates.Nullspace);
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        _shuttingDown = true;
        foreach (var (company, vault) in _vaults)
        {
            if (!TryComp(vault, out CompanyVaultComponent? comp))
                continue;

            var items = ReadItems(vault);
            items.AddRange(comp.Unloaded);
            _ = _db.SetCompanyVault(company, comp.Width, comp.Height, items);
        }
    }

    private async Task Save(EntityUid uid, CompanyVaultComponent comp)
    {
        var gate = Gate(comp.CompanyId);
        await gate.WaitAsync();
        try
        {
            if (!Exists(uid) || comp.Restoring || _shuttingDown)
                return;

            var items = ReadItems(uid);
            items.AddRange(comp.Unloaded);
            _sizes[comp.CompanyId] = (comp.Width, comp.Height);
            await _db.SetCompanyVault(comp.CompanyId, comp.Width, comp.Height, items);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save the company vault for {comp.CompanyId}: {ex}");
        }
        finally
        {
            gate.Release();
        }
    }

    private void Restore(EntityUid vault, CompanyVaultComponent comp, List<string> items)
    {
        comp.Unloaded.Clear();
        if (!TryComp<StorageComponent>(vault, out var storage))
            return;

        foreach (var data in items)
        {
            try
            {
                using var reader = new StringReader(data);
                if (!_loader.TryLoadEntity(reader, "company vault", out var loaded))
                {
                    comp.Unloaded.Add(data);
                    continue;
                }

                var item = loaded.Value.Owner;
                ClearCooldowns(item);
                if (!_storage.Insert(vault, item, out _, storageComp: storage, playSound: false))
                {
                    QueueDel(item);
                    comp.Unloaded.Add(data);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load an item into the company vault: {ex}");
                comp.Unloaded.Add(data);
            }
        }
    }

    private List<string> ReadItems(EntityUid vault)
    {
        var list = new List<string>();
        if (!TryComp<StorageComponent>(vault, out var storage))
            return list;

        foreach (var item in storage.Container.ContainedEntities)
        {
            try
            {
                using var writer = new StringWriter();
                _loader.TrySaveEntity(item, writer);
                var text = writer.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    list.Add(text);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save {ToPrettyString(item)} from the company vault: {ex}");
            }
        }

        return list;
    }

    private void ApplyGrid(EntityUid vault, int width, int height)
    {
        if (!TryComp<StorageComponent>(vault, out var storage))
            return;

        storage.Grid.Clear();
        storage.Grid.Add(new Box2i(0, 0, Math.Max(0, width - 1), Math.Max(0, height - 1)));
        Dirty(vault, storage);
    }

    private void ClearCooldowns(EntityUid item)
    {
        if (TryComp<UseDelayComponent>(item, out var delay))
        {
            foreach (var id in delay.Delays.Keys)
                _useDelay.CancelDelay((item, delay), id);
        }

        var children = Transform(item).ChildEnumerator;
        while (children.MoveNext(out var child))
            ClearCooldowns(child);
    }

    private void OnInsertAttempt(ContainerIsInsertingAttemptEvent args)
    {
        if (args.Cancelled || !IsInsideReadOnlyVault(args.Container.Owner))
            return;

        args.Cancel();
    }

    private void OnRemoveAttempt(ContainerIsRemovingAttemptEvent args)
    {
        if (args.Cancelled || !IsInsideReadOnlyVault(args.Container.Owner))
            return;

        args.Cancel();
    }

    /// <summary>
    /// A click in read-only mode cannot take the item. Papers still open for reading.
    /// </summary>
    private void OnReadItem(StorageInteractWithItemEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } actor)
            return;

        if (!TryGetEntity(msg.StorageUid, out var storage) || !TryGetEntity(msg.InteractedItemUid, out var item))
            return;

        if (!IsInsideReadOnlyVault(storage.Value))
            return;

        _paper.TryOpenReadUi(item.Value, actor);
    }

    private bool IsInsideReadOnlyVault(EntityUid uid)
    {
        var current = uid;
        for (var guard = 0; guard < 8 && current.IsValid(); guard++)
        {
            if (TryComp<CompanyVaultComponent>(current, out var vault))
                return !vault.Writable && !vault.Restoring && !_shuttingDown;

            var parent = Transform(current).ParentUid;
            if (parent == current)
                break;

            current = parent;
        }

        return false;
    }

    private bool IsMember(ICommonSession session, string companyId)
    {
        if (!_companies.TryGetCharacterContext(session.UserId, out var slot, out _))
            return false;

        var member = _companies.GetMemberForCharacter(session.UserId, slot);
        return member != null && member.Value.Company == companyId;
    }

    private SemaphoreSlim Gate(string companyId)
    {
        if (!_gates.TryGetValue(companyId, out var gate))
        {
            gate = new SemaphoreSlim(1, 1);
            _gates[companyId] = gate;
        }

        return gate;
    }
}
