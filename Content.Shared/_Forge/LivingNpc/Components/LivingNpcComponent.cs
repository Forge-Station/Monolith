using Content.Shared._Forge.LivingNpc.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.LivingNpc.Components;

/// <summary>
/// Forge living-NPC brain. Independent from HTN: personality, mood, and a utility intent loop.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class LivingNpcComponent : Component
{
    [DataField]
    public ProtoId<LivingNpcPersonalityPrototype>? PersonalityId;

    [DataField]
    public ProtoId<LivingNpcDialoguePrototype> DialogueId = "LivingNpcDialogueCivilian";

    [DataField]
    public LivingNpcPersonality Personality { get; set; } = new();

    [DataField]
    public LivingNpcMood Mood { get; set; } = new();

    [DataField]
    public string? JobTitle;

    /// <summary>
    /// NPCs with the same group prefer chatting with each other.
    /// </summary>
    [DataField]
    public string? Group;

    [DataField]
    public float VisionRange = 8f;

    [DataField]
    public float HearRange = 8f;

    [DataField]
    public float HomeRadius = 14f;

    [DataField]
    public float WanderRange = 6f;

    [DataField]
    public float ThinkInterval = 0.45f;

    [DataField]
    public float ThinkJitter = 0.2f;

    /// <summary>
    /// If false, this person will never choose to fight — they flee or freeze instead.
    /// </summary>
    [DataField]
    public bool WillFight;

    [DataField]
    public bool Enabled = true;

    [ViewVariables]
    public LivingNpcIntent CurrentIntent = LivingNpcIntent.Idle;

    [ViewVariables]
    public EntityUid? CurrentTarget;

    [ViewVariables]
    public EntityCoordinates? Destination;

    [ViewVariables]
    public EntityCoordinates Home;

    [ViewVariables]
    public bool HomeSet;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextThink;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan IntentStartedAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextUtterance;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextEmote;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan LastSocialAt;

    [ViewVariables]
    public string? QueuedSpeech;

    [ViewVariables]
    public string? QueuedEmoteId;

    [ViewVariables]
    public EntityUid? ConversationPartner;

    [ViewVariables]
    public int FailedMoves;

    [ViewVariables]
    public bool Asleep;
}
