using Robust.Shared.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Rotatable
{
    [RegisterComponent]
    public sealed partial class FlippableComponent : Component
    {
        /// <summary>
        ///     Entity to replace this entity with when the current one is 'flipped'.
        /// </summary>
        [DataField("mirrorEntity", required: true, customTypeSerializer: typeof(ProtoId<EntityPrototype>))]
        public string MirrorEntity = default!;
    }
}
