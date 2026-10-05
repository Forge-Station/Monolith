using Content.Shared.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server.Destructible.Thresholds.Behaviors
{
    [Serializable]
    [DataDefinition]
    public sealed partial class PlaySoundBehavior : IThresholdBehavior
    {
        /// <summary>
        ///     Sound played upon destruction.
        /// </summary>
        [DataField("sound", required: true)] public SoundSpecifier Sound { get; set; } = default!;

        public void Execute(EntityUid owner, DestructibleSystem system, EntityUid? cause = null)
        {
            // WOLFGATE(Audio) START: destruction sounds share a budget, so a landing hull can't exhaust the client's sources.
            // A hull grinding out a landing destroys every alarm, light and window aboard in the same tick,
            // and one PlayPvs each is one OpenAL source each on every client that can hear them.
            if (!system.WfDestructionSoundAllowed(owner))
                return;
            // WOLFGATE END

            var pos = system.EntityManager.GetComponent<TransformComponent>(owner).Coordinates;
            system.EntityManager.System<SharedAudioSystem>().PlayPvs(Sound, pos);
        }
    }
}
