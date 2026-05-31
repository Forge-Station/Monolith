using Content.Shared.Overlays;
using Content.Shared._Forge.Guard.Components;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Inventory;

namespace Content.Client.Overlays;

public sealed partial class ShowGuardIconsSystem : EquipmentHudSystem<ShowGuardIconsComponent>
{
    [Dependency] private IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GuardComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);

    }

    private void OnGetStatusIconsEvent(EntityUid uid, GuardComponent component, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        if (_prototype.TryIndex(component.GuardStatusIcon, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
    }
}
