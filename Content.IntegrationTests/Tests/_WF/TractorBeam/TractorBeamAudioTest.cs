using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Server.Power.Components;
using Content.Shared._WF.TractorBeam;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorBeamAudioTest
{
    private const float Step = 1f / 60f;

    [TestCase("engage", 3.635193)]
    [TestCase("loop", 8.188730)]
    [TestCase("disengage", 9.564082)]
    public async Task BeamAssetsLoadAsFullLengthMonoClips(string phase, double seconds)
    {
        await using var pair = await PoolManager.GetServerClient();
        var resources = pair.Client.ResolveDependency<IResourceCache>();
        await pair.Client.WaitAssertion(() =>
        {
            var beam = new TractorBeamEmitterComponent();
            var sound = (SoundPathSpecifier) (phase switch
            {
                "engage" => beam.EngageSound,
                "loop" => beam.LoopSound,
                _ => beam.DisengageSound,
            });
            var clip = resources.GetResource<AudioResource>(sound.Path).AudioStream;
            Assert.That(clip.ChannelCount, Is.EqualTo(1), "Positional audio requires mono samples.");
            Assert.That(clip.Length.TotalSeconds, Is.EqualTo(seconds).Within(0.03),
                "The replacement clips must load fully for correctly timed transitions.");
        });
        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task EngagementAndDisengagementPlayOnceAcrossBothHulls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var maps = pair.Server.ResolveDependency<IMapManager>();
        await pair.Server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var system = entities.System<TractorBeamSystem>();
            system.UpdateBeforeSolve(false, Step);
            foreach (var grid in new[] { source, target })
            {
                var sounds = Sounds(entities, grid, "tractorbeam_engage.ogg");
                Assert.That(sounds, Has.Count.EqualTo(1), "Repeated physics ticks cannot restart engagement.");
                Assert.That(sounds[0].Component.Flags.HasFlag(AudioFlags.GridAudio), Is.True);
                Assert.That(sounds[0].Component.Flags.HasFlag(AudioFlags.NoOcclusion), Is.True);
                var loops = Sounds(entities, grid, "tractorbeam_loop.ogg");
                Assert.That(loops, Has.Count.EqualTo(1), "The first active tick must start the loop alongside engagement.");
                Assert.That(loops[0].Component.Params.Loop, Is.True);
                Assert.That(MathF.Pow(10f, loops[0].Component.Params.Volume / 20f), Is.EqualTo(0.5f).Within(0.0001f),
                    "The sustained loop plays at half its previous gain.");
            }
            system.UpdateBeforeSolve(false, Step);
            foreach (var grid in new[] { source, target })
            {
                Assert.That(Sounds(entities, grid, "tractorbeam_engage.ogg"), Has.Count.EqualTo(1));
                Assert.That(Sounds(entities, grid, "tractorbeam_loop.ogg"), Has.Count.EqualTo(1),
                    "Repeated physics ticks must not restart or duplicate the loop.");
            }
            system.Release(emitter, beam);
            system.Release(emitter, beam);
            foreach (var grid in new[] { source, target })
            {
                Assert.That(Sounds(entities, grid, "tractorbeam_engage.ogg"), Is.Empty);
                Assert.That(Sounds(entities, grid, "tractorbeam_loop.ogg"), Is.Empty);
                Assert.That(Sounds(entities, grid, "tractorbeam_disengage.ogg"), Has.Count.EqualTo(1));
            }
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoordinatedBeamsShareOneHullLoopUntilTheLastBeamReleases()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var maps = pair.Server.ResolveDependency<IMapManager>();
        await pair.Server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            var (secondSource, _, secondEmitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId,
                target, new Vector2(-15, 0));
            var first = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var second = entities.GetComponent<TractorBeamEmitterComponent>(secondEmitter);
            first.EngageSound = second.EngageSound = null;
            var system = entities.System<TractorBeamSystem>();
            system.UpdateBeforeSolve(false, Step);
            system.UpdateBeforeSolve(false, Step);
            foreach (var grid in new[] { source, secondSource, target })
            {
                var loop = Sounds(entities, grid, "tractorbeam_loop.ogg");
                Assert.That(loop, Has.Count.EqualTo(1));
                Assert.That(loop[0].Component.Params.Loop, Is.True);
            }
            var targetLoop = Sounds(entities, target, "tractorbeam_loop.ogg")[0].Entity;
            // Emitter deletion follows ComponentShutdown, not a console release command.
            entities.DeleteEntity(emitter);
            Assert.That(Sounds(entities, source, "tractorbeam_loop.ogg"), Is.Empty);
            Assert.That(Sounds(entities, target, "tractorbeam_loop.ogg")[0].Entity, Is.EqualTo(targetLoop));
            Assert.That(Sounds(entities, target, "tractorbeam_disengage.ogg"), Is.Empty);
            system.Release(secondEmitter, second);
            Assert.That(Sounds(entities, target, "tractorbeam_loop.ogg"), Is.Empty);
            Assert.That(Sounds(entities, target, "tractorbeam_disengage.ogg"), Has.Count.EqualTo(1));
            entities.DeleteEntity(source);
            entities.DeleteEntity(secondSource);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PowerLossDuringGraceStopsAudioEvenWhileCaptureIsRetained()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var maps = pair.Server.ResolveDependency<IMapManager>();
        await pair.Server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            beam.EngageSound = null;
            beam.PowerGraceUntil = TimeSpan.MaxValue;
            var system = entities.System<TractorBeamSystem>();
            system.UpdateBeforeSolve(false, Step);
            Assert.That(Sounds(entities, target, "tractorbeam_loop.ogg"), Has.Count.EqualTo(1));
            entities.GetComponent<PowerConsumerComponent>(emitter).NetworkLoad.ReceivingPower = 0;
            system.UpdateBeforeSolve(false, Step);
            Assert.That(beam.Target, Is.EqualTo(target));
            Assert.That(beam.Active, Is.False);
            Assert.That(Sounds(entities, source, "tractorbeam_loop.ogg"), Is.Empty);
            Assert.That(Sounds(entities, target, "tractorbeam_loop.ogg"), Is.Empty);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RotationalResistanceCreaksOnBothShipsWithCooldownAndIdleStaysQuiet()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var maps = pair.Server.ResolveDependency<IMapManager>();
        await pair.Server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            beam.CreakInitialMinimumDelay = beam.CreakInitialMaximumDelay = 0;
            var system = entities.System<TractorBeamSystem>();
            system.UpdateBeforeSolve(false, Step);
            Assert.That(Sounds(entities, source, "creak"), Is.Empty, "An idle held ship must not randomly creak.");
            entities.System<SharedPhysicsSystem>().SetAngularVelocity(target, 0.5f);
            system.UpdateBeforeSolve(false, Step);
            var sourceCreak = Sounds(entities, source, "creak");
            var targetCreak = Sounds(entities, target, "creak");
            Assert.That(sourceCreak, Has.Count.EqualTo(1));
            Assert.That(targetCreak, Has.Count.EqualTo(1));
            Assert.That(sourceCreak[0].Component.FileName, Is.EqualTo(targetCreak[0].Component.FileName));
            for (var i = 0; i < 10; i++)
            {
                entities.System<SharedPhysicsSystem>().SetAngularVelocity(target, 0.5f);
                system.UpdateBeforeSolve(false, Step);
            }
            Assert.That(Sounds(entities, source, "creak"), Has.Count.EqualTo(1), "Sustained attempts cannot spam overlapping creaks.");
            system.Release(emitter, beam);
            Assert.That(Sounds(entities, source, "creak"), Is.Empty);
            Assert.That(Sounds(entities, target, "creak"), Is.Empty);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    private static List<(EntityUid Entity, AudioComponent Component)> Sounds(IEntityManager entities, EntityUid grid, string filename)
    {
        var sounds = new List<(EntityUid, AudioComponent)>();
        var query = entities.EntityQueryEnumerator<AudioComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var audio, out var transform))
        {
            var soundFile = audio.FileName;
            if (!entities.IsQueuedForDeletion(uid) && transform.ParentUid == grid && soundFile.Contains(filename))
                sounds.Add((uid, audio));
        }
        return sounds;
    }
}
