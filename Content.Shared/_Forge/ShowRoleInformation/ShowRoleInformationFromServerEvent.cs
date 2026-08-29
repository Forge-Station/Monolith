using Robust.Shared.Serialization;

namespace Content.Shared._Forge.ShowRoleInformation;

[Serializable, NetSerializable]
public sealed class ShowRoleInformationFromServerEvent : EntityEventArgs
{
    public string Description { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public float Duration { get; set; } = 15f;
}
