using Robust.Shared.GameStates;
using Robust.Shared.Audio;

namespace Content.Shared.Bed.Sleep;

/// <summary>
/// This is used for the snoring trait.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SnoringComponent : Component
{
/// Forge-Change-Start
	/// <summary>
	/// Cached snore sound from the entity's vocal emote sounds. If null, use sleep default.
	/// </summary>
	[DataField]
	public SoundSpecifier? Snore;
/// Forge-Change-End
}
