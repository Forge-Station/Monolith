using System.Diagnostics.CodeAnalysis;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Lathe;

/// <summary>
/// Links lathes to fabrication silos the way the ore silo links them, and reads silo stock on both sides.
/// </summary>
public abstract partial class SharedFabricationSiloSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedOreSiloSystem _oreSilo = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;

    private EntityQuery<FabricationSiloClientComponent> _clientQuery;
    private EntityQuery<FabricationSiloComponent> _siloQuery;
    private EntityQuery<StackComponent> _stackQuery;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FabricationSiloComponent, ComponentInit>(OnSiloInit);
        SubscribeLocalEvent<FabricationSiloComponent, ComponentShutdown>(OnSiloShutdown);
        SubscribeLocalEvent<FabricationSiloComponent, ToggleOreSiloClientMessage>(OnToggleClient);
        Subs.BuiEvents<FabricationSiloComponent>(OreSiloUiKey.Key,
            subs => subs.Event<BoundUIOpenedEvent>(OnUiOpened));
        SubscribeLocalEvent<FabricationSiloClientComponent, ComponentShutdown>(OnClientShutdown);

        _clientQuery = GetEntityQuery<FabricationSiloClientComponent>();
        _siloQuery = GetEntityQuery<FabricationSiloComponent>();
        _stackQuery = GetEntityQuery<StackComponent>();

        InitializeWhitelist();
        InitializeFilling();
    }

    /// <summary>
    /// Whether a silo is powered and can reach a machine, using the ore silo's rules.
    /// </summary>
    public bool CanTransmit(Entity<FabricationSiloComponent> silo, EntityUid client)
    {
        return _oreSilo.CanTransmit(silo.Owner, client, silo.Comp.Range);
    }

    /// <summary>
    /// The silo of this kind that currently supplies a machine, if any.
    /// </summary>
    public Entity<FabricationSiloComponent>? GetLinkedSilo(EntityUid client, FabricationSiloKind kind)
    {
        if (!_clientQuery.TryComp(client, out var link) ||
            GetLink(link, kind) is not { } uid ||
            !_siloQuery.TryComp(uid, out var silo) ||
            silo.Kind != kind ||
            !CanTransmit((uid, silo), client))
            return null;

        return (uid, silo);
    }

    /// <summary>
    /// Count of a part in a machine's linked parts silo.
    /// </summary>
    public int GetPartAmount(EntityUid client, EntProtoId prototype)
    {
        if (GetLinkedSilo(client, FabricationSiloKind.Parts) is not { Comp.Parts: { } parts })
            return 0;

        var count = 0;
        foreach (var part in parts.ContainedEntities)
        {
            if (IsRecipePart(part, prototype))
                count += GetItemCount(part);
        }

        return count;
    }

    /// <summary>
    /// Amount of a reagent, in any data variant, in a machine's linked chemical silo.
    /// </summary>
    public FixedPoint2 GetReagentAmount(EntityUid client, ProtoId<ReagentPrototype> reagent)
    {
        if (GetLinkedSilo(client, FabricationSiloKind.Chemicals) is not { } silo ||
            !TryGetSolution(silo, out _, out var solution))
            return FixedPoint2.Zero;

        return solution.GetTotalPrototypeQuantity(reagent.Id);
    }

    /// <summary>
    /// A chemical silo's stored solution.
    /// </summary>
    public bool TryGetSolution(Entity<FabricationSiloComponent> silo,
        [NotNullWhen(true)] out Entity<SolutionComponent>? soln,
        [NotNullWhen(true)] out Solution? solution)
    {
        soln = null;
        solution = null;
        return silo.Comp.Kind == FabricationSiloKind.Chemicals &&
               _solution.TryGetSolution(silo.Owner, silo.Comp.SolutionName, out soln, out solution);
    }

    /// <summary>
    /// The container in a chemical silo's slot and its solution.
    /// </summary>
    public bool TryGetSlottedSolution(Entity<FabricationSiloComponent> silo,
        out EntityUid container,
        [NotNullWhen(true)] out Entity<SolutionComponent>? soln,
        [NotNullWhen(true)] out Solution? solution)
    {
        container = default;
        soln = null;
        solution = null;
        if (silo.Comp.Kind != FabricationSiloKind.Chemicals ||
            _itemSlots.GetItemOrNull(silo.Owner, FabricationSiloComponent.ContainerSlotId) is not { } item)
            return false;

        container = item;
        return _solution.TryGetFitsInDispenser(item, out soln, out solution);
    }

    /// <summary>
    /// Items a stored part counts for.
    /// </summary>
    public int GetItemCount(EntityUid part)
    {
        return _stackQuery.TryComp(part, out var stack) ? stack.Count : 1;
    }

    /// <summary>
    /// Shows the silo's machine list to open windows; the server builds it.
    /// </summary>
    protected virtual void UpdateUi(Entity<FabricationSiloComponent> ent)
    {
    }

    /// <summary>
    /// Refreshes a machine's own window after its supplies changed; the server sends it.
    /// </summary>
    protected virtual void RefreshClient(EntityUid client)
    {
    }

    private void OnSiloInit(Entity<FabricationSiloComponent> ent, ref ComponentInit args)
    {
        if (ent.Comp.Kind == FabricationSiloKind.Parts)
            ent.Comp.Parts = _container.EnsureContainer<Container>(ent.Owner, FabricationSiloComponent.PartsContainerId);
    }

    private void OnToggleClient(Entity<FabricationSiloComponent> ent, ref ToggleOreSiloClientMessage args)
    {
        var client = GetEntity(args.Client);
        if (!_clientQuery.TryComp(client, out var clientComp))
            return;

        if (ent.Comp.Clients.Contains(client))
        {
            SetLink((client, clientComp), ent.Comp.Kind, null);
            ent.Comp.Clients.Remove(client);
            Dirty(ent);
        }
        else
        {
            if (!CanTransmit(ent, client))
                return;

            // A machine has one silo of each kind, so linking here drops its old one.
            if (GetLink(clientComp, ent.Comp.Kind) is { } old && _siloQuery.TryComp(old, out var previous))
            {
                previous.Clients.Remove(client);
                Dirty(old, previous);
                UpdateUi((old, previous));
            }

            ent.Comp.Clients.Add(client);
            Dirty(ent);
            SetLink((client, clientComp), ent.Comp.Kind, ent.Owner);
        }

        UpdateUi(ent);
        RefreshClient(client);
    }

    private void OnUiOpened(Entity<FabricationSiloComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnSiloShutdown(Entity<FabricationSiloComponent> ent, ref ComponentShutdown args)
    {
        foreach (var client in ent.Comp.Clients)
        {
            if (!_clientQuery.TryComp(client, out var comp) || GetLink(comp, ent.Comp.Kind) != ent.Owner)
                continue;

            SetLink((client, comp), ent.Comp.Kind, null);
            RefreshClient(client);
        }
    }

    private void OnClientShutdown(Entity<FabricationSiloClientComponent> ent, ref ComponentShutdown args)
    {
        foreach (var uid in new[] { ent.Comp.PartsSilo, ent.Comp.ChemicalSilo })
        {
            if (!_siloQuery.TryComp(uid, out var silo))
                continue;

            silo.Clients.Remove(ent);
            Dirty(uid.Value, silo);
            UpdateUi((uid.Value, silo));
        }
    }

    private static EntityUid? GetLink(FabricationSiloClientComponent client, FabricationSiloKind kind)
    {
        return kind == FabricationSiloKind.Parts ? client.PartsSilo : client.ChemicalSilo;
    }

    private void SetLink(Entity<FabricationSiloClientComponent> client, FabricationSiloKind kind, EntityUid? silo)
    {
        if (kind == FabricationSiloKind.Parts)
            client.Comp.PartsSilo = silo;
        else
            client.Comp.ChemicalSilo = silo;

        Dirty(client);
    }
}
