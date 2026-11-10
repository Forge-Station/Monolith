using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Weapons.BulletBox;

/// <summary>Sprite layer key of a box's fill.</summary>
[Serializable, NetSerializable]
public enum BulletBoxLayers
{
    Fill,
}

/// <summary>Key of a box's second fill layer.</summary>
[Serializable, NetSerializable]
public enum BulletBoxLayers2
{
    Fill,
}

/// <summary>How full a box looks.</summary>
[Serializable, NetSerializable]
public enum BulletBoxVisuals
{
    Empty = 0,
    Low,
    Medium,
    High,
    Full,
}

