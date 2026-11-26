#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Damage;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Damage;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// PartDamageVisualsComponent has projected per-part damage since WP5 with zero consumers (P3-D17). This proves
/// the server-side data itself is correct, and that it reaches the client's networked mirror, before WP11-4
/// builds a client-side reader on top of it.
/// </summary>
/// <remarks>PLAN3 §6.2 T-VISUALS. No sprite/screenshot assertion - this project prefers logic tests.</remarks>
[TestFixture]
[TestOf(typeof(WoundDamageProjectionSystem))]
public sealed class WolfmedVisualsTest : GameTest
{
    [Test]
    public async Task PartDamageProjectsToVisualsComponentTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var clientEntities = Pair.Client.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            // The test map is a vacuum: pressure damage on another part broke the absent-or-zero checks now and then.
            new Scenarios.WolfmedScenario(entities).SetAir(map.MapUid, true);
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entities.HasComponent<WoundHostComponent>(body), Is.True,
                "MobHuman is not a wound host; the D21/D32 species wiring is missing.");

            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var leftArm = parts.Single(part => part.Component.PartType == BodyPartType.Arm &&
                                                part.Component.Symmetry == BodyPartSymmetry.Left).Id;
            var leftHand = parts.Single(part => part.Component.PartType == BodyPartType.Hand &&
                                                 part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            Assert.That(routing.TryApplyPartDamage(body, leftArm, Spec("Blunt", 10)), Is.True);
            // P3-D25's server-side anchor: LHand is a real key TryGetVisualLayer writes, even though the stock
            // six-layer targetLayers list (base.yml) cannot render it - that fold is WP11-4's job, not this one's.
            Assert.That(routing.TryApplyPartDamage(body, leftHand, Spec("Blunt", 6)), Is.True);

            var visual = entities.GetComponent<PartDamageVisualsComponent>(body);
            AssertProjection(visual);
        });

        await Pair.RunTicksSync(10); // let the client catch up on the networked component state.

        await Pair.Client.WaitAssertion(() =>
        {
            var clientBody = clientEntities.GetEntity(entities.GetNetEntity(body));
            var visual = clientEntities.GetComponent<PartDamageVisualsComponent>(clientBody);
            AssertProjection(visual);
        });
    }

    private static void AssertProjection(PartDamageVisualsComponent visual)
    {
        Assert.Multiple(() =>
        {
            Assert.That(visual.Damage.TryGetValue(HumanoidVisualLayers.LArm, out var arm), Is.True,
                "the left arm's own damage must land under its own visual layer.");
            Assert.That(arm!.DamageDict[new ProtoId<DamageTypePrototype>("Blunt")], Is.EqualTo(FixedPoint2.New(10)));

            Assert.That(visual.Damage.TryGetValue(HumanoidVisualLayers.LHand, out var hand), Is.True,
                "TryGetVisualLayer writes LHand for a Hand/Left part; this is the key the stock 6-layer list can't render.");
            Assert.That(hand!.DamageDict[new ProtoId<DamageTypePrototype>("Blunt")], Is.EqualTo(FixedPoint2.New(6)));

            AssertAbsentOrZero(visual, HumanoidVisualLayers.RArm);
            AssertAbsentOrZero(visual, HumanoidVisualLayers.RHand);
        });
    }

    private static void AssertAbsentOrZero(PartDamageVisualsComponent visual, HumanoidVisualLayers layer)
    {
        if (visual.Damage.TryGetValue(layer, out var damage))
            Assert.That(damage.GetTotal(), Is.EqualTo(FixedPoint2.Zero), $"{layer} must be absent or zero.");
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };

    // --- V3: per-part degradation overlays ---

    /// <summary>
    /// The organic ladder on one limb: nothing, muscle, bone, nothing again. Each step is driven by the
    /// summed severity of that limb's own wounds, so the other limbs stay clean throughout.
    /// </summary>
    [Test]
    public async Task OrganicDegradationStagesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var clientEntities = Pair.Client.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var profile = Profile(prototypes);
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            Assert.That(Stage(entities, body, HumanoidVisualLayers.LArm),
                Is.EqualTo(WolfmedPartDegradation.None), "an unhurt arm shows nothing.");

            // Below stage 1: a scratch is not worth an overlay.
            var wound = wounds.CreateOrMergeWound(arm, "SlashWound", profile.Stage1 - FixedPoint2.New(5));
            Assert.That(wound, Is.Not.Null);
            Assert.That(Stage(entities, body, HumanoidVisualLayers.LArm),
                Is.EqualTo(WolfmedPartDegradation.None));

            // Two shallow cuts add up to one open wound: severity is summed across the part.
            Assert.That(wounds.CreateOrMergeWound(arm, "PiercingWound", FixedPoint2.New(10)), Is.Not.Null);
            Assert.That(Stage(entities, body, HumanoidVisualLayers.LArm),
                Is.EqualTo(WolfmedPartDegradation.Muscle));

            Assert.That(wounds.ChangeSeverity(wound!.Value, profile.Stage2), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(Stage(entities, body, HumanoidVisualLayers.LArm),
                    Is.EqualTo(WolfmedPartDegradation.Bone));
                Assert.That(Stage(entities, body, HumanoidVisualLayers.RArm),
                    Is.EqualTo(WolfmedPartDegradation.None), "damage stays on the limb that took it.");
                Assert.That(Stage(entities, body, HumanoidVisualLayers.Chest),
                    Is.EqualTo(WolfmedPartDegradation.None));
            });
        });

        await Pair.RunTicksSync(10);

        await Pair.Client.WaitAssertion(() =>
        {
            var clientBody = clientEntities.GetEntity(entities.GetNetEntity(body));
            Assert.That(clientEntities.GetComponent<PartDamageVisualsComponent>(clientBody)
                    .Degradation.GetValueOrDefault(HumanoidVisualLayers.LArm),
                Is.EqualTo(WolfmedPartDegradation.Bone), "the stage has to reach the client to be drawn.");

            // The overlay layer exists, is on, and sits between the arm it belongs to and the clothing
            // layers, so a sleeve covers it the way a sleeve covers the arm.
            var sprites = clientEntities.System<SpriteSystem>();
            var sprite = clientEntities.GetComponent<SpriteComponent>(clientBody);
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedDegradationLArm",
                out var overlay, false), Is.True, "the client never added the overlay layer.");
            Assert.Multiple(() =>
            {
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), HumanoidVisualLayers.LArm,
                    out var limb, false), Is.True);
                Assert.That(overlay, Is.GreaterThan(limb), "the overlay must draw over its own limb.");
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "jumpsuit",
                    out var jumpsuit, false), Is.True);
                Assert.That(overlay, Is.LessThan(jumpsuit), "clothing must still cover the overlay.");
                Assert.That(sprites.TryGetLayer((clientBody, sprite), "WolfmedDegradationLArm",
                    out var layer, false) && layer.Visible, Is.True, "the overlay layer is off.");
            });
        });

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            foreach (var wound in wounds.GetWounds(arm).ToArray())
                wounds.RemoveWound(wound.Owner);

            Assert.That(Stage(entities, body, HumanoidVisualLayers.LArm),
                Is.EqualTo(WolfmedPartDegradation.None), "healing the wounds clears the overlay.");
        });
    }

    /// <summary>A chassis shows struts and wiring, never muscle and bone; dead tissue outranks both.</summary>
    [Test]
    public async Task MechanicalAndNecroticDegradationTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var profile = Profile(prototypes);

            var machine = entities.SpawnEntity("MobIPC", map.GridCoords);
            var frame = Part(entities, machine, BodyPartType.Arm, BodyPartSymmetry.Left);
            var wound = wounds.CreateOrMergeWound(frame, "IpcMechanicalDamageWound", profile.Stage1);
            Assert.That(wound, Is.Not.Null);
            Assert.That(Stage(entities, machine, HumanoidVisualLayers.LArm),
                Is.EqualTo(WolfmedPartDegradation.Struts));

            Assert.That(wounds.ChangeSeverity(wound!.Value, profile.Stage2 - profile.Stage1), Is.True);
            Assert.That(Stage(entities, machine, HumanoidVisualLayers.LArm),
                Is.EqualTo(WolfmedPartDegradation.Wiring));

            // Necrosis is a state, not a stage: it replaces whatever the severity would have shown.
            var flesh = entities.SpawnEntity("MobHuman", map.GridCoords);
            var leg = Part(entities, flesh, BodyPartType.Leg, BodyPartSymmetry.Right);
            Assert.That(wounds.CreateOrMergeWound(leg, "SlashWound", profile.Stage1), Is.Not.Null);
            Assert.That(Stage(entities, flesh, HumanoidVisualLayers.RLeg),
                Is.EqualTo(WolfmedPartDegradation.Muscle));

            entities.System<WolfmedNecrosisSystem>().MakeNecrotic(leg);
            Assert.That(Stage(entities, flesh, HumanoidVisualLayers.RLeg),
                Is.EqualTo(WolfmedPartDegradation.Necrotic));
        });
    }

    /// <summary>
    /// A severed limb keeps its own overlay, alongside the phase-3 detached damage art, and gives it back
    /// to the body when it is put on again.
    /// </summary>
    [Test]
    public async Task DetachedLimbKeepsItsDegradationTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var profile = Profile(prototypes);

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(wounds.CreateOrMergeWound(arm, "SlashWound", profile.Stage2), Is.Not.Null);
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Slash", 30)), Is.True);

            Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(arm), Is.True);

            var detached = entities.GetComponent<PartDamageVisualsComponent>(arm);
            Assert.Multiple(() =>
            {
                Assert.That(detached.Degradation.GetValueOrDefault(HumanoidVisualLayers.LArm),
                    Is.EqualTo(WolfmedPartDegradation.Bone), "the limb carries its own overlay once it is off.");
                Assert.That(detached.Damage.ContainsKey(HumanoidVisualLayers.LArm), Is.True,
                    "phase 3's detached damage art still has its data.");
                Assert.That(Stage(entities, body, HumanoidVisualLayers.LArm),
                    Is.EqualTo(WolfmedPartDegradation.None), "the body has no arm left to draw.");
            });
        });
    }

    /// <summary>
    /// Every (layer, stage) pair the profile can select resolves to a state that exists. A missing state
    /// is a client error log, which is a test failure everywhere else in this suite.
    /// </summary>
    [Test]
    public async Task DegradationArtCoversEveryLayerAndStageTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var profile = Profile(server.ResolveDependency<IPrototypeManager>());
        var cache = Pair.Client.ResolveDependency<IResourceCache>();

        await Pair.Client.WaitAssertion(() =>
        {
            Assert.That(cache.TryGetResource<RSIResource>(new ResPath("/Textures") / profile.Rsi, out var rsi),
                Is.True, $"{profile.ID}: RSI {profile.Rsi} not found.");

            Assert.Multiple(() =>
            {
                foreach (var layer in WolfmedDegradationLayers.All)
                {
                    foreach (var stage in Enum.GetValues<WolfmedPartDegradation>())
                    {
                        if (stage == WolfmedPartDegradation.None)
                            continue;

                        var state = profile.GetState(layer, stage);
                        Assert.That(state, Is.Not.Null, $"{profile.ID}: no state for {layer}/{stage}.");
                        Assert.That(rsi!.RSI.TryGetState(state!, out _), Is.True,
                            $"{profile.ID}: state {state} is missing from {profile.Rsi}.");
                    }
                }
            });
        });
    }

    private static WolfmedDegradationProfilePrototype Profile(IPrototypeManager prototypes)
    {
        return prototypes.Index<WolfmedDegradationProfilePrototype>(
            WolfmedDegradationVisualsComponent.DefaultProfile);
    }

    private static WolfmedPartDegradation Stage(IEntityManager entities, EntityUid body, HumanoidVisualLayers layer)
    {
        // Wound-driven refreshes are coalesced to one walk per body per tick, so drain them first rather
        // than running server ticks between every assertion.
        entities.System<WolfmedDegradationVisualsSystem>().Update(0f);
        return entities.GetComponent<PartDamageVisualsComponent>(body).Degradation.GetValueOrDefault(layer);
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type,
        BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }
}
