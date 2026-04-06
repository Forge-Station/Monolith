using Content.Shared.Containers.ItemSlots;
using Content.Shared._Forge.Access.Components;
using Robust.Shared.Log;

namespace Content.Shared._Forge.Access.Systems;

public abstract class SharedJobPresetIdCardConsoleSystem : EntitySystem
{
    public const string Sawmill = "job-preset-id-card-console";

    [Dependency] protected readonly ItemSlotsSystem ItemSlotsSystem = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    protected ISawmill Log = default!;

    public override void Initialize()
    {
        base.Initialize();

        Log = _logManager.GetSawmill(Sawmill);

        SubscribeLocalEvent<JobPresetIdCardConsoleComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<JobPresetIdCardConsoleComponent, ComponentRemove>(OnComponentRemove);
    }

    private void OnComponentInit(EntityUid uid, JobPresetIdCardConsoleComponent component, ComponentInit args)
    {
        ItemSlotsSystem.AddItemSlot(uid, JobPresetIdCardConsoleComponent.PrivilegedIdCardSlotId, component.PrivilegedIdSlot);
        ItemSlotsSystem.AddItemSlot(uid, JobPresetIdCardConsoleComponent.TargetIdCardSlotId, component.TargetIdSlot);
    }

    private void OnComponentRemove(EntityUid uid, JobPresetIdCardConsoleComponent component, ComponentRemove args)
    {
        ItemSlotsSystem.RemoveItemSlot(uid, component.PrivilegedIdSlot);
        ItemSlotsSystem.RemoveItemSlot(uid, component.TargetIdSlot);
    }
}
