using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.LivingNpc.Components;

/// <summary>
/// Speech / greeting bookkeeping for a living NPC.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class LivingNpcSocialComponent : Component
{
    [DataField]
    public float ChatRange = 5.5f;

    [DataField]
    public float GreetRange = 3.5f;

    [DataField]
    public TimeSpan MinSpeechGap = TimeSpan.FromSeconds(7);

    [DataField]
    public TimeSpan MaxSpeechGap = TimeSpan.FromSeconds(22);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextIdleChat;

    [ViewVariables]
    public HashSet<EntityUid> Greeted { get; } = new();
}
