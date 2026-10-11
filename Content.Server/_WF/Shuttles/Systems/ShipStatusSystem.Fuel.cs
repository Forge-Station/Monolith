using Content.Server.Ame.Components;
using Content.Server.Materials;
using Content.Server.Power.Generator;
using Content.Shared._FarHorizons.Materials;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared._WF.Shuttles;
using Content.Shared.Ame.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Materials;
using Content.Shared.Power.Generator;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Shuttles.Systems;

public sealed partial class ShipStatusSystem
{
    [Dependency] private MaterialStorageSystem _fuelMaterials = default!;
    [Dependency] private SharedSolutionContainerSystem _fuelSolutions = default!;
    [Dependency] private IPrototypeManager _fuelPrototypes = default!;

    private readonly Dictionary<EntityUid, ShipFuelSummary> _fuelReserves = new();

    /// <summary>Samples installed fuel once per grid sweep, including stopped machinery.</summary>
    private void GatherFuel(HashSet<EntityUid> grids)
    {
        _fuelReserves.Clear();
        var generators = EntityQueryEnumerator<FuelGeneratorComponent, TransformComponent>();
        while (generators.MoveNext(out var uid, out _, out var xform))
        {
            if (InstalledFuelSource(uid, xform, grids, out var grid))
                AddFuelReserve(grid, GeneratorReserve(uid));
        }

        var antimatter = EntityQueryEnumerator<AmeControllerComponent, TransformComponent>();
        while (antimatter.MoveNext(out var uid, out var controller, out var xform))
        {
            if (!InstalledFuelSource(uid, xform, grids, out var grid))
                continue;

            AddFuelReserve(grid, TryComp<AmeFuelContainerComponent>(controller.FuelSlot.Item, out var jar)
                ? ReserveFraction(jar.FuelAmount, jar.FuelCapacity)
                : 0f);
        }

        var reactors = EntityQueryEnumerator<NuclearReactorComponent, TransformComponent>();
        while (reactors.MoveNext(out var uid, out var reactor, out var xform))
        {
            if (InstalledFuelSource(uid, xform, grids, out var grid))
                AddFuelReserve(grid, ReactorReserve(reactor));
        }
    }

    private bool InstalledFuelSource(EntityUid uid, TransformComponent xform, HashSet<EntityUid> grids,
        out EntityUid grid)
    {
        grid = xform.GridUid ?? default;
        return xform.Anchored && xform.GridUid != null && grids.Contains(grid)
            && !TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid);
    }

    /// <summary>Uses local accepted fuel only; remote silos and unrelated tank contents are not reserves.</summary>
    private float? GeneratorReserve(EntityUid uid)
    {
        if (TryComp<SolidFuelGeneratorAdapterComponent>(uid, out var solid))
        {
            if (!TryComp<MaterialStorageComponent>(uid, out var storage) || storage.StorageLimit is not { } capacity
                || !float.IsFinite(solid.Multiplier) || solid.Multiplier <= 0f)
                return null;

            var remaining = _fuelMaterials.GetMaterialAmount(uid, solid.FuelMaterial, storage, localOnly: true)
                + solid.FractionalMaterial;
            return ReserveFraction(remaining, capacity);
        }

        if (!TryComp<ChemicalFuelGeneratorAdapterComponent>(uid, out var chemical)
            || !_fuelSolutions.TryGetSolution(uid, chemical.SolutionName, out _, out var solution))
            return null;

        var fuelVolume = 0f;
        var accepted = false;
        foreach (var (reagent, multiplier) in chemical.Reagents)
        {
            if (!float.IsFinite(multiplier) || multiplier <= 0f)
                continue;

            accepted = true;
            fuelVolume += solution.GetTotalPrototypeQuantity(reagent).Float()
                + chemical.FractionalReagents.GetValueOrDefault(reagent) * FixedPoint2.Epsilon.Float();
        }

        return accepted ? ReserveFraction(fuelVolume, solution.MaxVolume.Float()) : null;
    }

    /// <summary>Weights installed fuel rods by fresh usable activity; empty reactors are empty sources.</summary>
    private float? ReactorReserve(NuclearReactorComponent reactor)
    {
        if (reactor.Melted || reactor.ComponentGrid == null)
            return 0f;

        var remaining = 0f;
        var capacity = 0f;
        foreach (var part in reactor.ComponentGrid)
        {
            if (part == null || !part.HasRodType(ReactorPartComponent.RodTypes.FuelRod))
                continue;

            if (!_fuelPrototypes.TryIndex(part.Material, out var material))
                return null;

            var fresh = RodActivity(material.Properties);
            if (!float.IsFinite(fresh))
                return null;
            if (fresh <= 0f)
                continue;

            capacity += fresh;
            if (part.Melted)
                continue;

            var current = RodActivity(part.Properties ?? material.Properties);
            if (!float.IsFinite(current))
                return null;
            remaining += Math.Clamp(current, 0f, fresh);
        }

        return capacity > 0f ? ReserveFraction(remaining, capacity) : 0f;
    }

    /// <summary>Neutron-active fuel converts into half as much ordinary active fuel before becoming spent.</summary>
    private static float RodActivity(MaterialProperties material)
    {
        return 1.5f * Math.Max(0f, material.NeutronRadioactivity) + Math.Max(0f, material.Radioactivity);
    }

    private static float? ReserveFraction(float remaining, float capacity)
    {
        return float.IsFinite(remaining) && float.IsFinite(capacity) && capacity > 0f
            ? Math.Clamp(remaining / capacity, 0f, 1f)
            : null;
    }

    private void AddFuelReserve(EntityUid grid, float? fraction)
    {
        var summary = _fuelReserves.GetValueOrDefault(grid);
        var known = summary.Sources - summary.UnknownSources;
        summary.Sources++;
        if (fraction is { } value)
            summary.Fraction = (summary.Fraction * known + value) / (known + 1);
        else
            summary.UnknownSources++;
        _fuelReserves[grid] = summary;
    }
}
