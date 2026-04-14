using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Forge.OreMagnet.Components;

[RegisterComponent, Access(typeof(OreMagnetSystem)), AutoGenerateComponentPause]
public sealed partial class OreMagnetGridCooldownComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUseTime = TimeSpan.Zero;
}
