using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Grid;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Vehicles.Bison;

/// <summary>The parts of the Bison that both sides see: an armed carrier stops running, and a hard hit makes its own sound.</summary>
public sealed partial class SharedCMUCargoVehicleSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CMUCargoVehicleComponent, VehicleCanRunEvent>(OnVehicleCanRun);
        SubscribeLocalEvent<CMUCargoVehicleComponent, VehicleCollisionEvent>(OnVehicleCollision);
    }

    private void OnVehicleCanRun(Entity<CMUCargoVehicleComponent> ent, ref VehicleCanRunEvent args)
    {
        if (ent.Comp.ArmingMode == CMUCargoVehicleArmingMode.Automatic)
            args.CanRun = false;
    }

    /// <summary>The mover raises the event on the server only, once for a hit and once for a blocked drive step.</summary>
    private void OnVehicleCollision(Entity<CMUCargoVehicleComponent> ent, ref VehicleCollisionEvent args)
    {
        if (ent.Comp.CollisionSound == null || MathF.Abs(args.Speed) < ent.Comp.CollisionSoundMinSpeed)
            return;

        var now = _timing.CurTime;
        if (ent.Comp.NextCollisionSound > now)
            return;

        _audio.PlayPvs(ent.Comp.CollisionSound, ent.Owner);
        ent.Comp.NextCollisionSound = now + TimeSpan.FromSeconds(ent.Comp.CollisionSoundCooldown);
    }
}
