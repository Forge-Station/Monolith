using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._WF.Remade.Signals;

/// <summary>A dead player held by a Monolith: an unseen eye that spends Psi on tricks and can take over a Remade.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFSignalComponent : Component
{
    /// <summary>The Monolith it answers to. Lose it and the Signal is let go.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Stone;

    [DataField, AutoNetworkedField]
    public float Psi = WFSignalMath.StartPsi;

    /// <summary>The stone's stage, for the screen.</summary>
    [ViewVariables, AutoNetworkedField]
    public WFMonolithStage Stage;

    /// <summary>The Remade it is driving, if any.</summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Possessing;

    /// <summary>When each trick can be played again.</summary>
    [ViewVariables]
    public Dictionary<WFSignalTrick, TimeSpan> Ready = new();

    [ViewVariables]
    public TimeSpan PossessReady;

    /// <summary>The last place it was allowed to be, to pull it back to.</summary>
    [ViewVariables]
    public MapCoordinates? LastGood;

    [ViewVariables]
    public TimeSpan NextPullMessage;

    /// <summary>Tricks played, Remade taken and pulls back. Tests read them.</summary>
    [ViewVariables]
    public int Tricks;

    [ViewVariables]
    public int Possessions;

    [ViewVariables]
    public int Pulls;
}
