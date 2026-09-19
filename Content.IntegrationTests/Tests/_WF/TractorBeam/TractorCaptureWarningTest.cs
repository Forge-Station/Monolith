using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.TractorBeam;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorCaptureWarningTest
{
    [TestCase(IFFFlags.Hide)]
    [TestCase(IFFFlags.HideLabel)]
    [TestCase(IFFFlags.HideLabelAlways)]
    public async Task HiddenAttackerIdentityStaysAnonymousWithoutSuppressingCaptureWarning(IFFFlags hiddenFlag)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, _, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            entities.System<MetaDataSystem>().SetEntityName(source, "SECRET ARRESTOR NAME");
            var helm = entities.SpawnEntity("ComputerShuttle", new EntityCoordinates(target, new Vector2(0.5f, 0.5f)));
            var helms = entities.System<ShuttleConsoleSystem>();
            var shuttles = entities.System<SharedShuttleSystem>();
            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, 1f / 60f);
            helms.Update(0.25f);
            Assert.That(Sources(), Is.EqualTo(new[] { "SECRET ARRESTOR NAME" }));
            shuttles.AddIFFFlag(source, hiddenFlag);
            helms.Update(0.25f);
            Assert.That(Sources(), Has.Length.EqualTo(1), "A hidden attacker still generates a capture warning.");
            Assert.That(Sources(), Does.Not.Contain("SECRET ARRESTOR NAME"), "The warning cannot reveal hidden ship identity.");
            shuttles.RemoveIFFFlag(source, hiddenFlag);
            helms.Update(0.25f);
            Assert.That(Sources(), Is.EqualTo(new[] { "SECRET ARRESTOR NAME" }), "IFF changes refresh an existing warning.");
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
            helms.Update(0.25f);

            string[] Sources()
            {
                Assert.That(entities.System<SharedUserInterfaceSystem>().TryGetUiState<ShuttleBoundUserInterfaceState>(
                    helm, ShuttleConsoleUiKey.Key, out var state), Is.True);
                return state!.TractorSources;
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VictimHelmNamesDistinctActiveArrestorsAndClearsOnReleasePowerLossAndDeletion()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var beamSystem = entities.System<TractorBeamSystem>();
            var helms = entities.System<ShuttleConsoleSystem>();
            var metadata = entities.System<MetaDataSystem>();
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId);
            var (secondSource, _, secondEmitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId,
                target, new Vector2(-15, 0));
            metadata.SetEntityName(source, "ARRESTOR ALPHA");
            metadata.SetEntityName(secondSource, "ARRESTOR BETA");
            var victimHelm = entities.SpawnEntity("ComputerShuttle", new EntityCoordinates(target, new Vector2(0.5f, 0.5f)));
            var arrestorHelm = entities.SpawnEntity("ComputerShuttle", new EntityCoordinates(source, new Vector2(3.5f, 0.5f)));
            helms.Update(0.25f);
            Assert.That(Sources(victimHelm), Is.Empty, "An acquired but inactive beam does not report a capture.");

            // A second functioning dish on ALPHA must not duplicate that vessel in the banner.
            var firstBeam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var extraDish = entities.SpawnEntity("WFTractorBeamEmitter",
                new EntityCoordinates(source, new Vector2(2.5f, 0.5f)));
            entities.System<SharedTransformSystem>().SetWorldRotation(extraDish, Angle.FromDegrees(-90));
            var extraBeam = entities.GetComponent<TractorBeamEmitterComponent>(extraDish);
            extraBeam.SourceGrid = source;
            extraBeam.Target = target;
            extraBeam.Controller = firstBeam.Controller;
            extraBeam.TargetOffset = firstBeam.TargetOffset;
            extraBeam.HoldDistance = firstBeam.HoldDistance;
            entities.GetComponent<PowerConsumerComponent>(extraDish).NetworkLoad.ReceivingPower = extraBeam.MaxPower;
            beamSystem.UpdateBeforeSolve(false, 1f / 60f);
            helms.Update(0.25f);
            Assert.That(firstBeam.Active && extraBeam.Active && entities.GetComponent<TractorBeamEmitterComponent>(secondEmitter).Active,
                Is.True, "All three real dishes must be actively capturing the victim.");
            Assert.That(Sources(victimHelm), Is.EqualTo(new[] { "ARRESTOR ALPHA", "ARRESTOR BETA" }));
            Assert.That(Sources(arrestorHelm), Is.Empty, "The attacking vessel is not itself marked captured.");

            // The warning follows current names and survives a helm refresh/reopen.
            metadata.SetEntityName(source, "ARRESTOR [ALPHA]");
            helms.Update(0.25f);
            helms.RefreshShuttleConsoles(target);
            Assert.That(Sources(victimHelm), Is.EquivalentTo(new[] { "ARRESTOR [ALPHA]", "ARRESTOR BETA" }));

            beamSystem.Release(emitter, firstBeam);
            helms.Update(0.25f);
            Assert.That(Sources(victimHelm).Length, Is.EqualTo(2), "ALPHA's remaining dish still holds the victim.");
            entities.DeleteEntity(extraDish);
            helms.Update(0.25f);
            Assert.That(Sources(victimHelm), Is.EqualTo(new[] { "ARRESTOR BETA" }));

            entities.GetComponent<PowerConsumerComponent>(secondEmitter).NetworkLoad.ReceivingPower = 0;
            entities.GetComponent<TractorBeamEmitterComponent>(secondEmitter).PowerGraceUntil = TimeSpan.Zero;
            beamSystem.UpdateBeforeSolve(false, 1f / 60f);
            helms.Update(0.25f);
            Assert.That(Sources(victimHelm), Is.Empty, "Power loss must clear the capture warning.");

            var (lastSource, _, _, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId, target, new Vector2(-25, 0));
            metadata.SetEntityName(lastSource, "ARRESTOR GAMMA");
            beamSystem.UpdateBeforeSolve(false, 1f / 60f);
            helms.Update(0.25f);
            Assert.That(Sources(victimHelm), Is.EqualTo(new[] { "ARRESTOR GAMMA" }));
            entities.DeleteEntity(lastSource);
            helms.Update(0.25f);
            Assert.That(Sources(victimHelm), Is.Empty, "Deleting the source ship must not leave a stale warning.");
            entities.DeleteEntity(source);
            entities.DeleteEntity(secondSource);
            entities.DeleteEntity(target);
            helms.Update(0.25f);

            string[] Sources(EntityUid helm)
            {
                Assert.That(entities.System<SharedUserInterfaceSystem>().TryGetUiState<ShuttleBoundUserInterfaceState>(
                    helm, ShuttleConsoleUiKey.Key, out var state), Is.True);
                return state!.TractorSources;
            }
        });
        await pair.CleanReturnAsync();
    }
}
