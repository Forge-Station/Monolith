using Content.Shared.Movement.Events;
using Robust.Shared.Audio;

namespace Content.Shared._WF.GroundCover;

/// <summary>Steps on frozen water sound of ice rather than a splash, or crunch when snow covers it.</summary>
public sealed class WFFrozenWaterSystem : EntitySystem
{
    [Dependency] private SharedWFSnowSlowSystem _snow = default!;

    private static readonly SoundSpecifier IceSteps = new SoundCollectionSpecifier("FootstepFloor");
    private static readonly SoundSpecifier SnowSteps = new SoundCollectionSpecifier("FootstepSnow");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFFreezableWaterComponent, GetFootstepSoundEvent>(OnFootstep);
    }

    private void OnFootstep(Entity<WFFreezableWaterComponent> ent, ref GetFootstepSoundEvent args)
    {
        if (!ent.Comp.Frozen || args.Sound != null)
            return;

        // Left unset, the water's own splash would follow, so snow on the ice answers here.
        args.Sound = _snow.OnCover(args.User) ? SnowSteps : IceSteps;
    }
}
