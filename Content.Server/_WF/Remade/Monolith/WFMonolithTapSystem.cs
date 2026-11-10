using Content.Server._WF.Remade.Gifts;
using Content.Server.Administration.Logs;
using Content.Shared._WF.Remade;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Monolith;

/// <summary>The Tap console: sets a Monolith's draw and feeds it corpses to bring its resonance down.</summary>
public sealed partial class WFMonolithTapSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private WFMatterSystem _matter = default!;
    [Dependency] private WFMonolithSystem _monolith = default!;

    private static readonly TimeSpan RefreshRate = TimeSpan.FromSeconds(1);

    private readonly HashSet<Entity<WFMonolithComponent>> _inReach = new();
    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFMonolithTapComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<WFMonolithTapComponent, WFMonolithTapSetDrawMessage>(OnSetDraw);
        SubscribeLocalEvent<WFMonolithTapComponent, DragDropTargetEvent>(OnDragDrop);
        SubscribeLocalEvent<WFMonolithTapComponent, WFMonolithFeedDoAfterEvent>(OnFeed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextRefresh)
            return;

        _nextRefresh = now + RefreshRate;
        var query = EntityQueryEnumerator<WFMonolithTapComponent>();
        while (query.MoveNext(out var uid, out var tap))
        {
            if (_ui.IsUiOpen(uid, WFMonolithTapUiKey.Key))
                SendState((uid, tap));
        }
    }

    private void OnOpened(Entity<WFMonolithTapComponent> ent, ref BoundUIOpenedEvent args)
    {
        SendState(ent);
    }

    private void OnSetDraw(Entity<WFMonolithTapComponent> ent, ref WFMonolithTapSetDrawMessage args)
    {
        if (FindMonolith(ent) is not { } monolith)
            return;

        _monolith.SetDial(monolith, args.Gift, args.Draw);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):user} set the {args.Gift} dial of {ToPrettyString(monolith):monolith} to {_monolith.GetDial(monolith, args.Gift):P0}");
        SendState(ent);
    }

    private void OnDragDrop(Entity<WFMonolithTapComponent> ent, ref DragDropTargetEvent args)
    {
        if (!HasComp<HumanoidAppearanceComponent>(args.Dragged) || !_mobState.IsDead(args.Dragged))
            return;

        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.FeedTime, new WFMonolithFeedDoAfterEvent(), ent, args.Dragged, ent)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = false,
        });
    }

    private void OnFeed(Entity<WFMonolithTapComponent> ent, ref WFMonolithFeedDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } corpse || !_mobState.IsDead(corpse))
            return;

        args.Handled = true;
        if (FindMonolith(ent) is not { } monolith)
        {
            _popup.PopupEntity(Loc.GetString("wf-monolith-tap-no-scion"), ent, args.User);
            return;
        }

        if (!_monolith.TryQuiet(monolith))
        {
            _popup.PopupEntity(Loc.GetString("wf-monolith-tap-refused"), ent, args.User);
            return;
        }

        foreach (var item in _inventory.GetHandOrInventoryEntities(corpse))
        {
            _transform.DropNextTo(item, ent.Owner);
        }

        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(args.User):user} fed {ToPrettyString(corpse):corpse} to {ToPrettyString(monolith):monolith}");
        _popup.PopupEntity(Loc.GetString("wf-monolith-tap-fed", ("corpse", corpse)), ent, PopupType.LargeCaution);
        QueueDel(corpse);
        SendState(ent);
    }

    /// <summary>The nearest Monolith in the Tap's reach.</summary>
    private Entity<WFMonolithComponent>? FindMonolith(Entity<WFMonolithTapComponent> ent)
    {
        _inReach.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.Range, _inReach);

        Entity<WFMonolithComponent>? nearest = null;
        var best = float.MaxValue;
        var origin = _transform.GetWorldPosition(ent);
        foreach (var monolith in _inReach)
        {
            var distance = (_transform.GetWorldPosition(monolith) - origin).LengthSquared();
            if (distance < best)
            {
                best = distance;
                nearest = monolith;
            }
        }

        return nearest;
    }

    private void SendState(Entity<WFMonolithTapComponent> ent)
    {
        WFMonolithTapState state;
        if (FindMonolith(ent) is { } monolith)
        {
            var dials = new float[Enum.GetValues<WFGift>().Length];
            foreach (var gift in Enum.GetValues<WFGift>())
            {
                dials[(int) gift] = _monolith.GetDial(monolith, gift);
            }

            var rate = WFMonolithMath.Rate(_monolith.EffectiveDraw(monolith), monolith.Comp.Resonance, monolith.Comp.Corpses);
            state = new WFMonolithTapState(
                monolith.Comp.Resonance,
                monolith.Comp.Stage,
                dials,
                WFMonolithMath.Output(dials[(int) WFGift.Power], monolith.Comp.Resonance),
                rate * 60f,
                _monolith.IsBarred(monolith),
                _matter.FindSilo(monolith) != null,
                monolith.Comp.Shards.Count);
        }
        else
        {
            state = new WFMonolithTapState(null, WFMonolithStage.Quiet, new float[Enum.GetValues<WFGift>().Length], 0f, 0f, false, false, 0);
        }

        _ui.SetUiState(ent.Owner, WFMonolithTapUiKey.Key, state);
    }
}
