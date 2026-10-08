using System.Numerics;
using System.Linq;
using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Forge.Radio.Components;
using Content.Shared._Forge.TTS;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Silicons.StationAi;
using Content.Shared._Forge.Silicons.StationAi;
using Content.Shared.Ghost.Roles;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Forge.StationAi;

[TestFixture]
public sealed class StationAiPrototypeTests
{
    private static readonly string[] BrainPrototypes =
    {
        "StationAiBrain",
        "StationAiBrainForerunner",
        "StationAiBrainForerunnerWhitelist",
        "StationAiBrainRedacted",
        "StationAiBrainVessel",
        "StationAiBrainTSFMC",
        "StationAiBrainPDV",
    };

    [Test]
    public async Task GhostRolesAndCameraRcdContractsAreValid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var coordinates = new MapCoordinates(Vector2.Zero, map.MapId);

            foreach (var prototype in BrainPrototypes)
            {
                var brain = entityManager.SpawnEntity(prototype, coordinates);
                var ghostRole = entityManager.GetComponent<GhostRoleComponent>(brain);
                var tts = entityManager.GetComponent<TTSComponent>(brain);

                Assert.Multiple(() =>
                {
                    Assert.That(tts.VoicePrototypeId, Is.EqualTo("Glados"), prototype);
                    Assert.That(tts.FixedVoice, Is.True, prototype);
                    Assert.That(ghostRole.ReregisterOnGhost, Is.True, $"{prototype} must reopen its takeover role");
                    Assert.That(ghostRole.RaffleConfig, Is.Not.Null, $"{prototype} must use the raffle");
                    Assert.That(ghostRole.Prototype, Is.Not.Null, $"{prototype} must reference a ghost role prototype");
                });

                entityManager.DeleteEntity(brain);
            }

            var standardBrain = entityManager.SpawnEntity("StationAiBrain", coordinates);
            var standardRole = entityManager.GetComponent<GhostRoleComponent>(standardBrain);
            Assert.Multiple(() =>
            {
                Assert.That(standardRole.Prototype, Is.EqualTo((ProtoId<GhostRolePrototype>) "StationAICore"));
                Assert.That(standardRole.Requirements, Has.Exactly(1).InstanceOf<OverallPlaytimeRequirement>());
                Assert.That(prototypeManager.Index<GhostRolePrototype>("StationAICore").EntityPrototype,
                    Is.EqualTo((EntProtoId) "PlayerStationAi"));
            });
            entityManager.DeleteEntity(standardBrain);

            var standardRcd = entityManager.SpawnEntity("RCDRecharging", coordinates);
            var stationAiRcd = entityManager.SpawnEntity("RCDRechargingForerunner", coordinates);
            var cameraRecipe = (ProtoId<RCDPrototype>) "SurveillanceCamera";

            Assert.Multiple(() =>
            {
                Assert.That(entityManager.GetComponent<RCDComponent>(standardRcd).AvailablePrototypes,
                    Does.Not.Contain(cameraRecipe));
                Assert.That(entityManager.GetComponent<RCDComponent>(stationAiRcd).AvailablePrototypes,
                    Does.Contain(cameraRecipe));
                Assert.That(entityManager.HasComponent<StationAiCameraRcdComponent>(stationAiRcd), Is.True);

                var recipe = prototypeManager.Index<RCDPrototype>(cameraRecipe);
                Assert.That(recipe.Cost, Is.EqualTo(3));
                Assert.That(recipe.Delay, Is.EqualTo(2f));
                Assert.That(recipe.Prototype, Is.EqualTo("SurveillanceCameraGeneral"));
            });

            entityManager.DeleteEntity(standardRcd);
            entityManager.DeleteEntity(stationAiRcd);

            var camera = entityManager.SpawnEntity("SurveillanceCameraGeneral", coordinates);
            var destructible = entityManager.GetComponent<DestructibleComponent>(camera);
            var destructionTrigger = destructible.Thresholds
                .Select(threshold => threshold.Trigger)
                .OfType<DamageTrigger>()
                .Single();

            Assert.Multiple(() =>
            {
                Assert.That(destructionTrigger.Damage, Is.EqualTo(600));
                Assert.That(entityManager.HasComponent<StationAiCameraRelayComponent>(camera), Is.True);
            });
            entityManager.DeleteEntity(camera);

            var standardCore = entityManager.SpawnEntity("PlayerStationAiEmpty", coordinates);
            var forerunnerCore = entityManager.SpawnEntity("PlayerStationAiForerunner", coordinates);
            var forerunnerWhitelistCore = entityManager.SpawnEntity("PlayerStationAiForerunnerWhitelist", coordinates);
            Assert.Multiple(() =>
            {
                Assert.That(entityManager.GetComponent<StationAiScreenComponent>(standardCore).ForceNamePrefix,
                    Is.Empty);
                Assert.That(entityManager.GetComponent<StationAiScreenComponent>(forerunnerCore).ForceNamePrefix,
                    Is.EqualTo("ADC"));
                Assert.That(entityManager.GetComponent<StationAiScreenComponent>(forerunnerWhitelistCore).ForceNamePrefix,
                    Is.EqualTo("ADC"));
            });
            entityManager.DeleteEntity(standardCore);
            var stationAi = server.System<SharedStationAiSystem>();
            var radio = server.System<RadioSystem>();
            var handheld = prototypeManager.Index<RadioChannelPrototype>("Handheld");
            var common = prototypeManager.Index<RadioChannelPrototype>("Common");
            foreach (var core in new[] { forerunnerCore, forerunnerWhitelistCore })
            {
                var holder = entityManager.GetComponent<EncryptionKeyHolderComponent>(core);
                var key = holder.KeyContainer.ContainedEntities.Single();
                var configurable = entityManager.GetComponent<ConfigurableEncryptionKeyComponent>(key);
                Assert.That(stationAi.TryGetHeld((core, entityManager.GetComponent<StationAiCoreComponent>(core)), out var brain), Is.True);
                Assert.That(entityManager.GetComponent<IntrinsicRadioTransmitterComponent>(brain).Channels,
                    Does.Contain((ProtoId<RadioChannelPrototype>) "Handheld"));
                Assert.That(entityManager.GetComponent<ActiveRadioComponent>(brain).Channels,
                    Does.Contain((ProtoId<RadioChannelPrototype>) "Handheld"));
                configurable.Frequency = 1459;
                Assert.That(radio.GetFrequency(brain, handheld), Is.EqualTo(1459));
                Assert.That(radio.GetFrequency(brain, common), Is.EqualTo(common.Frequency));
            }
            entityManager.DeleteEntity(forerunnerCore);
            entityManager.DeleteEntity(forerunnerWhitelistCore);

            var screens = prototypeManager.EnumeratePrototypes<StationAiScreenPrototype>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(screens, Has.Length.GreaterThan(2));
                Assert.That(screens.Select(screen => screen.State), Has.All.Not.Empty);
                Assert.That(prototypeManager.HasIndex<StationAiScreenPrototype>("StationAiScreenDefault"), Is.True);
                Assert.That(prototypeManager.HasIndex<StationAiScreenPrototype>("StationAiScreenFace"), Is.True);
            });
        });

        await pair.CleanReturnAsync();
    }
}
