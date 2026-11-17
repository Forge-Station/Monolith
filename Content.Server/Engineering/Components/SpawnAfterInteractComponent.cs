using Robust.Shared.Map; // Forge-Change
using Robust.Shared.Prototypes;

namespace Content.Server.Engineering.Components
{
    [RegisterComponent]
    public sealed partial class SpawnAfterInteractComponent : Component
    {
        [DataField("prototype", customTypeSerializer: typeof(ProtoId<EntityPrototype>))]
        public string? Prototype { get; private set; }

        [DataField("ignoreDistance")]
        public bool IgnoreDistance { get; private set; }

        [DataField("doAfter")]
        public float DoAfterTime = 0;

        [DataField("removeOnInteract")]
        public bool RemoveOnInteract = false;
    }

    // Forge-Change-start
    /// <summary>
    /// Raised on the item before it spawns anything. Cancel to keep the item.
    /// </summary>
    public sealed class SpawnAfterInteractAttemptEvent : CancellableEntityEventArgs
    {
        public EntityUid User { get; }
        public EntityCoordinates Coordinates { get; }

        public SpawnAfterInteractAttemptEvent(EntityUid user, EntityCoordinates coordinates)
        {
            User = user;
            Coordinates = coordinates;
        }
    }
    // Forge-Change-end
}
