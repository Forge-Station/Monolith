using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.ClawSharpness;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoClawsSystem))]
public sealed partial class AirlockReceiverXenoClawsComponent : Component
{
    [DataField, AutoNetworkedField]
    public float MaxHealth = 500;

    [DataField, AutoNetworkedField]
    public int HitsToDestroyBolted = 10;

    [DataField, AutoNetworkedField]
    public int HitsToDestroyWelded = 15;

    /// <summary>Hits for very sharp claws (queen, crusher, ravager), bolted or not; Wolfgate addition.</summary>
    [DataField, AutoNetworkedField]
    public int HitsToDestroyVerySharp = 8;

    [DataField, AutoNetworkedField]
    public XenoClawType MinimumClawStrength = XenoClawType.Sharp;
}
