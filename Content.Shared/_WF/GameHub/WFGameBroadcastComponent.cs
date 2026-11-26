using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared._WF.GameHub;

/// <summary>On a cabinet or PDA while someone plays: tells nearby clients which music to render and loop.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFGameBroadcastComponent : Component
{
    [DataField, AutoNetworkedField]
    public string Game = string.Empty;

    [DataField, AutoNetworkedField]
    public string? Track;

    [DataField, AutoNetworkedField]
    public bool Loop;

    /// <summary>The player, whose own client already plays the music. Null if nobody is playing it.</summary>
    [DataField, AutoNetworkedField]
    public NetUserId? Player;
}

/// <summary>An arcade cabinet that plays a hub game and pays prizes for finished levels.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFGameCabinetComponent : Component
{
    /// <summary>The <see cref="WFGamePrototype"/> this cabinet runs.</summary>
    [DataField]
    public string Game = "WFGameFreedoom";

    [DataField]
    public string LowPrize = "PrizeTicket10";

    [DataField]
    public string HighPrize = "PrizeTicket30";

    /// <summary>Level score that earns the high prize.</summary>
    [DataField]
    public int HighPrizeScore = 2000;
}

/// <summary>Puts the Gamehub program on a PDA when it spawns.</summary>
[RegisterComponent]
public sealed partial class WFGameHubInstallerComponent : Component
{
    [DataField]
    public string Program = "WFGameHubCartridge";
}
