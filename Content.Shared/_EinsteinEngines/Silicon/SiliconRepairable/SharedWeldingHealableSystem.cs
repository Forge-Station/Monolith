using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._EinsteinEngines.Silicon.WeldingHealing;

public abstract partial class SharedWeldingHealableSystem : EntitySystem
{
    [Serializable, NetSerializable]
    protected sealed partial class SiliconRepairFinishedEvent : SimpleDoAfterEvent
    {
        public float Delay;
    }

    // WOLFGATE(Wolfmed) START: the do-after event for welding a chassis wound.
    [Serializable, NetSerializable]
    protected sealed partial class WoundRepairFinishedEvent : SimpleDoAfterEvent
    {
        public NetEntity Part;
        public float Delay;

        public override DoAfterEvent Clone() => new WoundRepairFinishedEvent { Part = Part, Delay = Delay };
    }
    // WOLFGATE END
}
