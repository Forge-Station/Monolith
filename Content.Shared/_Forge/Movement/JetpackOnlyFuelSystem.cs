using Content.Shared._Forge.Movement.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.UserInterface;

namespace Content.Shared._Forge.Movement;

public sealed class JetpackOnlyFuelSystem : EntitySystem
{
    [Dependency] private readonly SharedGasTankSystem _gasTank = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JetpackOnlyFuelComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<JetpackOnlyFuelComponent> ent, ref MapInitEvent args)
    {
        RemComp<UserInterfaceComponent>(ent);

        if (TryComp<GasTankComponent>(ent, out var gasTank) && gasTank.IsConnected)
            _gasTank.DisconnectFromInternals((ent, gasTank));
    }
}
