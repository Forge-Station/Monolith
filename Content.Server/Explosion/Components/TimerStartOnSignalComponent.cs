using Content.Shared.DeviceLinking;
using Robust.Shared.Prototypes;

namespace Content.Server.Explosion.Components
{
    /// <summary>
    /// Sends a trigger when signal is received.
    /// </summary>
    [RegisterComponent]
    public sealed partial class TimerStartOnSignalComponent : Component
    {
        [DataField("port", customTypeSerializer: typeof(ProtoId<SinkPortPrototype>))]
        public string Port = "Timer";
    }
}
