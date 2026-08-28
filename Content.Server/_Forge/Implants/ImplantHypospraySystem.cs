using Content.Server.Chemistry.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Implants.Components;

namespace Content.Server._Forge.Implants;

/// <summary>
/// Injects hypospray solution from subdermal implants when their trigger fires (crit, implant action, etc.).
/// </summary>
public sealed class ImplantHypospraySystem : EntitySystem
{
    [Dependency] private readonly HypospraySystem _hypospray = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<HyposprayComponent, TriggerEvent>(OnHyposprayTrigger);
    }

    private void OnHyposprayTrigger(Entity<HyposprayComponent> ent, ref TriggerEvent args)
    {
        if (!TryComp<SubdermalImplantComponent>(ent, out var implant) || implant.ImplantedEntity == null)
            return;

        var target = implant.ImplantedEntity.Value;
        var user = args.User ?? target;

        if (_hypospray.TryDoInject(ent, target, user))
            args.Handled = true;
    }
}
