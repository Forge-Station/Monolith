using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Components;
using Content.Server._WF.Access;
using Content.Server.Carrying;
using Content.Server.Chat.Systems;
using Content.Server.Stack;
using Content.Shared.Chat;
using Content.Shared.Climbing.Components;
using Content.Shared._NF.Bank;
using Content.Shared._WF.Access;
using Content.Shared._WF.Traders;
using Content.Shared.Access.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle.Components;
using Content.Shared.Clothing;
using Content.Shared.Cargo.Components;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Ensnaring.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Item;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nyanotrasen.Item.PseudoItem;
using Content.Shared.Paper;
using Content.Shared.Placeable;
using Content.Shared.SSDIndicator;
using Content.Shared.Stacks;
using Content.Shared.Strip.Components;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Traders;

/// <summary>
/// Runs a trader's conversation: the dialogue menu, the barter zone, payment and receipts.
/// </summary>
public sealed class TraderSystem : EntitySystem
{
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IdCardOwnerSystem _idOwner = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedGodmodeSystem _godmode = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    /// <summary>
    /// Stack type traders accept as cash.
    /// </summary>
    public static readonly ProtoId<StackPrototype> CashStackType = "Credit";

    private float _housekeeping;

    public override void Initialize()
    {
        base.Initialize();

        // After the body exists (godmode walks body parts) and after the loadout has dressed the trader.
        SubscribeLocalEvent<TraderComponent, MapInitEvent>(OnMapInit,
            after: [typeof(SharedBodySystem), typeof(LoadoutSystem)]);
        SubscribeLocalEvent<TraderComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<TraderComponent, ComponentShutdown>(OnShutdown);
        // Humanoids answer InteractHand with a hug, which would swallow the click.
        SubscribeLocalEvent<TraderComponent, InteractHandEvent>(OnInteractHand,
            before: [typeof(InteractionPopupSystem)]);
        SubscribeLocalEvent<TraderComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<TraderComponent, GetVerbsEvent<ActivationVerb>>(OnGetVerbs);
        SubscribeLocalEvent<TraderComponent, BoundUIClosedEvent>(OnUiClosed);

        Subs.BuiEvents<TraderComponent>(TraderUiKey.Dialogue, subs =>
        {
            subs.Event<TraderDialogueSelectMessage>(OnDialogueSelect);
            subs.Event<TraderConfirmMessage>(OnConfirm);
        });
    }

    #region Setup

    private void OnMapInit(Entity<TraderComponent> ent, ref MapInitEvent args)
    {
        MakeInert(ent);
        _container.EnsureContainer<Container>(ent, TraderComponent.OfferContainerId);
        RefreshTable(ent);
    }

    /// <summary>
    /// Strips everything that would let a player move, drag, strip or starve the trader.
    /// </summary>
    private void MakeInert(Entity<TraderComponent> ent)
    {
        _godmode.EnableGodmode(ent);

        RemComp<PullableComponent>(ent);
        RemComp<StrippableComponent>(ent);
        RemComp<CuffableComponent>(ent);
        RemComp<BuckleComponent>(ent);
        RemComp<EnsnareableComponent>(ent);
        RemComp<HungerComponent>(ent);
        RemComp<ThirstComponent>(ent);
        RemComp<SSDIndicatorComponent>(ent);
        RemComp<CarriableComponent>(ent);
        RemComp<PseudoItemComponent>(ent);
        RemComp<InputMoverComponent>(ent);
        RemComp<MobMoverComponent>(ent);
        RemComp<InteractionPopupComponent>(ent);
        RemComp<ClimbingComponent>(ent); // no dragging them onto their own table

        if (TryComp<PhysicsComponent>(ent, out var physics))
            _physics.SetBodyType(ent, BodyType.Static, body: physics);
    }

    private void OnTerminating(Entity<TraderComponent> ent, ref EntityTerminatingEvent args)
    {
        Cleanup(ent);
    }

    private void OnShutdown(Entity<TraderComponent> ent, ref ComponentShutdown args)
    {
        Cleanup(ent);
    }

    /// <summary>
    /// Hands back anything the trader was holding and drops the zone marker.
    /// </summary>
    private void Cleanup(Entity<TraderComponent> ent)
    {
        ReturnOfferedItems(ent);
        ReturnHeldItems(ent);

        if (ent.Comp.ZoneMarkerEntity is { } marker)
        {
            QueueDel(marker);
            ent.Comp.ZoneMarkerEntity = null;
        }

        ent.Comp.Customer = null;
        ent.Comp.PendingOption = null;
        ent.Comp.ReplyAt = null;
        ent.Comp.ReplyLine = null;
        ent.Comp.ReplyAction = TraderAction.None;
        ent.Comp.ReplyArgument = null;
    }

    #endregion

    #region Table and barter zone

    /// <summary>
    /// Looks one tile ahead of the trader for a table, and keeps the zone marker in sync.
    /// </summary>
    public void RefreshTable(Entity<TraderComponent> ent)
    {
        if (ent.Comp.ZoneMarkerEntity is { } existing && TerminatingOrDeleted(existing))
            ent.Comp.ZoneMarkerEntity = null;

        EntityUid? table = null;

        var xform = Transform(ent);
        if (xform.GridUid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var grid))
        {
            var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates)
                       + xform.LocalRotation.GetCardinalDir().ToIntVec();

            foreach (var anchored in _map.GetAnchoredEntities(gridUid, grid, tile))
            {
                if (!HasComp<PlaceableSurfaceComponent>(anchored))
                    continue;

                table = anchored;
                break;
            }
        }

        // Nothing changed, and the marker is where it should be.
        if (table == ent.Comp.Table && (table == null) == (ent.Comp.ZoneMarkerEntity == null))
            return;

        if (ent.Comp.Table != table && ent.Comp.Customer != null)
            ReturnOfferedItems(ent);

        ent.Comp.Table = table;
        Dirty(ent);
        UpdateZoneMarker(ent);
    }

    private void UpdateZoneMarker(Entity<TraderComponent> ent)
    {
        if (ent.Comp.ZoneMarkerEntity is { } marker)
        {
            QueueDel(marker);
            ent.Comp.ZoneMarkerEntity = null;
        }

        if (!TryGetTableTile(ent, out var gridUid, out var grid, out var tile))
            return;

        var coords = new EntityCoordinates(gridUid, _map.TileCenterToVector(gridUid, grid, tile));
        ent.Comp.ZoneMarkerEntity = Spawn(ent.Comp.ZoneMarker, coords);
    }

    private bool TryGetTableTile(Entity<TraderComponent> ent,
        out EntityUid gridUid,
        out MapGridComponent grid,
        out Vector2i tile)
    {
        gridUid = default;
        grid = default!;
        tile = default;

        if (ent.Comp.Table is not { } table || TerminatingOrDeleted(table))
            return false;

        var xform = Transform(table);
        if (xform.GridUid is not { } uid || !TryComp(uid, out MapGridComponent? gridComp))
            return false;

        gridUid = uid;
        grid = gridComp;
        tile = _map.TileIndicesFor(uid, gridComp, xform.Coordinates);
        return true;
    }

    /// <summary>
    /// Items the customer has offered: on the table, or in the offer container when there is none.
    /// </summary>
    public List<EntityUid> GetZoneItems(Entity<TraderComponent> ent)
    {
        var results = new List<EntityUid>();

        if (ent.Comp.Table != null)
        {
            if (!TryGetTableTile(ent, out var gridUid, out _, out var tile))
                return results;

            foreach (var uid in _lookup.GetLocalEntitiesIntersecting(gridUid, tile, 0f,
                         LookupFlags.Dynamic | LookupFlags.Sundries))
            {
                if (!HasComp<ItemComponent>(uid) || Transform(uid).Anchored)
                    continue;

                results.Add(uid);
            }

            return results;
        }

        if (_container.TryGetContainer(ent, TraderComponent.OfferContainerId, out var container))
            results.AddRange(container.ContainedEntities);

        return results;
    }

    /// <summary>
    /// Where goods, change and receipts land.
    /// </summary>
    public EntityCoordinates GetOutputCoordinates(Entity<TraderComponent> ent)
    {
        if (TryGetTableTile(ent, out var gridUid, out var grid, out var tile))
        {
            var centre = _map.TileCenterToVector(gridUid, grid, tile);
            var offset = new Vector2(_random.NextFloat(-0.2f, 0.2f), _random.NextFloat(-0.2f, 0.2f));
            return new EntityCoordinates(gridUid, centre + offset);
        }

        return Transform(ent).Coordinates;
    }

    private void ReturnOfferedItems(Entity<TraderComponent> ent)
    {
        if (!_container.TryGetContainer(ent, TraderComponent.OfferContainerId, out var container))
            return;

        if (container.ContainedEntities.Count == 0)
            return;

        _container.EmptyContainer(container, force: true, destination: GetOutputCoordinates(ent));
    }

    #endregion

    #region Holding

    /// <summary>
    /// Takes an item out of the barter zone and into the trader's hold, to be given back when the
    /// conversation ends.
    /// </summary>
    public bool TryHoldItem(Entity<TraderComponent> ent, EntityUid item)
    {
        var container = _container.EnsureContainer<Container>(ent, TraderComponent.HoldContainerId);
        if (!_container.Insert(item, container))
            return false;

        NoteHeld(ent, item);
        return true;
    }

    /// <summary>
    /// Remembers an item the trader has taken but put somewhere of its own (a console's card slot,
    /// say), so it still comes back when the conversation ends.
    /// </summary>
    public void NoteHeld(Entity<TraderComponent> ent, EntityUid item)
    {
        if (!ent.Comp.Held.Contains(item))
            ent.Comp.Held.Add(item);
    }

    /// <summary>
    /// Finds whatever a shipyard console's card slot will take - a voucher first, since that is what
    /// a customer holding one came to redeem, otherwise the customer's own ID - and holds it. Says
    /// why it cannot on failure.
    /// </summary>
    public bool TryHoldConsoleId(Entity<TraderComponent> ent, EntityUid customer, out EntityUid item)
    {
        if (TryGetZoneVoucher(ent, out item))
            return TryHoldItem(ent, item);

        if (!TryGetZoneId(ent, customer, out item, out var wrongOwner))
        {
            SayAndShow(ent, wrongOwner
                ? Loc.GetString("trader-not-your-id")
                : Loc.GetString("trader-request-item", ("thing", Loc.GetString("trader-thing-id-voucher"))));
            return false;
        }

        return TryHoldItem(ent, item);
    }

    /// <summary>
    /// Puts everything the trader is holding back on the output spot, wherever it ended up.
    /// </summary>
    public void ReturnHeldItems(Entity<TraderComponent> ent)
    {
        if (ent.Comp.Held.Count == 0)
            return;

        var held = new List<EntityUid>(ent.Comp.Held);
        ent.Comp.Held.Clear();

        foreach (var item in held)
        {
            if (TerminatingOrDeleted(item))
                continue;

            // Force: card slots lock themselves while the trader is holding a card.
            if (_container.IsEntityInContainer(item))
                _container.TryRemoveFromContainer(item, force: true);

            _transform.SetCoordinates(item, GetOutputCoordinates(ent));
        }
    }

    #endregion

    #region Conversation

    /// <summary>
    /// Opens the dialogue menu for a customer, if the trader is free.
    /// </summary>
    public bool TryStartConversation(Entity<TraderComponent> ent, EntityUid customer)
    {
        if (!_proto.TryIndex(ent.Comp.Dialogue, out var dialogue))
            return false;

        if (ent.Comp.Customer is { } current && current != customer)
        {
            Say(ent, Loc.GetString("trader-busy"));
            return false;
        }

        RefreshTable(ent);

        if (ent.Comp.Customer != customer)
        {
            ent.Comp.Customer = customer;
            ent.Comp.Confirming = false;
            ent.Comp.CurrentLine = Loc.GetString(dialogue.Greeting);
            Say(ent, ent.Comp.CurrentLine);
        }

        ent.Comp.LastInput = _timing.CurTime;
        _ui.TryOpenUi(ent.Owner, TraderUiKey.Dialogue, customer);
        UpdateDialogueState(ent);
        return true;
    }

    /// <summary>
    /// Ends the conversation and gives the customer their things back.
    /// </summary>
    public void EndConversation(Entity<TraderComponent> ent, bool farewell = true)
    {
        var customer = ent.Comp.Customer;

        ent.Comp.Customer = null;
        ent.Comp.Confirming = false;
        ent.Comp.PendingOption = null;
        ent.Comp.ReplyAt = null;
        ent.Comp.ReplyLine = null;
        ent.Comp.ReplyAction = TraderAction.None;
        ent.Comp.ReplyArgument = null;

        ReturnOfferedItems(ent);
        ReturnHeldItems(ent);

        if (customer is { } uid && !TerminatingOrDeleted(uid))
        {
            _ui.CloseUi(ent.Owner, TraderUiKey.Shop, uid);
            _ui.CloseUi(ent.Owner, TraderUiKey.UsedShips, uid);
            _ui.CloseUi(ent.Owner, TraderUiKey.Dialogue, uid);

            var ev = new TraderConversationEndedEvent(ent.Owner, uid);
            RaiseLocalEvent(ent.Owner, ref ev);
        }

        if (farewell && !TerminatingOrDeleted(ent.Owner) && _proto.TryIndex(ent.Comp.Dialogue, out var dialogue))
            Say(ent, Loc.GetString(dialogue.Farewell));
    }

    /// <summary>
    /// Keeps the idle timer from running out while the customer is doing something.
    /// </summary>
    public void NoteInput(Entity<TraderComponent> ent)
    {
        ent.Comp.LastInput = _timing.CurTime;
    }

    /// <summary>
    /// Puts the conversation window away without ending the conversation, for a service that takes
    /// the screen over with one of its own.
    /// </summary>
    public void HideDialogue(Entity<TraderComponent> ent, EntityUid customer)
    {
        ent.Comp.HidingDialogue = true;
        _ui.CloseUi(ent.Owner, TraderUiKey.Dialogue, customer);
        ent.Comp.HidingDialogue = false;
    }

    /// <summary>
    /// Whether the customer has one of the trader's service windows open instead of the menu.
    /// </summary>
    public bool InServiceWindow(Entity<TraderComponent> ent, EntityUid customer)
    {
        if (!TryComp<UserInterfaceComponent>(ent, out var ui))
            return false;

        foreach (var (key, actors) in ui.Actors)
        {
            if (key is not TraderUiKey.Dialogue && actors.Contains(customer))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Replaces the option list with a yes/no question. The answer comes back as
    /// <see cref="TraderConfirmedEvent"/>.
    /// </summary>
    public void AskConfirmation(Entity<TraderComponent> ent, string line)
    {
        ent.Comp.Confirming = true;
        ent.Comp.CurrentLine = line;
        ent.Comp.LastInput = _timing.CurTime;
        Say(ent, line);
        UpdateDialogueState(ent);
    }

    /// <summary>
    /// Makes the trader speak, and shows the line in the open dialogue menu.
    /// </summary>
    public void Say(Entity<TraderComponent> ent, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        _chat.TrySendInGameICMessage(ent.Owner, message, InGameICChatType.Speak, false);
    }

    /// <summary>
    /// Shows a line in the menu. Only the greeting and farewell go to chat; the rest stays between
    /// the trader and the customer.
    /// </summary>
    public void SayAndShow(Entity<TraderComponent> ent, string message)
    {
        ent.Comp.CurrentLine = message;
        UpdateDialogueState(ent);
    }

    public void UpdateDialogueState(Entity<TraderComponent> ent)
    {
        if (ent.Comp.Customer == null)
            return;

        var options = new List<string>();
        if (!ent.Comp.Confirming && _proto.TryIndex(ent.Comp.Dialogue, out var dialogue))
        {
            foreach (var option in dialogue.Options)
                options.Add(Loc.GetString(option.Prompt));
        }

        _ui.SetUiState(ent.Owner, TraderUiKey.Dialogue,
            new TraderDialogueState(ent.Comp.CurrentLine, options, ent.Comp.Confirming));
    }

    #endregion

    #region Interaction

    private void OnInteractHand(Entity<TraderComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartConversation(ent, args.User);
    }

    private void OnGetVerbs(Entity<TraderComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("trader-verb-talk"),
            Act = () => TryStartConversation(ent, user),
        });
    }

    private void OnInteractUsing(Entity<TraderComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Table != null)
        {
            Say(ent, Loc.GetString("trader-put-on-table"));
            args.Handled = true;
            return;
        }

        if (ent.Comp.Customer is { } current && current != args.User)
        {
            Say(ent, Loc.GetString("trader-busy"));
            args.Handled = true;
            return;
        }

        if (ent.Comp.Customer == null && !TryStartConversation(ent, args.User))
        {
            args.Handled = true;
            return;
        }

        var container = _container.EnsureContainer<Container>(ent, TraderComponent.OfferContainerId);
        if (!_container.Insert(args.Used, container))
            return;

        NoteInput(ent);
        args.Handled = true;
    }

    private void OnUiClosed(Entity<TraderComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is not TraderUiKey.Dialogue || ent.Comp.HidingDialogue)
            return;

        if (ent.Comp.Customer != args.Actor)
            return;

        EndConversation(ent);
    }

    private void OnDialogueSelect(Entity<TraderComponent> ent, ref TraderDialogueSelectMessage args)
    {
        if (ent.Comp.Customer != args.Actor || ent.Comp.Confirming || ent.Comp.ReplyAt != null)
            return;

        if (!_proto.TryIndex(ent.Comp.Dialogue, out var dialogue))
            return;

        if (args.Index < 0 || args.Index >= dialogue.Options.Count)
            return;

        NoteInput(ent);

        var option = dialogue.Options[args.Index];
        if (!MeetsRequirement(ent, args.Actor, option.Requires, out var thing))
        {
            // Asked for rather than refused: the trader picks it up itself once it is put down.
            ent.Comp.PendingOption = args.Index;
            ent.Comp.PendingUntil = _timing.CurTime + ent.Comp.PendingTimeout;
            SayAndShow(ent, Loc.GetString("trader-request-item", ("thing", Loc.GetString(thing))));
            return;
        }

        ent.Comp.PendingOption = null;
        QueueReply(ent, option);
    }

    /// <summary>
    /// Has the trader answer an option after its reply delay, and run whatever service it names.
    /// </summary>
    private void QueueReply(Entity<TraderComponent> ent, TraderDialogueOption option)
    {
        ent.Comp.ReplyAt = _timing.CurTime + ent.Comp.ReplyDelay;
        ent.Comp.ReplyLine = Loc.GetString(option.Response);
        ent.Comp.ReplyAction = option.Action;
        ent.Comp.ReplyArgument = option.Argument;
    }

    /// <summary>
    /// Runs an option the customer already picked once whatever it asked for turns up in the zone.
    /// </summary>
    private void PollPendingOption(Entity<TraderComponent> ent, EntityUid customer, TimeSpan now)
    {
        if (ent.Comp.PendingOption is not { } index)
            return;

        if (now > ent.Comp.PendingUntil)
        {
            ent.Comp.PendingOption = null;
            return;
        }

        // Something else has the conversation: let it finish before jumping in.
        if (ent.Comp.Confirming || ent.Comp.ReplyAt != null)
            return;

        if (!_proto.TryIndex(ent.Comp.Dialogue, out var dialogue) || index >= dialogue.Options.Count)
        {
            ent.Comp.PendingOption = null;
            return;
        }

        var option = dialogue.Options[index];
        if (!MeetsRequirement(ent, customer, option.Requires, out _))
            return;

        ent.Comp.PendingOption = null;
        NoteInput(ent);
        QueueReply(ent, option);
    }

    private void OnConfirm(Entity<TraderComponent> ent, ref TraderConfirmMessage args)
    {
        if (ent.Comp.Customer != args.Actor || !ent.Comp.Confirming)
            return;

        NoteInput(ent);
        ent.Comp.Confirming = false;
        ent.Comp.PendingOption = null;

        var ev = new TraderConfirmedEvent(ent.Owner, args.Actor, args.Accepted);
        RaiseLocalEvent(ent.Owner, ref ev);

        UpdateDialogueState(ent);
    }

    #endregion

    #region Requirements

    /// <summary>
    /// Checks the barter zone for whatever an option demands, and names it if it is missing.
    /// </summary>
    public bool MeetsRequirement(Entity<TraderComponent> ent, EntityUid customer, TraderRequirement requirement, out string thing)
    {
        thing = string.Empty;

        if (requirement == TraderRequirement.None)
            return true;

        var items = GetZoneItems(ent);

        switch (requirement)
        {
            case TraderRequirement.Payment:
                thing = "trader-thing-payment";
                foreach (var item in items)
                {
                    if (HasComp<IdCardComponent>(item) || IsCash(item, out _))
                        return true;
                }

                return false;

            // Only the customer's own card counts, so a stranger's card on the table does not
            // satisfy the check and then get refused by the service a moment later.
            case TraderRequirement.Id:
                thing = "trader-thing-id-voucher";
                foreach (var item in items)
                {
                    if (HasComp<ShipyardVoucherComponent>(item))
                        return true;

                    if (HasComp<IdCardComponent>(item) && _idOwner.IsOwnedBy(item, customer))
                        return true;
                }

                return false;

            case TraderRequirement.DeedId:
                thing = "trader-thing-deed-id";
                foreach (var item in items)
                {
                    if (HasComp<IdCardComponent>(item)
                        && HasComp<Content.Shared._NF.Shipyard.Components.ShuttleDeedComponent>(item)
                        && _idOwner.IsOwnedBy(item, customer))
                    {
                        return true;
                    }
                }

                return false;
        }

        return true;
    }

    private bool IsCash(EntityUid uid, out StackComponent stack)
    {
        stack = default!;

        if (!HasComp<CashComponent>(uid))
            return false;

        if (!TryComp(uid, out StackComponent? stackComp) || stackComp.StackTypeId != CashStackType.Id)
            return false;

        stack = stackComp;
        return true;
    }

    /// <summary>
    /// Finds an ID card the trader can see - in the barter zone or already in its hands - and
    /// whether it belongs to the customer.
    /// </summary>
    public bool TryGetZoneId(Entity<TraderComponent> ent, EntityUid customer, out EntityUid idCard, out bool wrongOwner)
    {
        idCard = default;
        wrongOwner = false;

        var items = GetZoneItems(ent);
        items.AddRange(ent.Comp.Held);

        var found = false;
        foreach (var item in items)
        {
            // A held card can be spent and deleted by the console it was handed to.
            if (TerminatingOrDeleted(item) || !HasComp<IdCardComponent>(item))
                continue;

            if (_idOwner.IsOwnedBy(item, customer))
            {
                idCard = item;
                wrongOwner = false;
                return true;
            }

            found = true;
        }

        wrongOwner = found;
        return false;
    }

    /// <summary>
    /// Finds a shipyard voucher the trader can see. Vouchers carry no owner, so whoever put one down
    /// is the one redeeming it.
    /// </summary>
    public bool TryGetZoneVoucher(Entity<TraderComponent> ent, out EntityUid voucher)
    {
        voucher = default;

        var items = GetZoneItems(ent);
        items.AddRange(ent.Comp.Held);

        foreach (var item in items)
        {
            if (TerminatingOrDeleted(item) || !HasComp<ShipyardVoucherComponent>(item))
                continue;

            voucher = item;
            return true;
        }

        return false;
    }

    #endregion

    #region Payment

    /// <summary>
    /// Takes the price out of the barter zone, falling back to the customer's bank if their own ID is there.
    /// </summary>
    public bool TryTakePayment(Entity<TraderComponent> ent, EntityUid customer, int price)
    {
        return TryTakePayment(ent, customer, price, out _, out _);
    }

    /// <summary>
    /// As above, reporting what was tendered and what came back as change.
    /// </summary>
    public bool TryTakePayment(Entity<TraderComponent> ent, EntityUid customer, int price, out int tendered, out int change)
    {
        tendered = 0;
        change = 0;

        if (price <= 0)
            return true;

        var cash = new List<EntityUid>();
        var cashTotal = 0;
        foreach (var item in GetZoneItems(ent))
        {
            if (!IsCash(item, out var stack))
                continue;

            cash.Add(item);
            cashTotal += stack.Count;
        }

        if (cashTotal >= price)
        {
            foreach (var item in cash)
                QueueDel(item);

            tendered = cashTotal;
            change = cashTotal - price;
            GiveChange(ent, change);
            return true;
        }

        if (!TryGetZoneId(ent, customer, out _, out var wrongOwner))
        {
            if (wrongOwner)
                SayAndShow(ent, Loc.GetString("trader-not-your-id"));
            else
                SayAndShow(ent, Loc.GetString("trader-short",
                    ("amount", BankSystemExtensions.ToSpesoString(price - cashTotal))));

            return false;
        }

        var remainder = price - cashTotal;
        if (!_bank.TryBankWithdraw(customer, remainder))
        {
            _bank.TryGetBalance(customer, out var balance);
            SayAndShow(ent, Loc.GetString("trader-short",
                ("amount", BankSystemExtensions.ToSpesoString(Math.Max(1, remainder - balance)))));
            return false;
        }

        foreach (var item in cash)
            QueueDel(item);

        tendered = price;
        return true;
    }

    /// <summary>
    /// Cash sitting in the barter zone.
    /// </summary>
    public int GetZoneCash(Entity<TraderComponent> ent)
    {
        var total = 0;
        foreach (var item in GetZoneItems(ent))
        {
            if (IsCash(item, out var stack))
                total += stack.Count;
        }

        return total;
    }

    /// <summary>
    /// The customer's bank balance, or zero if they have no account.
    /// </summary>
    public int GetBalance(EntityUid customer)
    {
        _bank.TryGetBalance(customer, out var balance);
        return balance;
    }

    /// <summary>
    /// Drops credits on the output spot, split into a few uneven stacks.
    /// </summary>
    public void GiveChange(Entity<TraderComponent> ent, int amount)
    {
        if (amount <= 0)
            return;

        var coords = GetOutputCoordinates(ent);
        foreach (var part in SplitChange(amount, _random))
            _stack.Spawn(part, CashStackType, coords);
    }

    /// <summary>
    /// Splits change into one to three positive stacks of uneven size that sum to <paramref name="amount"/>.
    /// </summary>
    public static List<int> SplitChange(int amount, IRobustRandom random)
    {
        var result = new List<int>();
        if (amount <= 0)
            return result;

        if (amount < 3)
        {
            result.Add(amount);
            return result;
        }

        var parts = random.Next(2, 4);
        var remaining = amount;

        for (var i = 0; i < parts - 1; i++)
        {
            // Leave at least one credit for every stack still to come.
            var max = remaining - (parts - i - 1);
            if (max < 1)
                break;

            var take = random.Next(1, max + 1);
            result.Add(take);
            remaining -= take;
        }

        result.Add(remaining);
        return result;
    }

    #endregion

    #region Receipts

    /// <summary>
    /// Drops a stamped paper receipt at the output spot.
    /// </summary>
    public EntityUid PrintReceipt(Entity<TraderComponent> ent, string name, string content)
    {
        var paper = Spawn(ent.Comp.ReceiptPrototype, GetOutputCoordinates(ent));
        _metaData.SetEntityName(paper, name);

        if (!TryComp<PaperComponent>(paper, out var paperComp))
            return paper;

        _paper.SetContent((paper, paperComp), content);
        _paper.TryStamp((paper, paperComp), new StampDisplayInfo
        {
            StampedName = Name(ent),
            StampedColor = ent.Comp.StampColor,
        }, ent.Comp.StampState);

        return paper;
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        _housekeeping += frameTime;
        var refresh = _housekeeping >= 1f;
        if (refresh)
            _housekeeping = 0f;

        var query = EntityQueryEnumerator<TraderComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var ent = (uid, comp);

            if (refresh)
                RefreshTable(ent);

            if (comp.ReplyAt is { } at && now >= at)
            {
                var line = comp.ReplyLine ?? string.Empty;
                var action = comp.ReplyAction;
                var argument = comp.ReplyArgument;

                comp.ReplyAt = null;
                comp.ReplyLine = null;
                comp.ReplyAction = TraderAction.None;
                comp.ReplyArgument = null;

                SayAndShow(ent, line);

                if (action != TraderAction.None && comp.Customer is { } actor)
                    RunAction(ent, actor, action, argument);
            }

            if (comp.Customer is not { } customer)
                continue;

            if (TerminatingOrDeleted(customer) || _mobState.IsIncapacitated(customer))
            {
                EndConversation(ent, false);
                continue;
            }

            // Browsing a catalogue is not idling.
            if (now - comp.LastInput > comp.IdleTimeout && !InServiceWindow(ent, customer))
            {
                EndConversation(ent);
                continue;
            }

            if (!InCustomerRange(ent, customer))
            {
                EndConversation(ent);
                continue;
            }

            if (refresh)
                PollPendingOption(ent, customer, now);
        }
    }

    private bool InCustomerRange(Entity<TraderComponent> ent, EntityUid customer)
    {
        var traderXform = Transform(ent);
        var customerXform = Transform(customer);

        if (traderXform.MapID != customerXform.MapID)
            return false;

        var delta = _transform.GetWorldPosition(customerXform) - _transform.GetWorldPosition(traderXform);
        return delta.Length() <= ent.Comp.MaxCustomerDistance;
    }

    private void RunAction(Entity<TraderComponent> ent, EntityUid customer, TraderAction action, string? argument)
    {
        var ev = new TraderActionEvent(ent.Owner, customer, action, argument);
        RaiseLocalEvent(ent.Owner, ref ev);

        if (ev.Handled)
            return;

        SayAndShow(ent, Loc.GetString("trader-cannot-help"));
    }
}
