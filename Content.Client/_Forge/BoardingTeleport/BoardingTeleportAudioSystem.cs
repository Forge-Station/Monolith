using Content.Client.Audio;
using Content.Shared._Forge;
using Content.Shared._Forge.BoardingTeleport.Components;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;

namespace Content.Client._Forge.BoardingTeleport;

public sealed class BoardingTeleportAudioSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoardingTeleportAudioComponent, ComponentStartup>(OnStartup);
        _cfg.OnValueChanged(ForgeVars.BoardingTeleportVolume, OnVolumeChanged);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _cfg.UnsubValueChanged(ForgeVars.BoardingTeleportVolume, OnVolumeChanged);
    }

    private void OnStartup(Entity<BoardingTeleportAudioComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<AudioComponent>(ent, out var audio))
            return;

        ent.Comp.BaseVolume = audio.Params.Volume;
        ApplyVolume(ent, ent.Comp, audio);
    }

    private void OnVolumeChanged(float _)
    {
        var query = EntityQueryEnumerator<BoardingTeleportAudioComponent, AudioComponent>();
        while (query.MoveNext(out var uid, out var marker, out var audio))
        {
            ApplyVolume(uid, marker, audio);
        }
    }

    private void ApplyVolume(EntityUid uid, BoardingTeleportAudioComponent marker, AudioComponent audio)
    {
        var volume = marker.BaseVolume + GetUserVolumeOffsetDb();
        _audio.SetVolume(uid, volume, audio);
    }

    private float GetUserVolumeOffsetDb()
    {
        var userGain = _cfg.GetCVar(ForgeVars.BoardingTeleportVolume) * ContentAudioSystem.BoardingTeleportMultiplier;
        var referenceGain = ContentAudioSystem.BoardingTeleportMultiplier;
        return SharedAudioSystem.GainToVolume(userGain) - SharedAudioSystem.GainToVolume(referenceGain);
    }
}
