using System.Linq;
using Content.Server.Body.Components;
using Content.Shared._WF.Biotype;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Prototypes;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._WF.Biotype;

/// <summary>Converts a live mob to its profile's biotype with the matching <see cref="WFBiotypePrototype"/>.</summary>
public sealed partial class WFBiotypeSystem : SharedWFBiotypeSystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    private readonly Dictionary<WFBiotype, WFBiotypePrototype> _overlays = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        LoadOverlays();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WFBiotypePrototype>())
            LoadOverlays();
    }

    /// <summary>Indexes the overlays by biotype; a missing or doubled biotype or an unknown component name is an error.</summary>
    private void LoadOverlays()
    {
        _overlays.Clear();
        foreach (var overlay in Proto.EnumeratePrototypes<WFBiotypePrototype>())
        {
            if (overlay.Biotype == WFBiotype.Default || !_overlays.TryAdd(overlay.Biotype, overlay))
                Log.Error($"wfBiotype {overlay.ID}: biotype {overlay.Biotype} is Default or already has an overlay");

            foreach (var name in overlay.Remove)
            {
                if (_factory.GetComponentAvailability(name) == ComponentAvailability.Unknown)
                    Log.Error($"wfBiotype {overlay.ID}: unknown component {name} in remove");
            }
        }

        foreach (var biotype in new[] { WFBiotype.Organic, WFBiotype.Synthetic })
        {
            if (!_overlays.ContainsKey(biotype))
                Log.Error($"No wfBiotype overlay for {biotype}");
        }
    }

    /// <inheritdoc/>
    public override void Apply(EntityUid mob, WFBiotype stored, SpeciesPrototype species, HumanoidAppearanceComponent? humanoid = null)
    {
        if (TerminatingOrDeleted(mob))
            return;

        var effective = WFBiotypeRules.Resolve(stored, species);
        var marker = CompOrNull<WFBiotypeComponent>(mob);
        var applied = marker?.Applied;
        var current = GetBiotype(mob);

        // The default profile a humanoid loads as it spawns chose nothing, so the mob is taken as it was built.
        if (stored == WFBiotype.Default && marker == null)
            effective = current;

        // A mob that already is the effective biotype (its species' own mob, or one converted before) is left alone.
        if (current != effective)
        {
            if (!_overlays.TryGetValue(effective, out var overlay))
            {
                Log.Error($"No wfBiotype overlay for {effective}, {ToPrettyString(mob)} keeps its body");
                return;
            }

            // Converted back to its species' own biotype, it gets its species' own body.
            var native = effective == species.Biotype;
            var body = native && SpeciesBody(species) is { } own ? own : overlay.Body;
            Convert(mob, overlay, body);
            applied = native ? null : overlay.ID;
        }

        marker = EnsureComp<WFBiotypeComponent>(mob);
        if (marker.Biotype != effective || marker.Applied != applied)
        {
            marker.Biotype = effective;
            marker.Applied = applied;
            Dirty(mob, marker);
        }

        if (Resolve(mob, ref humanoid, false) && humanoid.Biotype != effective)
        {
            humanoid.Biotype = effective;
            Dirty(mob, humanoid);
        }
    }

    /// <summary>The body prototype the species' own mob is built with.</summary>
    private ProtoId<BodyPrototype>? SpeciesBody(SpeciesPrototype species)
    {
        return Proto.TryIndex(species.Prototype, out var mob) &&
               mob.TryComp(out BodyComponent? body, _factory)
            ? body.Prototype
            : null;
    }

    /// <summary>Strips, adds and replaces the overlay's components, then rebuilds the body as the given prototype.</summary>
    private void Convert(EntityUid mob, WFBiotypePrototype overlay, ProtoId<BodyPrototype> body)
    {
        foreach (var name in overlay.Remove)
        {
            if (_factory.TryGetRegistration(name, out var reg) && HasComp(mob, reg.Type))
                RemComp(mob, reg.Type);
        }

        foreach (var id in overlay.RemoveItemSlots)
        {
            if (!_itemSlots.TryGetSlot(mob, id, out var slot))
                continue;

            if (slot.Item is { } item)
                Del(item);

            _itemSlots.RemoveItemSlot(mob, slot);
        }

        foreach (var (name, entry) in overlay.Ensure)
        {
            if (entry.Component is ItemSlotsComponent slots && TryComp(mob, out ItemSlotsComponent? own))
                MergeItemSlots(mob, own, slots);
            else if (!HasComp(mob, _factory.GetRegistration(name).Type))
                EntityManager.AddComponents(mob, new ComponentRegistry { [name] = entry }, removeExisting: false);
        }

        if (overlay.Components.ContainsKey(_factory.GetComponentName<BloodstreamComponent>()))
            DrainBloodstream(mob);

        EntityManager.AddComponents(mob, overlay.Components, removeExisting: true);

        _body.WgRebuildBody(mob, body);
        DropStaleHands(mob);

        var ev = new WFBiotypeAppliedEvent(mob, overlay.Biotype);
        RaiseLocalEvent(mob, ref ev, true);
    }

    /// <summary>Adds the overlay's item slots the mob lacks to its own, with their starting items.</summary>
    private void MergeItemSlots(EntityUid mob, ItemSlotsComponent own, ItemSlotsComponent slots)
    {
        foreach (var (id, template) in slots.Slots)
        {
            if (_itemSlots.TryGetSlot(mob, id, out _, own))
                continue;

            var slot = _serialization.CreateCopy(template, notNullableOverride: true);
            _itemSlots.AddItemSlot(mob, id, slot, own);

            if (LifeStage(mob) < EntityLifeStage.MapInitialized || slot.StartingItem is not { } starting ||
                slot.ContainerSlot is not { } container)
                continue;

            var item = Spawn(starting, Transform(mob).Coordinates);
            if (!_containers.Insert(item, container))
                Del(item);
        }
    }

    /// <summary>
    /// Empties the old bloodstream's solutions. The replacing component keeps the solutions it finds and fills its blood
    /// up to its own volume with its own fluid, so it has to find them empty. Before init there is nothing to drain.
    /// </summary>
    private void DrainBloodstream(EntityUid mob)
    {
        if (!Initialized(mob) || !TryComp(mob, out BloodstreamComponent? blood))
            return;

        foreach (var name in new[] { blood.BloodSolutionName, blood.ChemicalSolutionName, blood.BloodTemporarySolutionName })
        {
            if (_solutions.TryGetSolution(mob, name, out var solution))
                _solutions.RemoveAllSolution(solution.Value);
        }
    }

    /// <summary>Body-part hands the new body has no part for are removed; what they held drops at the mob's feet.</summary>
    private void DropStaleHands(EntityUid mob)
    {
        if (!TryComp(mob, out HandsComponent? hands))
            return;

        var kept = new HashSet<string>();
        foreach (var (part, _) in _body.GetBodyChildrenOfType(mob, BodyPartType.Hand))
        {
            if (_containers.TryGetContainingContainer((part, null, null), out var container))
                kept.Add(container.ID);
        }

        foreach (var name in hands.Hands.Keys.ToList())
        {
            if (name.StartsWith(SharedBodySystem.PartSlotContainerIdPrefix) && !kept.Contains(name))
                _hands.RemoveHand(mob, name, hands);
        }
    }
}
