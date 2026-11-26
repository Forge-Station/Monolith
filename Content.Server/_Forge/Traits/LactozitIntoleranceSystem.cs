using Content.Server.Chat.Systems;
using Content.Server.Medical;
using Content.Server.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Player;

namespace Content.Server.Traits.Assorted;

/// <summary>
/// This handles Lactozit intolerance incidents when Lactozit is metabolized.
/// </summary>
public sealed partial class LactozitIntoleranceSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private VomitSystem _vomit = default!;

    public void TryTriggerLactozitiumReaction(EntityUid uid, LactozitIntoleranceComponent? intolerance = null)
    {
        if (!Resolve(uid, ref intolerance, false))
            return;

        if (intolerance.TimeUntilNextIncident > 0)
            return;

        intolerance.TimeUntilNextIncident =
            _random.NextFloat(intolerance.TimeBetweenIncidents.X, intolerance.TimeBetweenIncidents.Y);

        BloatingEffect(uid);

    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<LactozitIntoleranceComponent>();
        while (query.MoveNext(out _, out var intolerance))
        {
            if (intolerance.TimeUntilNextIncident <= 0)
                continue;

            intolerance.TimeUntilNextIncident -= frameTime;
        }
    }
    public void BloatingEffect(EntityUid uid){
        switch (_random.Next(3))
        {
            case 0:
            {
                _audio.PlayPvs(new SoundCollectionSpecifier("Parp"), uid, AudioParams.Default.WithVariation(0.125f));
                var fartMessage = Loc.GetString("trait-lactozit-intolerance-fart", ("entity", uid));
                _popup.PopupEntity(fartMessage, uid, Filter.PvsExcept(uid), true);
                _popup.PopupEntity(fartMessage, uid, uid);
                break;
            }
            case 1:
                _chat.TryEmoteWithChat(uid, "Belch", ignoreActionBlocker: true, forceEmote: true);
                break;
            case 2:
                _vomit.Vomit(uid);
                break;
        }
    }
}
