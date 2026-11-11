namespace Content.Server.Chat;

using Content.Server.Chat.Systems;
using Content.Shared.Chat.Prototypes;
using Robust.Shared.Prototypes; // WOLFGATE(Wolfmed): HOOK 17, ProtoId<> for the Wolfmed pain-sound thresholds.
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

/// <summary>
/// Causes an entity to automatically emote when taking damage.
/// </summary>
[RegisterComponent, Access(typeof(EmoteOnDamageSystem)), AutoGenerateComponentPause]
public sealed partial class EmoteOnDamageComponent : Component
{
    /// <summary>
    /// Chance of preforming an emote when taking damage and not on cooldown.
    /// </summary>
    [DataField("emoteChance"), ViewVariables(VVAccess.ReadWrite)]
    public float EmoteChance = 0.5f;

    /// <summary>
    /// A set of emotes that will be randomly picked from.
    /// <see cref="EmotePrototype"/>
    /// </summary>
    [DataField("emotes", customTypeSerializer: typeof(ProtoId<EmotePrototype>)), ViewVariables(VVAccess.ReadWrite)]
    public HashSet<string> Emotes = new();

    /// <summary>
    /// Also send the emote in chat.
    /// <summary>
    [DataField("withChat"), ViewVariables(VVAccess.ReadWrite)]
    public bool WithChat = false;

    /// <summary>
    /// Hide the chat message from the chat window, only showing the popup.
    /// This does nothing if WithChat is false.
    /// <summary>
    [DataField("hiddenFromChatWindow")]
    public bool HiddenFromChatWindow = false;

    /// <summary>
    /// The simulation time of the last emote preformed due to taking damage.
    /// </summary>
    [DataField("lastEmoteTime", customTypeSerializer: typeof(TimeOffsetSerializer)), ViewVariables(VVAccess.ReadWrite)]
    [AutoPausedField]
    public TimeSpan LastEmoteTime = TimeSpan.Zero;

    /// <summary>
    /// The cooldown between emotes.
    /// </summary>
    [DataField("emoteCooldown"), ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan EmoteCooldown = TimeSpan.FromSeconds(2);

    // WOLFGATE(Wolfmed) START: HOOK 17, Wolfmed pain-sound fields.
    // Ported from ONYX Content.Server/Chat/EmoteOnDamageComponent.cs <Onyx-PainSounds>. Purely additive: Onyx
    // replaces Emotes with EmotesThreshold, we keep both so ZombieSystem's AddEmote(uid, "Scream") call sites are
    // untouched (P2-D9).

    /// <summary>
    /// Emotes keyed by the total damage at which they unlock; the highest matching threshold wins.
    /// <see cref="EmotePrototype"/>
    /// </summary>
    [DataField("emotesThreshold"), ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<float, HashSet<ProtoId<EmotePrototype>>> EmotesThreshold = new();

    /// <summary>
    /// Damage types that count towards the pain that triggers a threshold emote.
    /// </summary>
    [DataField("allowedDamageType"), ViewVariables(VVAccess.ReadWrite)]
    public HashSet<string> AllowedDamageType = ["Blunt", "Caustic", "Heat", "Cold", "Piercing", "Shock", "Slash"];

    /// <summary>
    /// Minimum pain in a single hit before a threshold emote is considered.
    /// </summary>
    [DataField("painThreshold"), ViewVariables(VVAccess.ReadWrite)]
    public float PainThreshold = 6f;

    /// <summary>
    /// Total damage at the last threshold check, used to derive the delta.
    /// </summary>
    [ViewVariables]
    public float LastTotalDamage;
    // WOLFGATE END
}
