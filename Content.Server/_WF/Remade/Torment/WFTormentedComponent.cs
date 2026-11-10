namespace Content.Server._WF.Remade.Torment;

/// <summary>
/// An admin's torment: this player feels a Crescendo stone right beside them wherever they are, with every whisper, glimpse,
/// voice, false sense, the bed and the score. Nobody else hears or sees any of it.
/// </summary>
[RegisterComponent]
public sealed partial class WFTormentedComponent : Component
{
    /// <summary>The least stress the torment treats them as having, so the mind's tricks play too.</summary>
    public const float Stress = 60f;
}
