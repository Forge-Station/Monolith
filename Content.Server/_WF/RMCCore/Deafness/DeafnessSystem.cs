using Content.Server.Radio;
using Content.Shared._WF.RMCCore.Deafness;

namespace Content.Server._WF.RMCCore.Deafness;

// WOLFGATE: RMC garbles heard speech for the deaf through a ChatSystem hook (ChatMessageOverrideInVoiceRangeEvent) that isn't ported; deaf mobs still can't hear radios.
public sealed partial class DeafnessSystem : SharedDeafnessSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnRadioReceiveAttempt);
    }

    private void OnRadioReceiveAttempt(ref RadioReceiveAttemptEvent args)
    {
        var user = Transform(args.RadioReceiver).ParentUid;

        if (!HasComp<DeafComponent>(user))
            return;

        args.Cancelled = true;
    }
}
