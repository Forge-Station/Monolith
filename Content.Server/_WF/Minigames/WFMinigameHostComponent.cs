using Content.Shared._WF.Minigames;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Minigames;

/// <summary>
/// A trader who plays games for a stake. Each dialogue option with the <c>PlayMinigame</c> action names one of
/// <see cref="Offers"/> by index in its argument.
/// </summary>
[RegisterComponent]
public sealed partial class WFMinigameHostComponent : Component
{
    [DataField(required: true)]
    public List<MinigameOffer> Offers = new();

    /// <summary>How long a player who won must wait before this host plays them again.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    /// <summary>Most this host pays out per <see cref="CapPeriod"/>; it turns down a game whose prize would pass it.</summary>
    [DataField]
    public int DailyCap = 30000;

    [DataField]
    public TimeSpan CapPeriod = TimeSpan.FromHours(24);

    /// <summary>How long the NPC "thinks" before its move.</summary>
    [DataField]
    public TimeSpan NpcDelay = TimeSpan.FromSeconds(0.8);

    [DataField]
    public SoundSpecifier MoveSound = new SoundPathSpecifier("/Audio/Machines/beep.ogg");

    [DataField]
    public SoundSpecifier DiceSound = new SoundCollectionSpecifier("Dice");

    [DataField]
    public SoundSpecifier WinSound = new SoundPathSpecifier("/Audio/Effects/kaching.ogg");

    [DataField]
    public SoundSpecifier LoseSound = new SoundPathSpecifier("/Audio/Machines/buzz-two.ogg");

    /// <summary>Offer the customer was asked to confirm.</summary>
    [ViewVariables]
    public int? PendingOffer;

    /// <summary>The game in progress or just finished, shown until the window closes.</summary>
    [ViewVariables]
    public MinigameGame? Game;

    [ViewVariables]
    public EntityUid? Player;

    [ViewVariables]
    public int Offer;

    [ViewVariables]
    public bool Settled;

    [ViewVariables]
    public TimeSpan? NpcMoveAt;

    /// <summary>When each winner (user id, or entity for an NPC body) may play again.</summary>
    [ViewVariables]
    public Dictionary<string, TimeSpan> Cooldowns = new();

    [ViewVariables]
    public int PaidOut;

    [ViewVariables]
    public TimeSpan CapResetAt;
}

/// <summary>One game a host offers, its table stakes and its prize.</summary>
[DataDefinition]
public sealed partial class MinigameOffer
{
    [DataField(required: true)]
    public MinigameKind Game;

    [DataField]
    public MinigameDifficulty Difficulty = MinigameDifficulty.Normal;

    /// <summary>Cash taken to play, from the counter then the bank.</summary>
    [DataField(required: true)]
    public int Stake;

    /// <summary>Items paid for a win instead of cash. What they count toward the daily cap is the cash prize.</summary>
    [DataField]
    public List<EntProtoId> Rewards = new();
}
