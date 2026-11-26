// WOLFGATE(Xenoarch) START: wizden's xenoarch rework
namespace Content.Server.Xenoarchaeology.Artifact.XAE.Components;

/// <summary>
/// When activated, will teleport the artifact
/// to a random position within a certain radius
/// </summary>
[RegisterComponent, Access(typeof(XAERandomTeleportInvokerSystem))]
public sealed partial class XAERandomTeleportInvokerComponent : Component
{
    /// <summary>
    /// The max distance that the artifact will teleport.
    /// </summary>
    [DataField]
    public float MaxRange = 30f; // Frontier: 15<30

    /// <summary>
    /// The min distance that the artifact will teleport.
    /// </summary>
    [DataField]
    public float MinRange = 6f;
}
// WOLFGATE END
