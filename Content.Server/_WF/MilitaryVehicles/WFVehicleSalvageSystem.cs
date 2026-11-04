using Content.Server.Cargo.Systems;
using Content.Server._WF.NpcVehicleCrew;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Content.Shared.Stacks;
using Robust.Shared.Containers;

namespace Content.Server._WF.MilitaryVehicles;

/// <summary>
/// Salvage pricing: an NPC crew's vehicle, one claimed from a crew and the parts stripped from either appraise for
/// <see cref="SalvageResale"/> of their new value, wherever they are sold.
/// </summary>
public sealed class WFVehicleSalvageSystem : EntitySystem
{
    /// <summary>Share of the new appraisal salvage sells for.</summary>
    public const float SalvageResale = 0.25f;

    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private PricingSystem _pricing = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSalvagedComponent, PriceCalculationEvent>(OnSalvagedPrice);
        SubscribeLocalEvent<WFSalvagedComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFVehicleCrewComponent, PriceCalculationEvent>(OnCrewVehiclePrice);
        SubscribeLocalEvent<HardpointItemComponent, EntGotRemovedFromContainerMessage>(OnPartRemoved);
    }

    private void OnSalvagedPrice(Entity<WFSalvagedComponent> ent, ref PriceCalculationEvent args)
    {
        if (args.Handled)
            return;

        args.Price = SalvagePrice(ent, ent.Comp.Resale, false);
        args.Handled = true;
    }

    /// <summary>A crew's own vehicle, not yet claimed, is salvage through and through.</summary>
    private void OnCrewVehiclePrice(Entity<WFVehicleCrewComponent> ent, ref PriceCalculationEvent args)
    {
        if (args.Handled)
            return;

        args.Price = SalvagePrice(ent, SalvageResale, true);
        args.Handled = true;
    }

    private void OnExamined(Entity<WFSalvagedComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-salvaged-examine"));
    }

    /// <summary>A part taken off an NPC crew's vehicle, or off a part still on one, is salvage.</summary>
    private void OnPartRemoved(Entity<HardpointItemComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (TerminatingOrDeleted(ent) || HasComp<WFSalvagedComponent>(ent))
            return;

        var holder = args.Container.Owner;
        for (var depth = 0; depth < 8; depth++)
        {
            if (HasComp<WFVehicleCrewComponent>(holder))
            {
                MarkSalvaged(ent);
                return;
            }

            if (!_container.TryGetContainingContainer(holder, out var outer))
                return;

            holder = outer.Owner;
        }
    }

    /// <summary>Marks an entity and everything in it, bar the living or dead, as salvage.</summary>
    public void MarkSalvaged(EntityUid uid)
    {
        if (HasComp<MobStateComponent>(uid))
            return;

        EnsureComp<WFSalvagedComponent>(uid);
        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var child in container.ContainedEntities)
                MarkSalvaged(child);
        }
    }

    /// <summary>
    /// The salvage share of an entity's own new value, plus its contents: priced as salvage too when the whole tree is,
    /// else as they stand, so a part fitted after a claim keeps its full value.
    /// </summary>
    public double SalvagePrice(EntityUid uid, float resale, bool wholeTree)
    {
        var price = NewValue(uid) * resale;
        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return price;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var child in container.ContainedEntities)
            {
                price += wholeTree && !HasComp<MobStateComponent>(child) && !HasComp<WFSalvagedComponent>(child)
                    ? SalvagePrice(child, resale, true)
                    : _pricing.GetPrice(child);
            }
        }

        return price;
    }

    /// <summary>What the entity alone appraises for new: its prototype's estimate, scaled to the size of a stack.</summary>
    private double NewValue(EntityUid uid)
    {
        if (MetaData(uid).EntityPrototype is not { } proto)
            return 0;

        var value = _pricing.GetEstimatedPrice(proto);
        if (TryComp<StackComponent>(uid, out var stack)
            && proto.TryGetComponent<StackComponent>(out var protoStack, EntityManager.ComponentFactory)
            && protoStack.Count > 0)
            value *= (double) stack.Count / protoStack.Count;

        return value;
    }
}
