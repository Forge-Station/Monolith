using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.Botany.Components;

/// <summary>
///    After scanning, retrieves the target Uid to use with its related UI.
/// </summary>
[RegisterComponent]
public sealed partial class PlantAnalyzerComponent : Component
{
    [DataDefinition]
    public partial struct PlantAnalyzerSetting
    {
        [DataField]
        public float ScanDelay = 0.8f;

        /// <summary>
        /// Whether extended seed gene information should be sent to the client.
        /// </summary>
        [DataField]
        public bool ExposeAdvancedData = true;
    }

    [DataField, ViewVariables]
    public PlantAnalyzerSetting Settings = new();

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public DoAfterId? DoAfter;

    [DataField]
    public SoundSpecifier? ScanningEndSound;

    [DataField]
    public EntityUid? ScannedEntity;

    /// <summary>
    /// The user who scanned the plant. Used to keep the UI open while the analyzer stays on that person.
    /// </summary>
    [DataField]
    public EntityUid? User;

    [DataField]
    public float MaxScanRange = 2.5f;
}
