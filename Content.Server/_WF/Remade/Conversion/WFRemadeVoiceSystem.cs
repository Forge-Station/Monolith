using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Conversion;

/// <summary>Plays the Remade's voices.</summary>
public sealed partial class WFRemadeVoiceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedWFRemadeLimbsSystem _limbs = default!;

    private static readonly TimeSpan PainCooldown = TimeSpan.FromSeconds(3);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeVoiceComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFRemadeVoiceComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<WFRemadeVoiceComponent, DamageChangedEvent>(OnDamaged);
    }

    private void OnMapInit(Entity<WFRemadeVoiceComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextSpeech = _timing.CurTime + NextSpeechDelay();
    }

    private void OnMobState(Entity<WFRemadeVoiceComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            _audio.PlayPvs(ent.Comp.Death, ent);
    }

    private void OnDamaged(Entity<WFRemadeVoiceComponent> ent, ref DamageChangedEvent args)
    {
        var now = _timing.CurTime;
        if (!args.DamageIncreased || now < ent.Comp.NextPain || _mobState.IsDead(ent))
            return;

        ent.Comp.NextPain = now + PainCooldown;
        _audio.PlayPvs(ent.Comp.Pain, ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFRemadeVoiceComponent>();
        while (query.MoveNext(out var uid, out var voice))
        {
            if (now < voice.NextSpeech)
                continue;

            voice.NextSpeech = now + NextSpeechDelay();
            if (!_mobState.IsDead(uid) && _limbs.Can(uid, WFRemadeLimbStat.Voice))
                _audio.PlayPvs(voice.Speech, uid, AudioParams.Default.WithVolume(-4f).WithMaxDistance(12f));
        }
    }

    /// <summary>Its long cry, heard well beyond where it stands.</summary>
    public void Shout(Entity<WFRemadeVoiceComponent> ent)
    {
        if (_limbs.Can(ent, WFRemadeLimbStat.Voice))
            _audio.PlayPvs(ent.Comp.Shout, ent);
    }

    private TimeSpan NextSpeechDelay()
    {
        return TimeSpan.FromSeconds(_random.NextFloat(25f, 60f));
    }
}
