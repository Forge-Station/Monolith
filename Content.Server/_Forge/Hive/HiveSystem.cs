using Content.Server.Administration.Logs;
using Content.Server.Store.Systems;
using Content.Shared._Forge.Hive;
using Content.Shared._Forge.Hive.Components;
using Content.Shared._Starlight.CollectiveMind;
using Content.Shared.Actions;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Forge.Hive;

public sealed class HiveSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly CollectiveMindUpdateSystem _collectiveMind = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly StoreSystem _store = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HiveMemberComponent, MapInitEvent>(OnMemberMapInit);
        SubscribeLocalEvent<HiveMemberComponent, ComponentShutdown>(OnMemberShutdown);
        SubscribeLocalEvent<HiveMemberComponent, HiveShopActionEvent>(OnShopAction);

        SubscribeLocalEvent<HiveInjectorComponent, UseInHandEvent>(OnInjectorUse);
        SubscribeLocalEvent<HiveInjectorComponent, HiveInjectDoAfterEvent>(OnInjectDoAfter);
    }

    #region Член Улья

    private void OnMemberMapInit(Entity<HiveMemberComponent> ent, ref MapInitEvent args)
    {
        SetupStore(ent);

        _actions.AddAction(ent, ref ent.Comp.ShopActionEntity, ent.Comp.ShopAction);
        _actions.AddAction(ent, ref ent.Comp.DevourActionEntity, ent.Comp.DevourAction);
        Dirty(ent);

        _collectiveMind.AddCollectiveMind(ent.Owner, ent.Comp.HivemindChannel, setDefault: true);

        RemComp<HiveLinkedComponent>(ent);
    }

    private void SetupStore(Entity<HiveMemberComponent> ent)
    {
        var store = EnsureComp<StoreComponent>(ent);
        store.Name = ent.Comp.StoreName;
        store.RefundAllowed = ent.Comp.RefundAllowed;
        store.CurrencyWhitelist.Add(ent.Comp.Currency);

        foreach (var category in ent.Comp.StoreCategories)
        {
            store.Categories.Add(category);
        }

        _ui.SetUi(ent.Owner, StoreUiKey.Key, new InterfaceData("StoreBoundUserInterface"));

        _store.RefreshAllListings(store);

        if (ent.Comp.StartingDna > 0)
            AddDna(ent.Owner, ent.Comp.StartingDna, store);
    }

    private void OnMemberShutdown(Entity<HiveMemberComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        _actions.RemoveAction(ent.Owner, ent.Comp.ShopActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.DevourActionEntity);
        _store.CloseUi(ent);
        _collectiveMind.RemoveCollectiveMind(ent.Owner, ent.Comp.HivemindChannel);
    }

    private void OnShopAction(Entity<HiveMemberComponent> ent, ref HiveShopActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<StoreComponent>(ent, out var store))
            return;

        _store.ToggleUi(ent, ent, store);
        args.Handled = true;
    }

    public bool AddDna(Entity<HiveMemberComponent?> ent, FixedPoint2 amount, StoreComponent? store = null)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !Resolve(ent, ref store, false))
            return false;

        var currency = new Dictionary<string, FixedPoint2> { { ent.Comp.Currency, amount } };
        if (!_store.TryAddCurrency(currency, ent, store))
            return false;

        _store.UpdateUserInterface(ent, ent, store);
        return true;
    }

    public FixedPoint2 GetDna(Entity<HiveMemberComponent?, StoreComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp1, false) || !Resolve(ent, ref ent.Comp2, false))
            return FixedPoint2.Zero;

        return ent.Comp2.Balance.GetValueOrDefault(ent.Comp1.Currency);
    }

    #endregion

    #region Инъектор

    private void OnInjectorUse(Entity<HiveInjectorComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var user = args.User;

        if (ent.Comp.Used)
        {
            _popup.PopupEntity(Loc.GetString("hive-injector-empty"), ent, user);
            return;
        }

        if (HasComp<HiveMemberComponent>(user))
        {
            _popup.PopupEntity(Loc.GetString("hive-injector-already-member"), ent, user);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, ent.Comp.InjectTime, new HiveInjectDoAfterEvent(), ent.Owner, target: user, used: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnHandChange = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnInjectDoAfter(Entity<HiveInjectorComponent> ent, ref HiveInjectDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Used)
            return;

        args.Handled = true;
        var user = args.User;

        if (HasComp<HiveMemberComponent>(user))
            return;

        ent.Comp.Used = true;
        Dirty(ent);

        _audio.PlayPvs(ent.Comp.InjectSound, user);
        _popup.PopupEntity(Loc.GetString("hive-injector-injected"), user, user, PopupType.Medium);

        AddComp<HiveMemberComponent>(user);

        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(user):user} became a Hive member using {ToPrettyString(ent):injector}");

        if (ent.Comp.DeleteOnUse)
            QueueDel(ent);
    }

    #endregion
}
