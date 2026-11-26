using Content.Shared._WF.AsteroidMining;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// Registers the beam overlay, spins the gun's barrel while it fires, and plays its hum, which slides up toward a
/// higher-pitched copy as the rock heats, so a gunner can hear their way into the window.
/// </summary>
public sealed partial class WFMiningBeamVisualizerSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private const string IdleState = "space_artillery";
    private const string FiringState = "firing";

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WFMiningBeamOverlay(EntityManager, _prototypes, _timing));
        SubscribeLocalEvent<WFMiningBeamEmitterComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFMiningBeamEmitterComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WFMiningBeamEmitterComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<WFMiningBeamEmitterComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Audio = _audio.Stop(ent.Comp.Audio);
        ent.Comp.AudioHot = _audio.Stop(ent.Comp.AudioHot);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var query = EntityQueryEnumerator<WFMiningBeamEmitterComponent>();
        while (query.MoveNext(out var uid, out var beam))
        {
            if (!beam.Active || beam.LoopSound == null)
            {
                if (beam.Audio == null && beam.AudioHot == null)
                    continue;

                beam.Audio = _audio.Stop(beam.Audio);
                beam.AudioHot = _audio.Stop(beam.AudioHot);
                continue;
            }

            // The hum and a copy pitched up by HeatPitch play together; heat crossfades from one to the other.
            var loop = beam.LoopSound.Params;
            if (beam.Audio == null || !HasComp<AudioComponent>(beam.Audio))
                beam.Audio = _audio.PlayEntity(beam.LoopSound, Filter.Local(), uid, false, loop)?.Entity;
            if (beam.AudioHot == null || !HasComp<AudioComponent>(beam.AudioHot))
                beam.AudioHot = _audio.PlayEntity(beam.LoopSound, Filter.Local(), uid, false,
                    loop.WithPitchScale(beam.HeatPitch).WithVolume(-60f))?.Entity;

            var heat = Math.Clamp(beam.Charge / WFAsteroidHeat.OverchargeLine, 0f, 1f);
            // Equal-power crossfade, so the hum keeps its loudness through the change.
            _audio.SetVolume(beam.Audio, loop.Volume + Decibels(MathF.Cos(heat * MathF.PI / 2f)));
            _audio.SetVolume(beam.AudioHot, loop.Volume + Decibels(MathF.Sin(heat * MathF.PI / 2f)));
        }
    }

    private static float Decibels(float gain)
    {
        return 20f * MathF.Log10(MathF.Max(gain, 0.001f));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<WFMiningBeamOverlay>();
        base.Shutdown();
    }

    private void OnStartup(Entity<WFMiningBeamEmitterComponent> ent, ref ComponentStartup args)
    {
        UpdateBarrel(ent);
    }

    private void OnState(Entity<WFMiningBeamEmitterComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateBarrel(ent);
    }

    private void UpdateBarrel(Entity<WFMiningBeamEmitterComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite) ||
            !_sprite.LayerMapTryGet((ent, sprite), WFMiningBeamVisualLayers.Base, out var layer, false))
            return;

        var state = ent.Comp.Active ? FiringState : IdleState;
        if (sprite[layer].RsiState.Name != state)
            _sprite.LayerSetRsiState((ent, sprite), layer, state);
    }
}
