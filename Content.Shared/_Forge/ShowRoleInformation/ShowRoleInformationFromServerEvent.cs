using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.ShowRoleInformation;

[Serializable, NetSerializable]
public sealed class ShowRoleInformationFromServerEvent : EntityEventArgs
{
    public ProtoId<ShowRoleInformationWindowData> Window;
    public float Duration;
}
