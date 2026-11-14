#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Damage;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Damage;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 5: "some species don't have their stump/bleed overlays". Every organic round-start species gets the
/// stump, its drip and its artery for an arm off, on the server and on the client, and the chest glyph for a slash
/// where its flesh takes a slash (a slime or a plant takes its own wounds, a machine takes none of this by design).
/// </summary>
[TestFixture]
public sealed class WolfmedSpeciesOverlayTest : GameTest
{
    [Test]
    public async Task EverySpeciesDrawsWoundsAndStumpsTest()
    {
        var map = await Pair.CreateTestMap();
        var species = SProtoMan.EnumeratePrototypes<SpeciesPrototype>()
            .Where(s => s.RoundStart)
            .OrderBy(s => s.ID)
            .ToList();
        Assert.That(species, Is.Not.Empty);

        var bodies = new Dictionary<string, EntityUid>();
        var slashed = new HashSet<string>();
        var problems = new List<string>();
        await Server.WaitPost(() =>
        {
            foreach (var s in species)
            {
                if (!SProtoMan.HasIndex<EntityPrototype>(s.Prototype))
                {
                    problems.Add($"{s.ID}: no mob prototype {s.Prototype}");
                    continue;
                }

                var uid = SEntMan.SpawnEntity(s.Prototype, map.GridCoords);
                // The species' default markings: the fur, scales or feathers that covered the wounds in play.
                SEntMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(uid, HumanoidCharacterProfile.DefaultWithSpecies(s.ID));
                bodies[s.ID] = uid;
            }
        });
        await RunSeconds(2);

        await Server.WaitPost(() =>
        {
            var wounds = SEntMan.System<WoundSystem>();
            var amputation = SEntMan.System<AmputationSystem>();
            var body = SEntMan.System<SharedBodySystem>();
            var overlays = SEntMan.System<WolfmedWoundOverlaySystem>();
            var organic = SEntMan.System<Content.Shared._WF.Wolfmed.Sounds.WolfmedOrganicSoundSystem>();
            foreach (var (id, uid) in bodies.ToList())
            {
                // A machine draws none of this: its chassis visuals are its own.
                if (!organic.IsOrganicBody(uid))
                {
                    bodies.Remove(id);
                    continue;
                }

                var parts = body.GetBodyChildren(uid).ToList();
                var torso = parts.FirstOrDefault(p => p.Component.PartType == BodyPartType.Torso).Id;
                var arm = parts.FirstOrDefault(p => p.Component.PartType == BodyPartType.Arm && p.Component.Symmetry == BodyPartSymmetry.Left).Id;
                if (torso == default || arm == default)
                {
                    problems.Add($"{id}: no torso or left arm ({parts.Count} parts)");
                    continue;
                }

                if (wounds.CreateOrMergeWound(torso, "SlashWound", FixedPoint2.New(60)) != null)
                    slashed.Add(id);
                if (!amputation.TryAmputate(uid, arm))
                    problems.Add($"{id}: the arm did not come off");

                overlays.Refresh(uid);
                var visual = SEntMan.GetComponent<PartDamageVisualsComponent>(uid);
                if (slashed.Contains(id) &&
                    visual.Wounds.GetValueOrDefault(Content.Shared.Humanoid.HumanoidVisualLayers.Chest) == WolfmedWoundOverlay.None)
                    problems.Add($"{id}: server shows no chest wound glyph");
                if (visual.Stumps.GetValueOrDefault(WolfmedArterySite.LArm) == WolfmedWoundOverlay.None)
                    problems.Add($"{id}: server shows no arm stump");
                if (visual.Arteries.GetValueOrDefault(WolfmedArterySite.LArm) == WolfmedArteryOverlay.None)
                    problems.Add($"{id}: server shows no arm artery");
            }
        });

        await RunTicksSync(15);
        await Client.WaitPost(() =>
        {
            var sprites = CEntMan.System<SpriteSystem>();
            var visuals = CEntMan.System<Content.Client.Damage.DamageVisualsSystem>();
            foreach (var (id, uid) in bodies)
            {
                var client = ToClientUid(uid);
                if (!CEntMan.TryGetComponent(client, out SpriteComponent? sprite))
                {
                    problems.Add($"{id}: no client sprite");
                    continue;
                }

                // The chest glyph must sit over the chest's markings, or the fur hides it.
                var markingTop = visuals.MarkingTop(client, sprite, Content.Shared.Humanoid.HumanoidVisualLayers.Chest);
                if (slashed.Contains(id) && sprites.LayerMapTryGet((client, sprite), "WolfmedWoundChest", out var chest, false) &&
                    chest <= markingTop)
                    problems.Add($"{id}: client chest wound glyph ({chest}) is under a marking ({markingTop})");

                foreach (var (key, what) in new[]
                         {
                             ("WolfmedWoundChest", "chest wound glyph"),
                             ("WolfmedStumpLArm", "arm stump"),
                             ("WolfmedStumpBleedLArm", "arm stump drip"),
                             ("WolfmedArteryLArm", "arm artery"),
                         })
                {
                    if (key == "WolfmedWoundChest" && !slashed.Contains(id))
                        continue;

                    if (!sprites.LayerMapTryGet((client, sprite), key, out var index, false))
                        problems.Add($"{id}: client has no {what} layer");
                    else if (!sprites.TryGetLayer((client, sprite), index, out var layer, false) || !layer.Visible)
                        problems.Add($"{id}: client {what} layer is hidden");
                }
            }
        });

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }
}
