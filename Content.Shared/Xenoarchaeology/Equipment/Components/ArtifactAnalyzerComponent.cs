// WOLFGATE(Xenoarch) START: wizden's xenoarch rework
using Content.Shared.DeviceLinking;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Xenoarchaeology.Equipment.Components;

/// <summary>
/// A machine that is combined and linked to the <see cref="AnalysisConsoleComponent"/>
/// in order to analyze artifacts and extract points.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ArtifactAnalyzerComponent : Component
{
    /// <summary>
    /// The current artifact placed on this analyzer.
    /// Can be null if none are present.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? CurrentArtifact;

    /// <summary>
    /// The corresponding console entity.
    /// Can be null if not linked.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Console;

    /// <summary>
    /// The machine linking port for linking the analyzer with the console.
    /// </summary>
    [DataField]
    public ProtoId<SinkPortPrototype> LinkingPort = "ArtifactAnalyzerReceiver";
}
// WOLFGATE END
