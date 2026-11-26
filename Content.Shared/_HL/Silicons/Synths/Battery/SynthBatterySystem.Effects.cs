using Content.Shared.Movement.Systems;

namespace Content.Shared._HL.Silicons.Synths.Battery;

public sealed partial class SynthBatteryEffectsSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SynthBatteryComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SynthBatteryComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SynthBatteryComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
    }

    public void RefreshUnpoweredEffects(Entity<SynthBatteryComponent> ent)
    {
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnStartup(Entity<SynthBatteryComponent> ent, ref ComponentStartup args)
    {
        RefreshUnpoweredEffects(ent);
    }

    private void OnShutdown(Entity<SynthBatteryComponent> ent, ref ComponentShutdown args)
    {
        var ev = new SynthBatteryShutdownEvent();
        RaiseLocalEvent(ent.Owner, ref ev);
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnRefreshMovementSpeed(Entity<SynthBatteryComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!ent.Comp.Unpowered)
            return;

        args.ModifySpeed(ent.Comp.UnpoweredWalkSpeedModifier, ent.Comp.UnpoweredSprintSpeedModifier);
    }
}

[ByRefEvent]
public readonly record struct SynthBatteryShutdownEvent;
