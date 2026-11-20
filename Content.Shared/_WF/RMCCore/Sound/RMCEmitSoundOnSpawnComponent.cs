using Robust.Shared.GameStates;
using Content.Shared.Sound.Components;

namespace Content.Shared._WF.RMCCore.Sound;

/// <summary>
/// RMC Version of the EmitSoundComponent, despawns instantly
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCEmitSoundOnSpawnComponent : BaseEmitSoundComponent
{
    [DataField]
    public EntityUid? Entity;
}
