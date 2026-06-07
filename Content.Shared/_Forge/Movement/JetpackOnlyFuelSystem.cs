using Content.Shared._Forge.Movement.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Clothing.Components;
using Content.Shared.Storage;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Shared._Forge.Movement;

public sealed class JetpackOnlyFuelSystem : EntitySystem
{
    [Dependency] private readonly SharedGasTankSystem _gasTank = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JetpackOnlyFuelComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<JetpackOnlyFuelComponent> ent, ref MapInitEvent args)
    {
        if (TryComp<UserInterfaceComponent>(ent, out var ui))
        {
            var hasOtherUi = _ui.HasUi(ent, StorageComponent.StorageUiKey.Key, ui)
                || _ui.HasUi(ent, ToggleClothingUiKey.Key, ui);

            if (!hasOtherUi)
                RemComp<UserInterfaceComponent>(ent);
        }

        RemComp<ActivatableUIComponent>(ent);

        if (TryComp<GasTankComponent>(ent, out var gasTank) && gasTank.IsConnected)
            _gasTank.DisconnectFromInternals((ent, gasTank));
    }
}
