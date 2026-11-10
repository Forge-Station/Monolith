using System.Linq;
using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Torment;
using Content.Server._WF.Remade.Gifts;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Chat.Managers;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Dread;
using Content.Shared.Chat;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.Remade.Dread;

/// <summary>What the stone has done to one player's head, and when it may do more.</summary>
[RegisterComponent]
public sealed partial class WFDreadSubjectComponent : Component
{
    [ViewVariables]
    public TimeSpan NextWhisper;

    [ViewVariables]
    public TimeSpan NextGlimpse;

    [ViewVariables]
    public TimeSpan NextPulse;

    [ViewVariables]
    public TimeSpan NextMurmur;

    /// <summary>The whisper bed last sent, from 0 to 1.</summary>
    [ViewVariables]
    public float Bed;

    /// <summary>The warp of sight last sent, and when it must be sent again to hold.</summary>
    [ViewVariables]
    public float Level;

    [ViewVariables]
    public TimeSpan LevelRenew;

    /// <summary>How many of each have been sent. Tests read these.</summary>
    [ViewVariables]
    public int Whispers;

    [ViewVariables]
    public int Glimpses;

    [ViewVariables]
    public int Pulses;

    [ViewVariables]
    public int Murmurs;
}

/// <summary>
/// The stone in the head. Every player under a Monolith's influence gets whispers, glimpses of the Remade at the edge of
/// sight and a warp of the screen, all more often with the stone's stage and the player's own stress. The crew near the stone
/// get the most. At Rapture, lightly, and at the Crescendo, more so, everyone in the sector gets them too, on any map. A shard of
/// a Scion does the same in a small way to whoever carries it or stands near it, weaker than the stone and by the Scion's stage.
/// From Harvest a low whisper bed also plays in the player's head, and at Rapture and the Crescendo it never leaves.
/// </summary>
public sealed partial class WFDreadSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;
    [Dependency] private WFDriftMindSystem _drift = default!;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);

    /// <summary>Drift a second the stone puts in a head at full dread.</summary>
    private const float DreadDrift = 1.5f;
    private static readonly TimeSpan WhisperGap = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MurmurGap = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GlimpseGap = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PulseGap = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan LevelHold = TimeSpan.FromSeconds(6);
    private static readonly Color WhisperColor = Color.FromHex("#9b2a2a");

    /// <summary>How many lines each stage adds to the whispers: the Hymn also has the older ones in the Director's list.</summary>
    private const int WhispersPerStage = 12;
    private const int LegacyWhispers = 8;

    private const string VoiceSounds = "WFDreadVoices";
    private const string MenaceSounds = "WFDreadMenace";
    private const string WhisperSounds = "WFDreadWhispers";

    private readonly record struct Source(EntityUid Uid, WFMonolithStage Stage, MapCoordinates At, bool Shard = false,
        bool Rooted = false, EntityUid? Carrier = null, EntityUid? LocalGrid = null);

    private readonly List<Source> _sources = new();
    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + TickRate;
        Sweep(now);
    }

    private void Sweep(TimeSpan now)
    {
        _sources.Clear();
        var monoliths = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (monoliths.MoveNext(out var uid, out var monolith, out var xform))
        {
            if (monolith.Stage != WFMonolithStage.Quiet && xform.MapID != MapId.Nullspace)
                _sources.Add(new Source(uid, monolith.Stage, _transform.GetMapCoordinates(uid, xform), LocalGrid: monolith.Local ? xform.GridUid ?? uid : null));
        }

        // A shard stands in for its Scion at a short reach, and goes quiet near a main station like everything else.
        var shards = EntityQueryEnumerator<WFMonolithShardComponent, TransformComponent>();
        while (shards.MoveNext(out var uid, out var shard, out var xform))
        {
            if (shard.Parent is not { } parent || TerminatingOrDeleted(parent) || !TryComp<WFMonolithComponent>(parent, out var parentStone)
                || parentStone.Stage == WFMonolithStage.Quiet || xform.MapID == MapId.Nullspace || _corruption.IsSanctuaryAt(uid))
            {
                continue;
            }

            _sources.Add(new Source(uid, parentStone.Stage, _transform.GetMapCoordinates(uid, xform), true, shard.Deployed, CarrierOf(uid)));
        }

        var sectorWide = _config.GetCVar(WFDreadCVars.Sector);
        var rate = _config.GetCVar(WFDreadCVars.Rate);
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out _, out var xform))
        {
            if (HasComp<WFRemadeComponent>(uid) || _mobState.IsDead(uid) || xform.MapID == MapId.Nullspace)
                continue;

            var here = _transform.GetMapCoordinates(uid, xform);
            var stress = CompOrNull<WFRemadeStressComponent>(uid)?.Stress ?? 0f;
            var influence = 0f;
            var heard = 0f;
            var stage = WFMonolithStage.Quiet;
            var distance = 0f;
            var sameMap = false;
            var shardInfluence = 0f;
            var shardStage = WFMonolithStage.Quiet;
            foreach (var source in _sources)
            {
                var same = source.At.MapId == here.MapId;
                var away = same ? (source.At.Position - here.Position).Length() : 0f;
                if (source.Shard)
                {
                    var near = same ? WFDreadMath.ShardInfluence(source.Stage, away, source.Carrier == uid, source.Rooted) : 0f;
                    if (near > shardInfluence || near == shardInfluence && near > 0f && source.Stage > shardStage)
                    {
                        shardInfluence = near;
                        shardStage = source.Stage;
                    }

                    continue;
                }

                if (source.LocalGrid is { } localGrid && xform.GridUid != localGrid)
                    continue;

                var share = WFDreadMath.Influence(source.Stage, away, same, sectorWide && source.LocalGrid == null);
                heard = Math.Max(heard, WFDreadMath.Influence(source.Stage, away, same, false));
                if (share > influence || share == influence && source.Stage > stage)
                {
                    influence = share;
                    stage = source.Stage;
                    distance = away;
                    sameMap = same;
                }
            }

            // An admin's torment: the whole Crescendo, wherever they are.
            if (HasComp<WFTormentedComponent>(uid))
            {
                influence = 1f;
                heard = 1f;
                stage = WFMonolithStage.Crescendo;
                distance = 0f;
                sameMap = true;
                shardInfluence = 0f;
                stress = Math.Max(stress, WFTormentedComponent.Stress);
            }

            var subject = EnsureComp<WFDreadSubjectComponent>(uid);
            if (influence <= 0f && shardInfluence <= 0f)
            {
                if (subject.Level > 0f || subject.Bed > 0f)
                    SendLevel(actor.PlayerSession, subject, 0f, 0f, 0f, now);

                continue;
            }

            // Whichever reaches the player harder sets what they get; the warp takes the greater of the two.
            var fromShard = shardInfluence > influence;
            var level = Math.Max(WFDreadMath.Level(stage, distance, sameMap, stress, sectorWide),
                WFDreadMath.ShardLevel(shardStage, shardInfluence, stress));
            if (fromShard)
            {
                influence = shardInfluence;
                stage = shardStage;
            }

            if (fromShard)
                heard = influence;

            // The stone's hold on a head is Drift, so it shows on the gauge and builds the Unmooring's hallucinations too.
            _drift.Add(uid, level * DreadDrift * (float) TickRate.TotalSeconds, WFDriftFlavour.Monolith, Math.Min(1f, level * 1.5f) * 100f);

            Roll(uid, subject, stage, influence * WFDreadMath.StressFactor(stress) * rate, heard * WFDreadMath.StressFactor(stress) * rate, now);

            var bed = WFDreadMath.Bed(stage, influence, stress, !fromShard);
            if (MathF.Abs(level - subject.Level) > 0.03f || MathF.Abs(bed - subject.Bed) > 0.03f
                || (level > 0f || bed > 0f) && now >= subject.LevelRenew)
            {
                SendLevel(actor.PlayerSession, subject, level, 0f, bed, now);
            }
        }
    }

    /// <summary>
    /// Gives a player a chance of each thing the stone can do in a second. Whispers and murmurs take <paramref name="heard"/>, the
    /// reach without the sector share, so far from the stone the dread is the score and the bed rather than voices.
    /// </summary>
    private void Roll(EntityUid uid, WFDreadSubjectComponent subject, WFMonolithStage stage, float factor, float heard, TimeSpan now)
    {
        if (now >= subject.NextWhisper && _random.Prob(WFDreadMath.Chance(WFDreadMath.WhispersPerMinute(stage) * heard, 1f)))
        {
            subject.NextWhisper = now + WhisperGap;
            Whisper(uid, stage);
        }

        if (now >= subject.NextMurmur && _random.Prob(WFDreadMath.Chance(WFDreadMath.MurmursPerMinute(stage) * heard, 1f)))
        {
            subject.NextMurmur = now + MurmurGap;
            Murmur(uid, stage);
        }

        if (now >= subject.NextGlimpse && _random.Prob(WFDreadMath.Chance(WFDreadMath.GlimpsesPerMinute(stage) * factor, 1f)))
        {
            subject.NextGlimpse = now + GlimpseGap;
            Glimpse(uid, stage);
        }

        if (now >= subject.NextPulse && _random.Prob(WFDreadMath.Chance(WFDreadMath.PulsesPerMinute(stage) * factor, 1f)))
        {
            subject.NextPulse = now + PulseGap;
            Pulse(uid, 0.4f + 0.12f * (int) stage);
        }
    }

    private void SendLevel(ICommonSession session, WFDreadSubjectComponent subject, float level, float spike, float bed, TimeSpan now)
    {
        subject.Level = level;
        subject.Bed = bed;
        subject.LevelRenew = now + LevelHold / 2;
        RaiseNetworkEvent(new WFDreadVisionEvent(level, spike, (float) LevelHold.TotalSeconds, bed), session);
    }

    /// <summary>The mob carrying a thing, however deep in bags or hands, if it is a player.</summary>
    private EntityUid? CarrierOf(EntityUid thing)
    {
        var current = thing;
        for (var depth = 0; depth < 6 && _container.TryGetContainingContainer((current, null, null), out var container); depth++)
        {
            current = container.Owner;
            if (HasComp<ActorComponent>(current))
                return current;
        }

        return null;
    }

    /// <summary>A throb of the vignette and a drain of colour on top of whatever warp the player has.</summary>
    public void Pulse(EntityUid target, float spike)
    {
        if (!TryComp<ActorComponent>(target, out var actor))
            return;

        var subject = EnsureComp<WFDreadSubjectComponent>(target);
        subject.Pulses++;
        RaiseNetworkEvent(new WFDreadVisionEvent(subject.Level, Math.Clamp(spike, 0f, 1f), (float) LevelHold.TotalSeconds, subject.Bed), actor.PlayerSession);
    }

    /// <summary>Shows a player a Remade at the edge of their sight for a moment. The client finds the dark spot.</summary>
    public void Glimpse(EntityUid target, WFMonolithStage stage)
    {
        if (!TryComp<ActorComponent>(target, out var actor))
            return;

        var options = WFDreadGlimpses.Available(stage).ToList();
        if (options.Count == 0)
            return;

        EnsureComp<WFDreadSubjectComponent>(target).Glimpses++;
        RaiseNetworkEvent(new WFDreadGlimpseEvent(_random.Pick(options), _random.NextFloat(0.4f, 0.8f)), actor.PlayerSession);
    }

    /// <summary>A line in the player's head, with a voice behind them about half the time. Louder things join it with the stage.</summary>
    public void Whisper(EntityUid target, WFMonolithStage stage)
    {
        if (!TryComp<ActorComponent>(target, out var actor))
            return;

        EnsureComp<WFDreadSubjectComponent>(target).Whispers++;
        var text = Loc.GetString(WhisperKey(stage), ("name", Name(target)));
        _popup.PopupEntity(text, target, target, stage >= WFMonolithStage.Rapture ? PopupType.LargeCaution : PopupType.MediumCaution);
        _chat.ChatMessageToOne(ChatChannel.Server, text, $"[italic]{FormattedMessage.EscapeText(text)}[/italic]", default, false,
            actor.PlayerSession.Channel, WhisperColor);

        if (_random.Prob(0.85f))
        {
            var offset = _random.NextAngle().ToVec() * _random.NextFloat(2.5f, 7f);
            _audio.PlayStatic(WhisperSound(stage), actor.PlayerSession, Transform(target).Coordinates.Offset(offset), AudioParams.Default.WithVolume(-3f));
        }
    }

    /// <summary>Whispering with no words to read: close, quiet and layered more thickly as the stage climbs. Several can overlap.</summary>
    public void Murmur(EntityUid target, WFMonolithStage stage)
    {
        if (!TryComp<ActorComponent>(target, out var actor))
            return;

        EnsureComp<WFDreadSubjectComponent>(target).Murmurs++;
        var layers = stage >= WFMonolithStage.Crescendo ? 3 : stage >= WFMonolithStage.Rapture ? 2 : 1;
        for (var layer = 0; layer < layers; layer++)
        {
            var offset = _random.NextAngle().ToVec() * _random.NextFloat(0.8f, 4.5f);
            _audio.PlayStatic(MurmurSound(), actor.PlayerSession, Transform(target).Coordinates.Offset(offset),
                AudioParams.Default.WithVolume(_random.NextFloat(-9f, -5f)).WithVariation(0.06f));
        }
    }

    /// <summary>A whisper from the pool of cuts, for a place that wants one without the stage's rarer sounds.</summary>
    public SoundSpecifier MurmurSound() => new SoundCollectionSpecifier(WhisperSounds);

    /// <summary>The Fluent id of a whisper fit for a stage: mostly its own tier, now and then an older, quieter one.</summary>
    public string WhisperKey(WFMonolithStage stage)
    {
        var tier = stage;
        if (tier == WFMonolithStage.Quiet)
            tier = WFMonolithStage.Hymn;

        if (tier > WFMonolithStage.Hymn && !_random.Prob(0.6f))
            tier = (WFMonolithStage) _random.Next((int) WFMonolithStage.Hymn, (int) tier);

        return WhisperKeyAt(tier, _random.Next(tier == WFMonolithStage.Hymn ? WhispersPerStage + LegacyWhispers : WhispersPerStage));
    }

    /// <summary>The id of the n-th whisper of a tier. At the Hymn the first lines are the Director's older ones.</summary>
    public static string WhisperKeyAt(WFMonolithStage tier, int n)
    {
        if (tier == WFMonolithStage.Hymn && n < LegacyWhispers)
            return $"wf-monolith-whisper-{n + 1}";

        if (tier == WFMonolithStage.Hymn)
            n -= LegacyWhispers;

        return $"wf-dread-whisper-{tier.ToString().ToLowerInvariant()}-{n + 1}";
    }

    /// <summary>Every whisper id there is, for a check that each has its words.</summary>
    public static IEnumerable<string> AllWhisperKeys()
    {
        for (var n = 0; n < WhispersPerStage + LegacyWhispers; n++)
        {
            yield return WhisperKeyAt(WFMonolithStage.Hymn, n);
        }

        for (var tier = WFMonolithStage.Growth; tier <= WFMonolithStage.Crescendo; tier++)
        {
            for (var n = 0; n < WhispersPerStage; n++)
            {
                yield return WhisperKeyAt(tier, n);
            }
        }
    }

    /// <summary>
    /// A sound for a whisper: nearly always whispering, with the spoken voices and the Remade's own noises mixed in more thinly
    /// as the stage climbs.
    /// </summary>
    private SoundSpecifier WhisperSound(WFMonolithStage stage)
    {
        var (voices, menace) = stage switch
        {
            WFMonolithStage.Growth => (0.12f, 0f),
            WFMonolithStage.Harvest => (0.15f, 0.15f),
            WFMonolithStage.Rapture => (0.15f, 0.25f),
            WFMonolithStage.Crescendo => (0.15f, 0.3f),
            _ => (0f, 0f),
        };

        var roll = _random.NextFloat();
        if (roll < menace)
            return new SoundCollectionSpecifier(MenaceSounds);

        return roll < menace + voices ? new SoundCollectionSpecifier(VoiceSounds) : MurmurSound();
    }
}
