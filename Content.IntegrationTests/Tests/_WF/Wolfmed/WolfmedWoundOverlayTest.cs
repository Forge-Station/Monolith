#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.Components;
using Content.Server._WF.Wolfmed.Damage;
using Content.Server._WF.Wolfmed.Gore;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Damage;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 4, VISUALS: the open-wound and rot overlays. The server data, the client layer it draws and where that
/// layer sits in the stack.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedWoundOverlaySystem))]
public sealed class WolfmedWoundOverlayTest : GameTest
{
    /// <summary>
    /// A bleeding slash drips; dressed it stops and shows the still wound; a heavy bleed trickles; an unhurt limb and a
    /// machine's limb draw nothing.
    /// </summary>
    [Test]
    public async Task BleedingWoundOverlayTest()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await Server.WaitAssertion(() =>
        {
            var wounds = SEntMan.System<WoundSystem>();
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var torso = Part(body, BodyPartType.Torso);

            // 20 severity bleeds 0.6 a second: a drip. 60 bleeds 3.6: a trickle.
            Assert.That(wounds.CreateOrMergeWound(arm, "SlashWound", FixedPoint2.New(20)), Is.Not.Null);
            Assert.That(wounds.CreateOrMergeWound(torso, "SlashWound", FixedPoint2.New(60)), Is.Not.Null);
            Assert.That(SEntMan.System<WoundBleedingSystem>().GetPartRate(arm), Is.GreaterThan(0f), "the slash does not bleed.");

            var visual = Refresh(body);
            Assert.Multiple(() =>
            {
                Assert.That(visual.Wounds.GetValueOrDefault(HumanoidVisualLayers.LArm), Is.EqualTo(WolfmedWoundOverlay.Drip));
                Assert.That(visual.Wounds.GetValueOrDefault(HumanoidVisualLayers.Chest), Is.EqualTo(WolfmedWoundOverlay.Stream));
                Assert.That(visual.Wounds.ContainsKey(HumanoidVisualLayers.RArm), Is.False, "an unhurt arm has a wound overlay.");
                Assert.That(visual.WoundColor, Is.Not.EqualTo(Color.White), "the wound is not tinted with the blood colour.");
            });
        });

        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var (clientBody, sprite) = ClientSprite(body);
            var sprites = CEntMan.System<SpriteSystem>();
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedWoundLArm", out var overlay, false), Is.True,
                "the client never added the wound layer.");
            Assert.Multiple(() =>
            {
                Assert.That(sprites.LayerGetRsiState((clientBody, sprite), overlay).Name, Is.EqualTo("LArm_drip"));
                Assert.That(sprites.TryGetLayer((clientBody, sprite), overlay, out var layer, false) && layer.Visible, Is.True);
                Assert.That(layer!.Color, Is.EqualTo(CEntMan.GetComponent<PartDamageVisualsComponent>(clientBody).WoundColor));
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), HumanoidVisualLayers.LArm, out var limb, false), Is.True);
                Assert.That(overlay, Is.GreaterThan(limb), "the wound must draw over its limb.");
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "jumpsuit", out var jumpsuit, false), Is.True);
                Assert.That(overlay, Is.LessThan(jumpsuit), "a sleeve must still cover the wound.");
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedWoundChest", out var chest, false), Is.True);
                Assert.That(sprites.LayerGetRsiState((clientBody, sprite), chest).Name, Is.EqualTo("Chest_stream"));
                Assert.That(chest, Is.LessThan(jumpsuit), "a shirt must still cover a chest wound.");
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedWoundRArm", out _, false), Is.False,
                    "a limb with no wound has a wound layer.");
            });
        });

        await Server.WaitAssertion(() =>
        {
            var arm = Part(body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(SEntMan.System<WoundBleedingSystem>().ReducePartBleeding(arm, 1000, dressing: true));
            Assert.That(Refresh(body).Wounds.GetValueOrDefault(HumanoidVisualLayers.LArm), Is.EqualTo(WolfmedWoundOverlay.Old),
                "a dressed wound does not stop dripping.");

            // Machines keep their chassis visuals.
            var machine = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var frame = Part(machine, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(SEntMan.System<WoundSystem>().CreateOrMergeWound(frame, "IpcMechanicalDamageWound", FixedPoint2.New(40)),
                Is.Not.Null);
            Assert.That(Refresh(machine).Wounds, Is.Empty, "a mechanical part shows a wound overlay.");
        });

        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var (clientBody, sprite) = ClientSprite(body);
            Assert.That(CEntMan.System<SpriteSystem>().LayerGetRsiState((clientBody, sprite), "WolfmedWoundLArm", default).Name,
                Is.EqualTo("LArm_old"), "the client still draws the drip.");
        });
    }

    /// <summary>
    /// Dead tissue rots each arm under its own side's state; a septic torso rots the chest with the groin folded in;
    /// antibiotics under the stage clear it; an amputated limb takes its rot with it.
    /// </summary>
    [Test]
    public async Task RotOverlayTest()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        EntityUid wound = default;

        await Server.WaitAssertion(() =>
        {
            var necrosis = SEntMan.System<WolfmedNecrosisSystem>();
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var left = Part(body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var right = Part(body, BodyPartType.Arm, BodyPartSymmetry.Right);
            var torso = Part(body, BodyPartType.Torso);

            necrosis.MakeNecrotic(left);
            Assert.That(Refresh(body).Rot, Is.EquivalentTo(new[] { HumanoidVisualLayers.LArm }));
            necrosis.MakeNecrotic(right);
            Assert.That(Refresh(body).Rot, Is.EquivalentTo(new[] { HumanoidVisualLayers.LArm, HumanoidVisualLayers.RArm }));

            // An infection at the top stage on the torso.
            wound = SEntMan.System<WoundSystem>().CreateOrMergeWound(torso, "SlashWound", FixedPoint2.New(20))!.Value;
            var infection = SEntMan.EnsureComponent<WolfmedInfectionComponent>(wound);
            infection.Progress = SEntMan.System<WolfmedInfectionSystem>().Profile.SepsisAt;
            infection.Stage = WolfmedInfectionStage.Septic;
            Assert.That(Refresh(body).Rot, Does.Contain(HumanoidVisualLayers.Chest), "a septic torso does not rot.");
        });

        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var (clientBody, sprite) = ClientSprite(body);
            var sprites = CEntMan.System<SpriteSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(sprites.LayerGetRsiState((clientBody, sprite), "WolfmedRotLArm", default).Name, Is.EqualTo("LArm_rot"));
                Assert.That(sprites.LayerGetRsiState((clientBody, sprite), "WolfmedRotRArm", default).Name, Is.EqualTo("RArm_rot"));
                Assert.That(sprites.LayerGetRsiState((clientBody, sprite), "WolfmedRotChest", default).Name, Is.EqualTo("Chest_rot"),
                    "the chest's rot (chest and groin) is not drawn.");
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedRotChest", out var rot, false), Is.True);
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedWoundChest", out var cut, false), Is.True);
                Assert.That(cut, Is.GreaterThan(rot), "the wound must draw over the rot.");
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "jumpsuit", out var jumpsuit, false), Is.True);
                Assert.That(rot, Is.LessThan(jumpsuit), "a shirt must still cover the rot.");
            });
        });

        await Server.WaitAssertion(() =>
        {
            // Antibiotics under the stage clear it; the arms stay dead.
            Assert.That(SEntMan.System<WolfmedInfectionSystem>().Treat(body, 1000f), Is.True);
            Assert.That(SEntMan.GetComponent<WolfmedInfectionComponent>(wound).Stage, Is.LessThan(WolfmedInfectionStage.Septic));
            Assert.That(Refresh(body).Rot, Is.EquivalentTo(new[] { HumanoidVisualLayers.LArm, HumanoidVisualLayers.RArm }),
                "treating the infection did not clear the torso's rot.");

            // An amputated arm takes its rot with it.
            var left = Part(body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(SEntMan.System<Content.Shared._WF.Wolfmed.Compat.WolfmedBodySystem>().TryDetachPart(left), Is.True);
            Assert.That(Refresh(body).Rot, Is.EquivalentTo(new[] { HumanoidVisualLayers.RArm }));
        });

        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var (clientBody, sprite) = ClientSprite(body);
            var sprites = CEntMan.System<SpriteSystem>();
            Assert.That(sprites.TryGetLayer((clientBody, sprite), "WolfmedRotChest", out var layer, false) && layer.Visible,
                Is.False, "the torso still rots on the client.");
        });
    }

    /// <summary>
    /// Playtest 4: every artery on the sprite. A cut artery on the head is the head's, a limb off is that limb's stump
    /// (spurting while it bleeds untreated, still once dressed), a hand off then the arm off moves the site up the limb,
    /// a head off is the neck. The client draws the still artery above everything, tinted with the blood colour, and
    /// plays the spray once per blood spurt.
    /// </summary>
    [Test]
    public async Task ArteryOverlayTest()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        EntityUid head = default;

        await Server.WaitAssertion(() =>
        {
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);

            // The test map is a vacuum: a pressure tick part-way through bruises and burns the head, and no clamp takes on those.
            SEntMan.RemoveComponent<BarotraumaComponent>(body);
            head = Part(body, BodyPartType.Head);
            Assert.That(SEntMan.System<WoundSystem>().CreateOrMergeWound(head, "WFWolfmedArterialBleedWound", FixedPoint2.New(10)),
                Is.Not.Null);
            Assert.That(SEntMan.System<WoundBleedingSystem>().GetPartRate(head), Is.GreaterThan(0f), "the artery does not bleed.");
            var visual = Refresh(body);
            Assert.Multiple(() =>
            {
                Assert.That(visual.Arteries.GetValueOrDefault(WolfmedArterySite.Head), Is.EqualTo(WolfmedArteryOverlay.Bleeding),
                    "a pumping artery on the head does not spurt.");
                Assert.That(visual.Arteries, Has.Count.EqualTo(1), "an unhurt body shows more than the head's artery.");
            });
        });

        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var (clientBody, sprite) = ClientSprite(body);
            var sprites = CEntMan.System<SpriteSystem>();
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedArteryHead", out var artery, false), Is.True,
                "the client never added the artery layer.");
            Assert.Multiple(() =>
            {
                Assert.That(sprites.LayerGetRsiState((clientBody, sprite), artery).Name, Is.EqualTo("head_artery0"),
                    "the artery loops its spray instead of resting on the still frame.");
                Assert.That(sprites.TryGetLayer((clientBody, sprite), artery, out var layer, false) && layer.Visible, Is.True);
                Assert.That(layer!.Color, Is.EqualTo(CEntMan.GetComponent<PartDamageVisualsComponent>(clientBody).WoundColor));
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), HumanoidVisualLayers.Hair, out var hair, false), Is.True);
                Assert.That(artery, Is.GreaterThan(hair), "hair must not hide the spray.");
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "head", out var helmet, false), Is.True);
                Assert.That(artery, Is.GreaterThan(helmet), "a helmet must not hide the spray.");
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedArteryNeck", out _, false), Is.False,
                    "a head still on has a neck layer.");
            });
        });

        await Server.WaitAssertion(() =>
        {
            var bleeding = SEntMan.System<WoundBleedingSystem>();
            var amputation = SEntMan.System<AmputationSystem>();
            foreach (var wound in SEntMan.System<WoundSystem>().GetWounds(head))
                Assert.That(bleeding.SetTreatment(wound.Owner, BleedingTreatment.Clamped), Is.True, "the clamp did not take.");
            Assert.That(bleeding.GetPartRate(head), Is.EqualTo(0f), "a clamp did not stop the artery.");
            Assert.That(Refresh(body).Arteries.GetValueOrDefault(WolfmedArterySite.Head), Is.EqualTo(WolfmedArteryOverlay.Still),
                "a clamped artery is not the still one.");

            // The left arm off: its stump on the torso is the left arm's site, and the first spurt moves the counter.
            Assert.That(amputation.TryAmputate(body, Part(body, BodyPartType.Arm, BodyPartSymmetry.Left)), Is.True);
            var visual = Refresh(body);
            Assert.That(visual.Arteries.GetValueOrDefault(WolfmedArterySite.LArm), Is.EqualTo(WolfmedArteryOverlay.Bleeding),
                "a fresh arm stump does not spurt.");
            Assert.That(visual.ArterySprayAt, Is.EqualTo(TimeSpan.Zero), "the spray was stamped before any spurt.");
            Assert.That(SEntMan.System<WolfmedBleedSpurtSystem>().TrySpurt(body), Is.True, "the body did not spurt.");
            Assert.That(visual.ArterySprayAt, Is.GreaterThan(TimeSpan.Zero), "a spurt did not stamp the spray.");
        });

        await Pair.RunTicksSync(2);
        var sprayed = false;
        for (var attempt = 0; attempt < 10 && !sprayed; attempt++)
        {
            await Pair.RunTicksSync(1);
            await Client.WaitPost(() =>
            {
                var (clientBody, _) = ClientSprite(body);
                sprayed = CEntMan.System<AnimationPlayerSystem>().HasRunningAnimation(clientBody, "WolfmedArteryLArm");
            });
        }
        Assert.That(sprayed, Is.True, "the client never played the arm's spray.");

        await Server.WaitAssertion(() =>
        {
            var bleeding = SEntMan.System<WoundBleedingSystem>();
            var amputation = SEntMan.System<AmputationSystem>();
            var torso = Part(body, BodyPartType.Torso);
            Assert.That(bleeding.ReducePartBleeding(torso, 1000, dressing: true), Is.True, "the arm stump could not be dressed.");
            Assert.That(Refresh(body).Arteries.GetValueOrDefault(WolfmedArterySite.LArm), Is.EqualTo(WolfmedArteryOverlay.Still),
                "a dressed arm stump is not the still one.");

            // The right hand off: its stump sits on the right arm.
            Assert.That(amputation.TryAmputate(body, Part(body, BodyPartType.Hand, BodyPartSymmetry.Right)), Is.True);
            Assert.That(Refresh(body).Arteries.GetValueOrDefault(WolfmedArterySite.RHand), Is.EqualTo(WolfmedArteryOverlay.Bleeding),
                "a fresh hand stump does not spurt.");
        });

        // The client sees the hand's artery before the arm goes, so the layer exists to be hidden.
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var (clientBody, sprite) = ClientSprite(body);
            Assert.That(CEntMan.System<SpriteSystem>().LayerMapTryGet((clientBody, sprite), "WolfmedArteryRHand", out _, false), Is.True,
                "the client never added the hand's artery.");
        });

        await Server.WaitAssertion(() =>
        {
            var bleeding = SEntMan.System<WoundBleedingSystem>();
            var amputation = SEntMan.System<AmputationSystem>();
            var torso = Part(body, BodyPartType.Torso);

            // The arm off after it takes the hand's stump away.
            Assert.That(amputation.TryAmputate(body, Part(body, BodyPartType.Arm, BodyPartSymmetry.Right)), Is.True);
            var visual = Refresh(body);
            Assert.Multiple(() =>
            {
                Assert.That(visual.Arteries.ContainsKey(WolfmedArterySite.RHand), Is.False, "the hand's stump outlived the arm.");
                Assert.That(visual.Arteries.GetValueOrDefault(WolfmedArterySite.RArm), Is.EqualTo(WolfmedArteryOverlay.Bleeding),
                    "a fresh arm stump does not spurt.");
            });

            // The head off: the neck.
            Assert.That(amputation.TryAmputate(body, head), Is.True, "the head did not come off.");
            visual = Refresh(body);
            Assert.Multiple(() =>
            {
                Assert.That(visual.Arteries.ContainsKey(WolfmedArterySite.Head), Is.False, "a head that is off still shows its artery.");
                Assert.That(visual.Arteries.GetValueOrDefault(WolfmedArterySite.Neck), Is.EqualTo(WolfmedArteryOverlay.Bleeding),
                    "the neck does not spurt.");
            });
            Assert.That(bleeding.ReducePartBleeding(torso, 1000, dressing: true), Is.True, "the neck could not be dressed.");
            Assert.That(Refresh(body).Arteries.GetValueOrDefault(WolfmedArterySite.Neck), Is.EqualTo(WolfmedArteryOverlay.Still),
                "a dressed neck is not the still one.");

            // Review: a stump whose bleed ran out loses its bleeding component and must read as still, not throw.
            foreach (var wound in SEntMan.System<WoundSystem>().GetWounds(torso))
            {
                if (SEntMan.HasComponent<WoundBleedingComponent>(wound))
                    Assert.That(bleeding.ReduceBleeding(wound.Owner, 1000), Is.True, "a torso bleed could not be run out.");
            }
            Assert.That(SEntMan.System<WoundSystem>().GetWounds(torso).Any(w => SEntMan.HasComponent<WoundBleedingComponent>(w)), Is.False,
                "a torso bleed survived running out.");
            visual = Refresh(body);
            Assert.Multiple(() =>
            {
                Assert.That(visual.Arteries.GetValueOrDefault(WolfmedArterySite.Neck), Is.EqualTo(WolfmedArteryOverlay.Still),
                    "a run-out neck is not the still one.");
                Assert.That(visual.Arteries.GetValueOrDefault(WolfmedArterySite.RArm), Is.EqualTo(WolfmedArteryOverlay.Still),
                    "a run-out arm stump is not the still one.");
                Assert.That(visual.Stumps.GetValueOrDefault(WolfmedArterySite.RArm), Is.EqualTo(WolfmedWoundOverlay.Old),
                    "a run-out arm stump still hangs a drip.");
            });
        });

        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var (clientBody, sprite) = ClientSprite(body);
            var sprites = CEntMan.System<SpriteSystem>();
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedArteryNeck", out var neck, false), Is.True,
                "the client never added the neck layer.");
            Assert.That(sprites.LayerGetRsiState((clientBody, sprite), neck).Name, Is.EqualTo("neck_artery0"));
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedArteryHead", out var artery, false), Is.True);
            Assert.That(sprites.TryGetLayer((clientBody, sprite), artery, out var layer, false) && layer.Visible, Is.False,
                "the head's artery is still drawn with the head off.");
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedArteryRHand", out var hand, false), Is.True);
            Assert.That(sprites.TryGetLayer((clientBody, sprite), hand, out var handLayer, false) && handLayer.Visible, Is.False,
                "the hand's artery is still drawn with the arm off.");
        });
    }

    /// <summary>
    /// Playtest 4: a limb off draws its stump at the joint: the flesh in the blood colour, the bone in its own, a
    /// trickle hung from it while it bleeds and nothing once dressed, under that site's artery; the torso stops showing
    /// a wound glyph for it. A head off is the neck's stump.
    /// </summary>
    [Test]
    public async Task StumpOverlayTest()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default;

        await Server.WaitAssertion(() =>
        {
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(SEntMan.System<AmputationSystem>().TryAmputate(body, Part(body, BodyPartType.Arm, BodyPartSymmetry.Left)), Is.True);
            var visual = Refresh(body);
            Assert.Multiple(() =>
            {
                Assert.That(visual.Stumps.GetValueOrDefault(WolfmedArterySite.LArm), Is.EqualTo(WolfmedWoundOverlay.Stream),
                    "a fresh arm stump does not trickle.");
                Assert.That(visual.Stumps, Has.Count.EqualTo(1), "a body with one limb off shows more than one stump.");
                Assert.That(visual.Wounds.ContainsKey(HumanoidVisualLayers.Chest), Is.False, "the torso draws the stump as a chest glyph.");
            });
        });

        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var (clientBody, sprite) = ClientSprite(body);
            var sprites = CEntMan.System<SpriteSystem>();
            var blood = CEntMan.GetComponent<PartDamageVisualsComponent>(clientBody).WoundColor;
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedStumpLArm", out var flesh, false), Is.True, "no stump layer.");
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedStumpBoneLArm", out var bone, false), Is.True, "no bone layer.");
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedStumpBleedLArm", out var bleed, false), Is.True, "no bleed layer.");
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedArteryLArm", out var artery, false), Is.True, "no artery layer.");
            Assert.Multiple(() =>
            {
                Assert.That(sprites.LayerGetRsiState((clientBody, sprite), flesh).Name, Is.EqualTo("l_arm_stump"));
                Assert.That(sprites.TryGetLayer((clientBody, sprite), flesh, out var fleshLayer, false) && fleshLayer.Visible, Is.True);
                Assert.That(fleshLayer!.Color, Is.EqualTo(blood), "the flesh is not the blood colour.");
                Assert.That(sprites.LayerGetRsiState((clientBody, sprite), bone).Name, Is.EqualTo("l_arm_stump_bone"));
                Assert.That(sprites.TryGetLayer((clientBody, sprite), bone, out var boneLayer, false) && boneLayer.Visible, Is.True);
                Assert.That(boneLayer!.Color, Is.EqualTo(Color.White), "the bone is tinted.");
                Assert.That(sprites.LayerGetRsiState((clientBody, sprite), bleed).Name, Is.EqualTo("l_arm_stump_stream"));
                Assert.That(sprites.TryGetLayer((clientBody, sprite), bleed, out var bleedLayer, false) && bleedLayer.Visible, Is.True);
                Assert.That(flesh, Is.LessThan(bone), "the bone must draw over the flesh.");
                Assert.That(bone, Is.LessThan(bleed), "the drip must draw over the bone.");
                Assert.That(bleed, Is.LessThan(artery), "the artery must draw over the drip.");
                Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "jumpsuit", out var jumpsuit, false) && flesh > jumpsuit, Is.True,
                    "a sleeve must not cover a missing arm.");
            });
        });

        await Server.WaitAssertion(() =>
        {
            var bleeding = SEntMan.System<WoundBleedingSystem>();
            Assert.That(bleeding.ReducePartBleeding(Part(body, BodyPartType.Torso), 1000, dressing: true), Is.True);
            Assert.That(Refresh(body).Stumps.GetValueOrDefault(WolfmedArterySite.LArm), Is.EqualTo(WolfmedWoundOverlay.Old),
                "a dressed stump still bleeds.");

            var head = Part(body, BodyPartType.Head);
            Assert.That(SEntMan.System<AmputationSystem>().TryAmputate(body, head), Is.True, "the head did not come off.");
            var visual = Refresh(body);
            Assert.Multiple(() =>
            {
                Assert.That(visual.Stumps.GetValueOrDefault(WolfmedArterySite.Neck), Is.EqualTo(WolfmedWoundOverlay.Stream), "the neck does not trickle.");
                Assert.That(visual.Stumps.ContainsKey(WolfmedArterySite.Head), Is.False, "a head site has a stump.");
            });
        });

        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var (clientBody, sprite) = ClientSprite(body);
            var sprites = CEntMan.System<SpriteSystem>();
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedStumpBleedLArm", out var bleed, false), Is.True);
            Assert.That(sprites.TryGetLayer((clientBody, sprite), bleed, out var bleedLayer, false) && bleedLayer.Visible, Is.False,
                "a dressed stump still shows its drip.");
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedStumpLArm", out var flesh, false), Is.True);
            Assert.That(sprites.TryGetLayer((clientBody, sprite), flesh, out var fleshLayer, false) && fleshLayer.Visible, Is.True,
                "a dressed stump lost its art.");
            Assert.That(sprites.LayerMapTryGet((clientBody, sprite), "WolfmedStumpNeck", out var neck, false), Is.True, "no neck stump.");
            Assert.That(sprites.LayerGetRsiState((clientBody, sprite), neck).Name, Is.EqualTo("neck_stump"));
        });
    }

    /// <summary>Every state the overlays can select exists in its RSI, and the offset table names only real species.</summary>
    [Test]
    public async Task OverlayArtCoversEveryLayerTest()
    {
        var cache = Client.ResolveDependency<IResourceCache>();
        await Client.WaitAssertion(() =>
        {
            Assert.That(cache.TryGetResource<RSIResource>(new ResPath("/Textures") / WolfmedWoundOverlays.WoundRsi, out var wounds), Is.True);
            Assert.That(cache.TryGetResource<RSIResource>(new ResPath("/Textures") / WolfmedWoundOverlays.RotRsi, out var rot), Is.True);
            Assert.That(cache.TryGetResource<RSIResource>(new ResPath("/Textures") / WolfmedWoundOverlays.ArteryRsi, out var artery), Is.True);
            Assert.That(cache.TryGetResource<RSIResource>(new ResPath("/Textures") / WolfmedWoundOverlays.StumpRsi, out var stumps), Is.True);
            Assert.That(CProtoMan.HasIndex<WolfmedOverlayOffsetsPrototype>(WolfmedOverlayOffsetsPrototype.Default), Is.True);
            Assert.Multiple(() =>
            {
                foreach (var layer in WolfmedDegradationLayers.All)
                {
                    foreach (var look in Enum.GetValues<WolfmedWoundOverlay>())
                    {
                        if (look == WolfmedWoundOverlay.None)
                            continue;

                        var state = WolfmedWoundOverlays.GetWoundState(layer, look);
                        Assert.That(state != null && wounds!.RSI.TryGetState(state, out _), Is.True, $"no wound state {state}.");
                    }

                    var rotState = WolfmedWoundOverlays.GetRotState(layer);
                    Assert.That(rotState != null && rot!.RSI.TryGetState(rotState, out _), Is.True, $"no rot state {rotState}.");
                }

                foreach (var site in Enum.GetValues<WolfmedArterySite>())
                {
                    foreach (var look in Enum.GetValues<WolfmedArteryOverlay>())
                    {
                        if (look == WolfmedArteryOverlay.None)
                            continue;

                        var state = WolfmedWoundOverlays.GetArteryState(site, look);
                        Assert.That(state != null && artery!.RSI.TryGetState(state, out _), Is.True, $"no artery state {state}.");
                    }

                    if (site == WolfmedArterySite.Head)
                        continue;

                    foreach (var state in new[]
                             {
                                 WolfmedWoundOverlays.GetStumpState(site),
                                 WolfmedWoundOverlays.GetStumpBoneState(site),
                                 WolfmedWoundOverlays.GetStumpBleedState(site, WolfmedWoundOverlay.Drip),
                                 WolfmedWoundOverlays.GetStumpBleedState(site, WolfmedWoundOverlay.Stream),
                             })
                        Assert.That(state != null && stumps!.RSI.TryGetState(state, out _), Is.True, $"no stump state {state}.");
                }
            });
        });
    }

    private PartDamageVisualsComponent Refresh(EntityUid body)
    {
        SEntMan.System<WolfmedWoundOverlaySystem>().Refresh(body);
        return SEntMan.GetComponent<PartDamageVisualsComponent>(body);
    }

    private (EntityUid, SpriteComponent) ClientSprite(EntityUid serverBody)
    {
        var clientBody = ToClientUid(serverBody);
        return (clientBody, CEntMan.GetComponent<SpriteComponent>(clientBody));
    }

    private EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        return SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }
}
