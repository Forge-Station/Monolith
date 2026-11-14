using Content.Shared._HL.Silicons.Synths.Surgery;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Server.Power.Components;
using Content.Shared.Power.Components;
using Content.Shared.PowerCell;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Containers;

namespace Content.Server._HL.Silicons.Synths.Surgery;

public sealed partial class SynthSurgerySystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SynthSurgeryStepBatteryInsertComponent, SurgeryStepEvent>(OnBatteryInsert);
        SubscribeLocalEvent<SynthSurgeryStepBatteryInsertComponent, SurgeryStepCompleteCheckEvent>(OnBatteryInsertCheck);
        SubscribeLocalEvent<SynthSurgeryStepBatteryInsertComponent, SurgeryCanPerformStepEvent>(OnBatteryInsertCanPerform);
        SubscribeLocalEvent<SynthSurgeryStepBatteryExtractComponent, SurgeryStepEvent>(OnBatteryExtract);
        SubscribeLocalEvent<SynthSurgeryStepBatteryExtractComponent, SurgeryStepCompleteCheckEvent>(OnBatteryExtractCheck);
        InitializeConditions();
    }

    private void OnBatteryExtract(Entity<SynthSurgeryStepBatteryExtractComponent> ent, ref SurgeryStepEvent args)
    {
        if (!_container.TryGetContainer(args.Part, SharedBodySystem.GetOrganContainerId(ent.Comp.Slot), out var container))
            return;

        foreach (var contained in container.ContainedEntities)
        {
            if (!HasComp<PowerCellComponent>(contained))
                continue;

            if (_container.Remove(contained, container))
                _hands.TryPickupAnyHand(args.User, contained);

            return;
        }
    }

    private void OnBatteryExtractCheck(Entity<SynthSurgeryStepBatteryExtractComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!_container.TryGetContainer(args.Part, SharedBodySystem.GetOrganContainerId(ent.Comp.Slot), out var container) ||
            ContainsPowerCell(container))
        {
            args.Cancelled = true;
        }
    }

    private void OnBatteryInsert(Entity<SynthSurgeryStepBatteryInsertComponent> ent, ref SurgeryStepEvent args)
    {
        if (!_container.TryGetContainer(args.Part, SharedBodySystem.GetOrganContainerId(ent.Comp.Slot), out var container))
        {
            return;
        }

        if (ContainsPowerCell(container))
        {
            EnsureComp<SynthPowerCellInsertedComponent>(args.Part);
            return;
        }

        foreach (var tool in args.Tools)
        {
            if (!HasComp<PowerCellComponent>(tool) ||
                !HasComp<BatteryComponent>(tool))
            {
                continue;
            }

            if (_container.Insert(tool, container) ||
                ContainsPowerCell(container))
            {
                EnsureComp<SynthPowerCellInsertedComponent>(args.Part);
                return;
            }

            return;
        }
    }

    private void OnBatteryInsertCheck(Entity<SynthSurgeryStepBatteryInsertComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!HasComp<SynthPowerCellInsertedComponent>(args.Part) ||
            !_container.TryGetContainer(args.Part, SharedBodySystem.GetOrganContainerId(ent.Comp.Slot), out var container) ||
            !ContainsPowerCell(container))
        {
            args.Cancelled = true;
        }
    }

    private void OnBatteryInsertCanPerform(Entity<SynthSurgeryStepBatteryInsertComponent> ent, ref SurgeryCanPerformStepEvent args)
    {
        if (args.Invalid != StepInvalidReason.None)
            return;

        if (!TryGetHeldPowerCell(args.Tools, out var powerCell))
        {
            args.Invalid = StepInvalidReason.MissingTool;
            args.Popup = "You need a power cell to perform this step!";
            return;
        }

        args.ValidTools ??= new Dictionary<EntityUid, float>();
        args.ValidTools[powerCell] = 1f;
    }

    private bool TryGetHeldPowerCell(List<EntityUid> tools, out EntityUid powerCell)
    {
        foreach (var tool in tools)
        {
            if (!HasComp<PowerCellComponent>(tool) ||
                !HasComp<BatteryComponent>(tool))
            {
                continue;
            }

            powerCell = tool;
            return true;
        }

        powerCell = EntityUid.Invalid;
        return false;
    }

    private bool ContainsPowerCell(BaseContainer container)
    {
        foreach (var contained in container.ContainedEntities)
        {
            if (HasComp<PowerCellComponent>(contained))
                return true;
        }

        return false;
    }
}
