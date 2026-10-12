using System.Linq;
using Content.Server.Disposal.Tube;
using Content.Server.Disposal.Unit;
using Content.Shared._Forge.OrePipe;
using Content.Shared.Disposal.Components;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.OrePipe;

public sealed class OreDisposalFilterSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OreDisposalFilterComponent, GetDisposalsConnectableDirectionsEvent>(OnGetConnectableDirections);
        SubscribeLocalEvent<OreDisposalFilterComponent, GetDisposalsNextDirectionEvent>(OnGetNextDirection);

        Subs.BuiEvents<OreDisposalFilterComponent>(OreDisposalFilterUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<OreDisposalFilterSetMessage>(OnSetFilter);
        });
    }

    private void OnGetConnectableDirections(
        EntityUid uid,
        OreDisposalFilterComponent component,
        ref GetDisposalsConnectableDirectionsEvent args)
    {
        var direction = Transform(uid).LocalRotation;
        args.Connectable = component.Degrees
            .Select(degree => new Angle(degree.Theta + direction.Theta).GetDir())
            .ToArray();
    }

    private void OnGetNextDirection(
        EntityUid uid,
        OreDisposalFilterComponent component,
        ref GetDisposalsNextDirectionEvent args)
    {
        var ev = new GetDisposalsConnectableDirectionsEvent();
        RaiseLocalEvent(uid, ref ev);

        // Unfiltered ores continue straight; filtered ores divert to the side.
        if (HolderIsFiltered(args.Holder, component))
        {
            args.Next = ev.Connectable.Length > 1
                ? ev.Connectable[1]
                : Transform(uid).LocalRotation.GetDir();
            return;
        }

        args.Next = Transform(uid).LocalRotation.GetDir();
    }

    private bool HolderIsFiltered(DisposalHolderComponent holder, OreDisposalFilterComponent filter)
    {
        // Empty filter = pass everything straight.
        if (filter.Filtered.Count == 0 || holder.Container.ContainedEntities.Count == 0)
            return false;

        foreach (var ent in holder.Container.ContainedEntities)
        {
            if (!TryComp<StackComponent>(ent, out var stack))
                continue;

            if (filter.Filtered.Contains(stack.StackTypeId))
                return true;
        }

        return false;
    }

    private void OnUiOpened(EntityUid uid, OreDisposalFilterComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, component);
    }

    private void OnSetFilter(EntityUid uid, OreDisposalFilterComponent component, OreDisposalFilterSetMessage args)
    {
        if (TryComp<PhysicsComponent>(uid, out var physBody) && physBody.BodyType != BodyType.Static)
            return;

        component.Filtered.Clear();
        foreach (var id in args.Filtered)
        {
            if (!_proto.HasIndex<StackPrototype>(id))
                continue;

            var allowed = false;
            foreach (var catalogId in OreDisposalFilterCatalog.StackTypes)
            {
                if (catalogId != id)
                    continue;
                allowed = true;
                break;
            }

            if (!allowed)
                continue;

            component.Filtered.Add(id);
        }

        _audio.PlayPvs(component.ClickSound, uid, AudioParams.Default.WithVolume(-2f));
        UpdateUi(uid, component);
    }

    private void UpdateUi(EntityUid uid, OreDisposalFilterComponent component)
    {
        if (!_ui.HasUi(uid, OreDisposalFilterUiKey.Key))
            return;

        var options = new List<OreDisposalFilterOption>();
        foreach (var stackId in OreDisposalFilterCatalog.StackTypes)
        {
            if (!_proto.TryIndex(stackId, out StackPrototype? stack))
                continue;

            options.Add(new OreDisposalFilterOption(
                stackId,
                GetOreDisplayName(stack),
                component.Filtered.Contains(stackId)));
        }

        options.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        _ui.SetUiState(uid, OreDisposalFilterUiKey.Key, new OreDisposalFilterBoundUserInterfaceState(options));
    }

    private string GetOreDisplayName(StackPrototype stack)
    {
        var entityKey = $"ent-{stack.Spawn}";
        if (Loc.TryGetString(entityKey, out var entityName))
            return entityName;

        if (!string.IsNullOrEmpty(stack.Name) && Loc.TryGetString(stack.Name, out var stackName))
            return stackName;

        return stack.Name;
    }
}
