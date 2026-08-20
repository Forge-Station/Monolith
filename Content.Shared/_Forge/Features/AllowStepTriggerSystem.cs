using Content.Shared._Forge.Features.Components;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared._Forge.Features;

public sealed partial class AllowStepTriggerSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AllowStepTriggerComponent, StepTriggerAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<AllowStepTriggerComponent, StepTriggeredOnEvent>(OnTriggered);
    }

    private static void OnAttempt(Entity<AllowStepTriggerComponent> ent, ref StepTriggerAttemptEvent args)
    {
        args.Continue = true;
    }

    private void OnTriggered(Entity<AllowStepTriggerComponent> ent, ref StepTriggeredOnEvent args)
    {
        if (!_net.IsServer)
            return;

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg"), ent,
            AudioParams.Default.WithVolume(-4f));
    }
}
