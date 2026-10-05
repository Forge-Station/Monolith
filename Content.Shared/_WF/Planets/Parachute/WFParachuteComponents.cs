using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Planets.Parachute;

/// <summary>A packed parachute: strapped onto a crate, an object or a person, it opens on its own when they fall.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFParachuteComponent : Component
{
    /// <summary>How long strapping it on takes.</summary>
    [DataField]
    public TimeSpan AttachTime = TimeSpan.FromSeconds(3);

    [DataField]
    public SoundSpecifier AttachSound = new SoundPathSpecifier("/Audio/Items/jumpsuit_equip.ogg");
}

/// <summary>Something wearing a parachute. Removed on landing, which hands the pack back.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFParachutedComponent : Component
{
    /// <summary>True from the moment the canopy opens until touchdown.</summary>
    [DataField, AutoNetworkedField]
    public bool Deployed;

    /// <summary>Fall speed the canopy holds, in levels per second; under the speed at which a landing hurts.</summary>
    [DataField, AutoNetworkedField]
    public float FallSpeed = 1.5f;

    /// <summary>What is left at the landing site.</summary>
    [DataField]
    public EntProtoId Pack = "WFParachute";

    [DataField]
    public SoundSpecifier DeploySound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");
}

/// <summary>Do-after for strapping a parachute on.</summary>
[Serializable, NetSerializable]
public sealed partial class WFParachuteAttachDoAfterEvent : SimpleDoAfterEvent;
