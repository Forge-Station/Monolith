using Robust.Client.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Map;

namespace Content.Client._WF.Audio;

/// <summary>
/// Mutes positional sounds left in nullspace, such as a loop on something that has left view. The lobby's listener
/// is in nullspace too, so the engine would otherwise play them there at full volume.
/// </summary>
public sealed class WFDetachedAudioSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        // The audio system sets each source's gain; this has to overwrite that.
        UpdatesAfter.Add(typeof(AudioSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        var query = AllEntityQuery<AudioComponent, TransformComponent>();
        while (query.MoveNext(out var audio, out var xform))
        {
            if (Muted(audio, xform) && audio.Gain > 0f)
                audio.Gain = 0f;
        }
    }

    /// <summary>Whether a sound would be muted: positional, with no map to be heard on.</summary>
    public static bool Muted(AudioComponent audio, TransformComponent xform) => !audio.Global && xform.MapID == MapId.Nullspace;
}
