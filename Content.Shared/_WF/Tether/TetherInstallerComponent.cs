using Content.Shared.DoAfter;
using Content.Shared.Materials;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Tether;

/// <summary>
/// A hand tool that spends stored steel to bolt a <see cref="RopeAttachPointComponent"/> eye to a
/// tile at range. Steel is inserted by hand via <see cref="Content.Shared.Materials.MaterialStorageComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TetherInstallerComponent : Component
{
    /// <summary>Entity spawned on a successful install.</summary>
    [DataField]
    public EntProtoId AnchorEyePrototype = "TetherAnchorEye";

    /// <summary>Material consumed per install, in the same units as <see cref="Content.Shared.Materials.MaterialStorageComponent.Storage"/> (a sheet is 100).</summary>
    [DataField, AutoNetworkedField]
    public int MaterialPerInstall = 200;

    [DataField, AutoNetworkedField]
    public ProtoId<MaterialPrototype> Material = "Steel";

    /// <summary>Longest reach, in metres, from the user to the targeted tile.</summary>
    [DataField, AutoNetworkedField]
    public float Range = 3f;

    [DataField, AutoNetworkedField]
    public TimeSpan InstallDelay = TimeSpan.FromSeconds(1.5);

    [DataField]
    public SoundSpecifier StartSound = new SoundPathSpecifier("/Audio/Weapons/Guns/MagIn/kinetic_reload.ogg");

    [DataField]
    public SoundSpecifier InstallSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/harpoon.ogg");
}

/// <summary>Tile-targeted doafter for installing an anchor eye.</summary>
[Serializable, NetSerializable]
public sealed partial class TetherInstallDoAfterEvent : DoAfterEvent
{
    [DataField(required: true)]
    public NetCoordinates Coordinates;

    public TetherInstallDoAfterEvent()
    {
    }

    public TetherInstallDoAfterEvent(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }

    public override DoAfterEvent Clone() => new TetherInstallDoAfterEvent(Coordinates);
}
