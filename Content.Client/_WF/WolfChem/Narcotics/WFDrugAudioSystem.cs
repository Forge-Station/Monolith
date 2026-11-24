using Content.Goobstation.Client.Audio;
using Content.Shared._WF.WolfChem.Narcotics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.WolfChem.Narcotics;

/// <summary>
/// The world heard through the local player's high: world sounds take the family's reverb and pitch. A light high only
/// shifts the pitch a little, a heavy one adds the reverb, an overdose doubles the shift. Music, interface and other
/// global sounds stay as they are. The reverb takes over from area echo while it lasts, and both come back off when
/// the high ends. While a nod shuts the eyes, world sounds are muffled as if through a wall.
/// </summary>
public sealed class WFDrugAudioSystem : EntitySystem
{
    [Dependency] private AudioEffectSystem _effects = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private WFIntoxicationSystem _intoxication = default!;

    private static readonly Dictionary<WFDrugFamily, (ProtoId<AudioPresetPrototype>? Preset, float Pitch)> Sounds = new()
    {
        [WFDrugFamily.Psychedelic] = ("Psychotic", 1f),
        [WFDrugFamily.Opioid] = ("Underwater", 0.9f),
        [WFDrugFamily.Cannabinoid] = ("Dizzy", 0.96f),
        [WFDrugFamily.Empathogen] = ("Drugged", 1f),
        [WFDrugFamily.Stimulant] = (null, 1.06f),
    };

    /// <summary>Sounds this system has changed, to put back when the high ends.</summary>
    private readonly HashSet<EntityUid> _touched = new();

    private ProtoId<AudioPresetPrototype>? _preset;

    /// <summary>Occlusion at a fully shut nod: most of the treble gone and about half the volume.</summary>
    private const float NodOcclusion = 7f;

    public override void Initialize()
    {
        base.Initialize();
        // The engine resets each sound's occlusion every frame, so the nod's muffling goes on after it.
        UpdatesAfter.Add(typeof(Robust.Client.Audio.AudioSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var (preset, pitch) = Target();
        var sober = preset == null && MathHelper.CloseTo(pitch, 1f);
        if (sober && _touched.Count == 0)
            return;

        var presetChanged = preset != _preset;
        _preset = preset;

        var nod = _intoxication.NodClosure() * NodOcclusion;

        EntityUid? auxiliary = null;
        if (preset is { } wanted && _effects.ResolveCachedEffect(wanted, out var resolved, out _))
            auxiliary = resolved;

        var query = EntityQueryEnumerator<AudioComponent>();
        while (query.MoveNext(out var uid, out var audio))
        {
            if (audio.Global)
                continue;

            if (sober)
            {
                if (_touched.Contains(uid))
                    Restore((uid, audio));
                continue;
            }

            _touched.Add(uid);
            if (nod > 0f)
                audio.Occlusion = MathF.Max(audio.Occlusion, nod);

            var shifted = audio.Params.Pitch * pitch;
            if (!MathHelper.CloseTo(audio.Pitch, shifted))
                SetPitch(audio, shifted);

            if (preset is { } apply && auxiliary != null)
            {
                if (audio.Auxiliary != auxiliary)
                    _effects.TryAddEffect((uid, audio), apply);
            }
            else if (presetChanged)
            {
                _effects.TryRemoveEffect((uid, audio));
            }
        }

        if (sober)
            _touched.Clear();
    }

    /// <summary>The reverb and pitch factor for the local player's strongest high; no reverb and 1 when sober.</summary>
    private (ProtoId<AudioPresetPrototype>? Preset, float Pitch) Target()
    {
        if (_player.LocalEntity is not { } local
            || !TryComp<WFIntoxicationComponent>(local, out var comp)
            || _intoxication.DominantHigh(comp) is not { } high
            || !Sounds.TryGetValue(high.Family, out var sound))
            return (null, 1f);

        return high.Phase switch
        {
            WFDrugPhase.Light => (null, 1f + (sound.Pitch - 1f) * 0.5f),
            WFDrugPhase.Overdose => (sound.Preset, 1f + (sound.Pitch - 1f) * 2f),
            _ => (sound.Preset, sound.Pitch),
        };
    }

    private void Restore(Entity<AudioComponent> audio)
    {
        SetPitch(audio.Comp, audio.Comp.Params.Pitch);
        _effects.TryRemoveEffect(audio);
    }

    private static void SetPitch(AudioComponent audio, float pitch)
    {
        // The engine has no pitch setter; this writes the playing source only, and a new network state resets it.
#pragma warning disable RA0002
        audio.Pitch = pitch;
#pragma warning restore RA0002
    }
}
