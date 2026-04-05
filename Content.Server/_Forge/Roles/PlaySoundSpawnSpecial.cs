using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Server.Audio;

/// </summary>
/// Used for company. Plays a sound when a player spawns.
/// </summary>
namespace Content.Server.Jobs
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class PlaySpawnSoundSpecial : JobSpecial
    {
        [DataField("sound")]
        public SoundSpecifier? Sound { get; private set; }

        public override void AfterEquip(EntityUid mob)
        {
            if (Sound == null)
                return;

            var sysMan = IoCManager.Resolve<IEntitySystemManager>();
            var audioSystem = sysMan.GetEntitySystem<AudioSystem>();

            audioSystem.PlayPvs(Sound, mob);
        }
    }
}
