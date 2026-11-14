using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Shared._NF.CryoSleep;

/// <summary>
/// Cryosleep already announces the sleeper. This names the channel for a job, instead of the common channel.
/// </summary>
[RegisterComponent]
public sealed partial class RadioOnCryosleepComponent : Component
{
    [DataField(required: true)]
    public ProtoId<RadioChannelPrototype> RadioChannel;
}
