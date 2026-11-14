using Content.Shared._HL.Silicons.Synths.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared.Body.Systems;
using Content.Shared.PowerCell;

namespace Content.Server._HL.Silicons.Synths.Surgery;

public sealed partial class SynthSurgerySystem
{
    private void InitializeConditions()
    {
        SubscribeLocalEvent<SynthSurgeryBatterySlotEmptyConditionComponent, SurgeryValidEvent>(OnBatterySlotEmptyCondition);
        SubscribeLocalEvent<SynthSurgeryBatterySlotFullConditionComponent, SurgeryValidEvent>(OnBatterySlotFullCondition);
    }

    private void OnBatterySlotEmptyCondition(Entity<SynthSurgeryBatterySlotEmptyConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!_container.TryGetContainer(args.Part, SharedBodySystem.GetOrganContainerId(ent.Comp.Slot), out var container))
        {
            args.Cancelled = true;
            return;
        }

        foreach (var contained in container.ContainedEntities)
        {
            if (!HasComp<PowerCellComponent>(contained))
                continue;

            if (HasComp<SynthPowerCellInsertedComponent>(args.Part))
            {
                return;
            }

            args.Cancelled = true;
            return;
        }
    }

    private void OnBatterySlotFullCondition(Entity<SynthSurgeryBatterySlotFullConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!_container.TryGetContainer(args.Part, SharedBodySystem.GetOrganContainerId(ent.Comp.Slot), out var container))
        {
            args.Cancelled = true;
            return;
        }

        foreach (var contained in container.ContainedEntities)
        {
            if (!HasComp<PowerCellComponent>(contained))
                continue;

            return;
        }

        args.Cancelled = true;
    }
}
