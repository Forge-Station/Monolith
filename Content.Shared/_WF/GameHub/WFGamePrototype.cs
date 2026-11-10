using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.GameHub;

/// <summary>A game the PDA Gamehub lists. Each one also needs a launcher in the client's WFGameHubSystem.</summary>
[Prototype("wfGame")]
public sealed partial class WFGamePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    /// <summary>Where the server keeps the data players download. Under /GameData, which clients don't ship.</summary>
    [DataField(required: true)]
    public ResPath DataPath;

    /// <summary>File name of the data in the player's game folder.</summary>
    [DataField(required: true)]
    public string DataFile = string.Empty;

    /// <summary>The sounds and music nearby players hear, which every client ships.</summary>
    [DataField(required: true)]
    public ResPath NoisePack;

    /// <summary>Lowest and highest level number the game can report.</summary>
    [DataField]
    public int MinLevel = 1;

    [DataField]
    public int MaxLevel = 1;
}

/// <summary>One player's row on a game's leaderboard.</summary>
[Serializable, NetSerializable]
public readonly record struct WFGameLeaderboardEntry(string Name, int Score, int Levels);
