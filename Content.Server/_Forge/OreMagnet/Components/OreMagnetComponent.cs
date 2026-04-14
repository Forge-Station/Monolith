using Content.Shared.Construction.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server._Forge.OreMagnet.Components;

[RegisterComponent, Access(typeof(OreMagnetSystem)), AutoGenerateComponentPause]
public sealed partial class OreMagnetComponent : Component
{
    [DataField]
    public float Range = 100f;

    [DataField]
    public float MaxRange = 100f;

    [DataField]
    public TimeSpan BaseCooldown = TimeSpan.FromSeconds(90);

    [DataField]
    public TimeSpan MinCooldown = TimeSpan.FromSeconds(30);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan BoundUntil = TimeSpan.Zero;

    [DataField]
    public EntityUid? BoundGridUid;

    [DataField]
    public EntityUid? LinkedContainerUid;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    [DataField("machinePartCooldown", customTypeSerializer: typeof(PrototypeIdSerializer<MachinePartPrototype>))]
    public string MachinePartCooldown = "Capacitor";

    [DataField]
    public float PartRatingCooldownMultiplier = 0.75f;

    [DataField]
    public SoundSpecifier ActivateSound = new SoundPathSpecifier("/Audio/Effects/teleport_arrival.ogg");

    [DataField]
    public int BatchSize = 10;

    [DataField]
    public int MaxPendingOres = 0;

    [DataField]
    public TimeSpan BatchDelay = TimeSpan.FromSeconds(5);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextBatchTime = TimeSpan.Zero;

    [DataField]
    public bool Processing;

    [DataField]
    public List<EntityUid> PendingOres = new();
}
