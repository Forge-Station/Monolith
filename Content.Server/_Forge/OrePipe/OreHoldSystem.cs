using Content.Server.Stack;
using Content.Shared._Forge.OrePipe;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.OrePipe;

/// <summary>
/// Abstract expandable ore bank. Modules on the same OrePipe graph add capacity.
/// </summary>
public sealed partial class OreHoldSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OreHoldComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<OreHoldModuleComponent, ExaminedEvent>(OnModuleExamined);
        SubscribeLocalEvent<OreHoldComponent, DestructionEventArgs>(OnDestruction);

        Subs.BuiEvents<OreHoldComponent>(OreHoldUiKey.Key, subscriber =>
        {
            subscriber.Event<BoundUIOpenedEvent>(OnUiOpened);
            subscriber.Event<OreHoldEjectMessage>(OnEjectMessage);
        });
    }

    private void OnDestruction(EntityUid uid, OreHoldComponent component, DestructionEventArgs args)
    {
        TryEjectAll(uid, component);
    }

    private void OnExamined(EntityUid uid, OreHoldComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!EntityManager.System<OrePipeSystem>().TryGetTrunkOnTile(uid, out _))
            args.PushMarkup(Loc.GetString("ore-pipe-examine-hold-no-trunk"));

        var cap = GetCapacity(uid, component);
        args.PushMarkup(Loc.GetString("ore-hold-examine-capacity",
            ("count", component.TotalCount),
            ("max", cap)));

        if (component.Contents.Count == 0)
        {
            args.PushMarkup(Loc.GetString("ore-hold-examine-empty"));
            return;
        }

        foreach (var (proto, count) in component.Contents)
        {
            var name = _proto.TryIndex(proto, out EntityPrototype? ent)
                ? ent.Name
                : proto.ToString();
            args.PushMarkup(Loc.GetString("ore-hold-examine-entry", ("name", name), ("count", count)));
        }
    }

    private void OnModuleExamined(EntityUid uid, OreHoldModuleComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("ore-hold-module-examine", ("extra", component.ExtraCapacity)));
    }

    private void OnUiOpened(Entity<OreHoldComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent.Owner, ent.Comp);
    }

    private void OnEjectMessage(Entity<OreHoldComponent> ent, ref OreHoldEjectMessage args)
    {
        if (args.Amount <= 0 || string.IsNullOrEmpty(args.PrototypeId))
            return;

        TryEject(ent.Owner, args.PrototypeId, args.Amount, ent.Comp);
        UpdateUi(ent.Owner, ent.Comp);
    }

    private void UpdateUi(EntityUid uid, OreHoldComponent component)
    {
        if (!_ui.IsUiOpen(uid, OreHoldUiKey.Key))
            return;

        var entries = new List<OreHoldEntry>();
        foreach (var (proto, count) in component.Contents)
        {
            if (count <= 0)
                continue;

            var name = _proto.TryIndex(proto, out EntityPrototype? ent)
                ? ent.Name
                : proto.ToString();
            entries.Add(new OreHoldEntry(proto.Id, name, count));
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        _ui.SetUiState(uid, OreHoldUiKey.Key,
            new OreHoldBoundUserInterfaceState(entries, component.TotalCount, GetCapacity(uid, component)));
    }

    public int GetCapacity(EntityUid holdUid, OreHoldComponent? hold = null)
    {
        if (!Resolve(holdUid, ref hold))
            return 0;

        var capacity = hold.BaseCapacity;
        capacity += EntityManager.System<OrePipeSystem>().GetLinkedModuleExtraCapacity(holdUid);
        return capacity;
    }

    public int GetFreeSpace(EntityUid holdUid, OreHoldComponent? hold = null)
    {
        if (!Resolve(holdUid, ref hold))
            return 0;

        return Math.Max(0, GetCapacity(holdUid, hold) - hold.TotalCount);
    }

    public bool TryDeposit(EntityUid holdUid, EntProtoId oreProto, int count, OreHoldComponent? hold = null)
    {
        if (count <= 0 || !Resolve(holdUid, ref hold) || !_proto.HasIndex(oreProto))
            return false;

        var free = GetFreeSpace(holdUid, hold);
        if (free <= 0)
            return false;

        var toAdd = Math.Min(count, free);
        hold.Contents.TryGetValue(oreProto, out var existing);
        hold.Contents[oreProto] = existing + toAdd;
        Dirty(holdUid, hold);
        UpdateUi(holdUid, hold);
        return toAdd > 0;
    }

    public bool TryEject(EntityUid holdUid, EntProtoId oreProto, int amount, OreHoldComponent? hold = null)
    {
        if (amount <= 0 || !Resolve(holdUid, ref hold))
            return false;

        if (!hold.Contents.TryGetValue(oreProto, out var available) || available <= 0)
            return false;

        var toEject = Math.Min(amount, available);
        SpawnOreStacks(oreProto, toEject, _transform.GetMoverCoordinates(holdUid));

        var remaining = available - toEject;
        if (remaining > 0)
            hold.Contents[oreProto] = remaining;
        else
            hold.Contents.Remove(oreProto);

        Dirty(holdUid, hold);
        return true;
    }

    public bool TryEjectAll(EntityUid holdUid, OreHoldComponent? hold = null)
    {
        if (!Resolve(holdUid, ref hold) || hold.Contents.Count == 0)
            return false;

        var keys = new List<EntProtoId>(hold.Contents.Keys);
        foreach (var protoId in keys)
        {
            if (!hold.Contents.TryGetValue(protoId, out var count) || count <= 0)
                continue;

            TryEject(holdUid, protoId, count, hold);
        }

        return true;
    }

    private void SpawnOreStacks(EntProtoId protoId, int count, EntityCoordinates coords)
    {
        if (!_proto.TryIndex(protoId, out EntityPrototype? entProto))
            return;

        var bound = 1;
        if (entProto.TryGetComponent(out StackComponent? stackTemplate, _factory))
        {
            var stackPrototype = _proto.Index<StackPrototype>(stackTemplate.StackTypeId);
            bound = stackPrototype.MaxCount ?? int.MaxValue;
        }

        for (var left = count; left > 0;)
        {
            var chunk = Math.Min(bound, left);
            var ent = Spawn(protoId, coords);
            if (TryComp<StackComponent>(ent, out var stack))
                _stack.SetCount(ent, chunk, stack);
            left -= chunk;
        }
    }
}
