using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Dread;

/// <summary>An alarm that is still to play on an entity: how many times more, and when.</summary>
[RegisterComponent]
public sealed partial class WFDreadAlarmComponent : Component
{
    [ViewVariables]
    public ResolvedSoundSpecifier Sound = default!;

    [ViewVariables]
    public TimeSpan Length;

    [ViewVariables]
    public TimeSpan Next;

    /// <summary>Plays still to come, not counting the one already playing.</summary>
    [ViewVariables]
    public int Left;

    /// <summary>Plays for everyone rather than for whoever is near the entity.</summary>
    [ViewVariables]
    public bool Global;
}

/// <summary>
/// Plays the biohazard alarm a few times back to back. Each play starts when the last one has finished, and the rest are
/// dropped with the entity they belong to.
/// </summary>
public sealed partial class WFDreadAlarmSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    /// <summary>How many times the alarm sounds.</summary>
    public const int Plays = 3;

    /// <summary>Plays the alarm now and again at the end of each play, <paramref name="times"/> times in all.</summary>
    public void Start(EntityUid host, SoundSpecifier sound, int times = Plays, bool global = false)
    {
        if (TerminatingOrDeleted(host) || times < 1)
            return;

        var alarm = EnsureComp<WFDreadAlarmComponent>(host);
        alarm.Sound = _audio.ResolveSound(sound);
        alarm.Length = _audio.GetAudioLength(alarm.Sound);
        alarm.Global = global;
        alarm.Left = times - 1;
        Play(host, alarm);
    }

    private void Play(EntityUid host, WFDreadAlarmComponent alarm)
    {
        if (alarm.Global)
            _audio.PlayGlobal(alarm.Sound, Filter.Broadcast(), true);
        else
            _audio.PlayPvs(alarm.Sound, host);

        alarm.Next = _timing.CurTime + alarm.Length;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFDreadAlarmComponent>();
        while (query.MoveNext(out var uid, out var alarm))
        {
            if (now < alarm.Next)
                continue;

            if (alarm.Left <= 0)
            {
                RemCompDeferred<WFDreadAlarmComponent>(uid);
                continue;
            }

            alarm.Left--;
            Play(uid, alarm);
        }
    }
}
