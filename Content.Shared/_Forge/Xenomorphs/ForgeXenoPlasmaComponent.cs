using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Content.Shared.Alert;

namespace Content.Shared._Forge.Xenomorphs;

/// <summary>
/// Plasma pool used to pay for caste abilities. Regenerates faster on weeds and while resting.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ForgeXenoPlasmaComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Plasma = 300f;

    [DataField, AutoNetworkedField]
    public float MaxPlasma = 300f;

    /// <summary>Plasma gained per second while standing off weeds.</summary>
    [DataField]
    public float Regen = 2f;

    /// <summary>Plasma gained per second while standing on hive weeds.</summary>
    [DataField]
    public float RegenOnWeeds = 4.5f;

    /// <summary>Multiplier applied to regen while resting.</summary>
    [DataField]
    public float RestMultiplier = 2f;

    [DataField]
    public ProtoId<AlertPrototype> Alert = "ForgeXenoPlasma";
}
