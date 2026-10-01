#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Chat.Systems;
using Content.Server.Nutrition.Components;
using Content.Server._WF.Wolfmed.Sounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Sounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 4, SOUNDS: the Bobmed sound pack. The files are where the collections say, the gasp is the owner's, the
/// Wolfmed emotes find a voice by sex, a melee hit on flesh stabs or thuds, a fracture cracks, a tendon snaps, a light
/// bleed drips, and none of it happens to a chassis.
/// </summary>
/// <remarks>
/// A test cannot hear, so a sound counts as played when its audio entity exists on the body, and the drip, which is
/// over before the next tick, by the time its clock stamped.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedBodySoundSystem))]
public sealed class WolfmedBobSoundsTest : GameTest
{
    private const string GaspDir = "/Audio/_WF/Wolfmed/Gasp/";

    /// <summary>Every Wolfmed collection's files exist, the gasp pair is exactly the owner's, and pills swallow.</summary>
    [Test]
    public async Task CollectionsAndPillTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ProtoMan;
        var resources = server.ResolveDependency<IResourceManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var collections = prototypes.EnumeratePrototypes<SoundCollectionPrototype>()
                .Where(proto => proto.ID.StartsWith("WFWolfmed"))
                .ToList();
            Assert.That(collections, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                foreach (var collection in collections)
                {
                    Assert.That(collection.PickFiles, Is.Not.Empty, $"{collection.ID} has no files.");
                    foreach (var file in collection.PickFiles)
                        Assert.That(resources.ContentFileExists(file), Is.True, $"{collection.ID}: {file} is missing.");
                }

                Assert.That(Files(prototypes, "MaleGasp"),
                    Is.EquivalentTo(Enumerable.Range(1, 7).Select(i => $"{GaspDir}gasp_male{i}.ogg")));
                Assert.That(Files(prototypes, "FemaleGasp"),
                    Is.EquivalentTo(Enumerable.Range(1, 13).Select(i => $"{GaspDir}gasp_female{i}.ogg")));

                // The upstream Pill and a variant that inherits from it.
                foreach (var pill in new[] { "Pill", "PillDexalin" })
                {
                    Assert.That(prototypes.Index<EntityPrototype>(pill).TryGetComponent(out FoodComponent? food, factory),
                        Is.True);
                    Assert.That(food!.UseSound, Is.InstanceOf<SoundCollectionSpecifier>());
                    var id = ((SoundCollectionSpecifier) food.UseSound).Collection!;
                    Assert.That(id, Is.EqualTo("WFWolfmedPillSwallow"), $"{pill} swallows with something else.");
                    Assert.That(Files(prototypes, id), Is.EqualTo(new[] { "/Audio/_WF/Wolfmed/Pill/pill_swallow.ogg" }));
                    Assert.That(food.EatMessage.Id, Is.EqualTo("food-swallow"));
                }
            });
        });
    }

    /// <summary>
    /// A Wolfmed emote finds its voice by sex as the gasp does, and plays it; a chassis makes none of the body noises.
    /// </summary>
    [Test]
    public async Task EmotesVoiceBySexAndOnlyForFleshTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var sounds = entities.System<WolfmedBodySoundSystem>();
            var chat = entities.System<ChatSystem>();

            var male = entities.SpawnEntity("MobHuman", map.GridCoords);
            var female = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.System<SharedHumanoidAppearanceSystem>().SetSex(female, Sex.Female);
            entities.System<SharedHumanoidAppearanceSystem>().SetSex(male, Sex.Male);
            var ipc = entities.SpawnEntity("MobIPC", map.GridCoords);

            Assert.Multiple(() =>
            {
                // The gasp comes from the species set, the sneeze from the Wolfmed one; both by the same sex.
                Assert.That(Collection(sounds.GetEmoteSound(male, "Gasp")), Is.EqualTo("MaleGasp"));
                Assert.That(Collection(sounds.GetEmoteSound(female, "Gasp")), Is.EqualTo("FemaleGasp"));
                Assert.That(Collection(sounds.GetEmoteSound(male, "WFWolfmedSneeze")), Is.EqualTo("WFWolfmedSneezeMale"));
                Assert.That(Collection(sounds.GetEmoteSound(female, "WFWolfmedSneeze")),
                    Is.EqualTo("WFWolfmedSneezeFemale"));

                Assert.That(sounds.GetEmoteSound(ipc, "WFWolfmedSneeze"), Is.Null, "a chassis does not sneeze.");
                Assert.That(sounds.GetEmoteSound(ipc, "Gasp"), Is.Null, "or gasp.");
            });

            chat.TryEmoteWithoutChat(male, "WFWolfmedSneeze", ignoreActionBlocker: true);
            chat.TryEmoteWithoutChat(female, "WFWolfmedSneeze", ignoreActionBlocker: true);
            chat.TryEmoteWithoutChat(ipc, "WFWolfmedSneeze", ignoreActionBlocker: true);

            Assert.Multiple(() =>
            {
                Assert.That(Played(entities, male, "/Audio/_WF/Wolfmed/Emotes/sneeze_male"), Is.EqualTo(1));
                Assert.That(Played(entities, female, "/Audio/_WF/Wolfmed/Emotes/sneeze_female"), Is.EqualTo(1));
                Assert.That(Played(entities, ipc, "/Audio/_WF/Wolfmed/Emotes/"), Is.Zero,
                    "the same emote from an IPC is silent.");
            });
        });
    }

    /// <summary>
    /// The hit sound on flesh: a held weapon's Piercing hit stabs, a Blunt or Slash hit from a weapon with no sound of
    /// its own gets the standard melee sound, and everything else keeps what it had. A chassis and a wall change nothing.
    /// </summary>
    [Test]
    public async Task MeleeHitPicksStabOrMeleeTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var stab = WolfmedOrganicSoundSystem.StabCollection;
            var melee = WolfmedOrganicSoundSystem.MeleeCollection;

            Assert.Multiple(() =>
            {
                Assert.That(WolfmedOrganicSoundSystem.PickCollection(Damage(("Piercing", 15)), true, true), Is.EqualTo(stab),
                    "a spear stabs, whatever its own sound.");
                Assert.That(WolfmedOrganicSoundSystem.PickCollection(Damage(("Piercing", 10), ("Blunt", 4)), true, false),
                    Is.EqualTo(stab), "mostly Piercing is a stab.");
                Assert.That(WolfmedOrganicSoundSystem.PickCollection(Damage(("Piercing", 8)), false, true), Is.Null,
                    "a bite keeps its bite.");
                Assert.That(WolfmedOrganicSoundSystem.PickCollection(Damage(("Blunt", 10)), true, false), Is.EqualTo(melee));
                Assert.That(WolfmedOrganicSoundSystem.PickCollection(Damage(("Slash", 12), ("Piercing", 3)), true, false),
                    Is.EqualTo(melee));
                Assert.That(WolfmedOrganicSoundSystem.PickCollection(Damage(("Blunt", 10)), true, true), Is.Null,
                    "a weapon with its own hit sound keeps it.");
                Assert.That(WolfmedOrganicSoundSystem.PickCollection(Damage(("Heat", 10), ("Blunt", 2)), true, false),
                    Is.Null, "a burn is not a thud.");
                Assert.That(WolfmedOrganicSoundSystem.PickCollection(new DamageSpecifier(), true, false), Is.Null,
                    "no damage keeps the no-damage sound.");
            });

            var organic = entities.System<WolfmedOrganicSoundSystem>();
            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            var ipc = entities.SpawnEntity("MobIPC", map.GridCoords);
            var wall = entities.SpawnEntity("WallSolid", map.GridCoords);
            var attacker = entities.SpawnEntity("MobHuman", map.GridCoords);
            var weapon = entities.SpawnEntity("Crowbar", map.GridCoords);
            var soundless = new MeleeWeaponComponent();

            Assert.Multiple(() =>
            {
                Assert.That(organic.IsOrganicBody(human), Is.True);
                Assert.That(organic.IsOrganicBody(ipc), Is.False);
                Assert.That(organic.IsOrganicBody(wall), Is.False);

                Assert.That(Collection(organic.GetHitSound(human, Damage(("Blunt", 10)), weapon, attacker, soundless)),
                    Is.EqualTo((string) melee));
                Assert.That(Collection(organic.GetHitSound(human, Damage(("Piercing", 10)), weapon, attacker, soundless)),
                    Is.EqualTo((string) stab));
                Assert.That(organic.GetHitSound(ipc, Damage(("Blunt", 10)), weapon, attacker, soundless), Is.Null,
                    "the same hit on an IPC keeps today's sound.");
                Assert.That(organic.GetHitSound(ipc, Damage(("Piercing", 10)), weapon, attacker, soundless), Is.Null);
                Assert.That(organic.GetHitSound(wall, Damage(("Blunt", 10)), weapon, attacker, soundless), Is.Null,
                    "and so does a wall.");
            });
        });
    }

    /// <summary>A new fracture cracks and a cut tendon snaps, on flesh; the same injuries on a chassis are silent.</summary>
    [Test]
    public async Task FractureCracksAndTendonSnapsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var routing = entities.System<WoundDamageRoutingSystem>();
            var fractures = entities.System<WoundFractureSystem>();
            var wounds = entities.System<WoundSystem>();

            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            var ipc = entities.SpawnEntity("MobIPC", map.GridCoords);
            var humanArm = Part(entities, human, BodyPartType.Arm, BodyPartSymmetry.Left);
            var ipcArm = Part(entities, ipc, BodyPartType.Arm, BodyPartSymmetry.Left);

            // 75 Blunt is the deterministic Comminuted fracture (P2-D23).
            Assert.That(routing.TryApplyPartDamage(human, humanArm, Damage(("Blunt", 75))), Is.True);
            routing.TryApplyPartDamage(ipc, ipcArm, Damage(("Blunt", 75)));
            Assert.That(fractures.GetFracture(humanArm), Is.Not.Null, "the fixture needs a fracture.");

            Assert.Multiple(() =>
            {
                Assert.That(Played(entities, human, "/Audio/_WF/Wolfmed/Bone/crack"), Is.EqualTo(1),
                    "one crack for the break.");
                Assert.That(Played(entities, ipc, "/Audio/_WF/Wolfmed/Bone/crack"), Is.Zero, "a chassis does not crack.");
            });

            var humanLeg = Part(entities, human, BodyPartType.Leg, BodyPartSymmetry.Left);
            var ipcLeg = Part(entities, ipc, BodyPartType.Leg, BodyPartSymmetry.Left);
            Assert.That(wounds.CreateOrMergeWound(humanLeg, WolfmedBodySoundSystem.TendonWound, FixedPoint2.New(20)),
                Is.Not.Null);
            wounds.CreateOrMergeWound(ipcLeg, WolfmedBodySoundSystem.TendonWound, FixedPoint2.New(20));

            Assert.Multiple(() =>
            {
                Assert.That(Played(entities, human, "/Audio/_WF/Wolfmed/Bone/tendon_snap"), Is.EqualTo(1));
                Assert.That(Played(entities, ipc, "/Audio/_WF/Wolfmed/Bone/tendon_snap"), Is.Zero);
            });
        });
    }

    /// <summary>
    /// A light bleed drips within two intervals; a spurting one does not, and neither does a chassis leaking at the
    /// same light rate.
    /// </summary>
    [Test]
    public async Task LightBleedDripsTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.DripSoundInterval, 1f);

        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid light = default, heavy = default, ipc = default;
        var start = TimeSpan.Zero;

        await server.WaitAssertion(() =>
        {
            var drips = entities.System<WolfmedBleedDripSystem>();
            var wounds = entities.System<WoundSystem>();
            start = server.Timing.CurTime;

            light = entities.SpawnEntity("MobHuman", map.GridCoords);
            heavy = entities.SpawnEntity("MobHuman", map.GridCoords);
            ipc = entities.SpawnEntity("MobIPC", map.GridCoords);

            // SlashWound bleeds from severity 9.
            Assert.That(wounds.CreateOrMergeWound(Part(entities, light, BodyPartType.Arm, BodyPartSymmetry.Left),
                "SlashWound", FixedPoint2.New(12)), Is.Not.Null);
            Assert.That(wounds.CreateOrMergeWound(Part(entities, heavy, BodyPartType.Arm, BodyPartSymmetry.Left),
                "WFWolfmedArterialBleedWound", FixedPoint2.New(20)), Is.Not.Null);
            wounds.CreateOrMergeWound(Part(entities, ipc, BodyPartType.Arm, BodyPartSymmetry.Left),
                "SlashWound", FixedPoint2.New(12));

            var rate = drips.ExternalBleedRate(light);
            Assert.That(rate, Is.GreaterThan(0f).And.LessThan(server.CfgMan.GetCVar(WolfmedCVars.DripSoundBelow)),
                "the fixture's cut must be a light bleed.");

            foreach (var body in new[] { light, heavy, ipc })
                drips.Refresh(body);
        });

        await RunSeconds(2.2f);

        await server.WaitAssertion(() =>
        {
            var drips = entities.System<WolfmedBleedDripSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(entities.TryGetComponent(light, out WolfmedBleedDripComponent? drip), Is.True);
                Assert.That(drip!.LastDrip, Is.GreaterThan(start), "a light bleed drips within two intervals.");

                Assert.That(drips.HasDripSource(heavy), Is.False, "an arterial bleed spurts instead.");
                Assert.That(entities.HasComponent<WolfmedBleedDripComponent>(heavy), Is.False);

                Assert.That(drips.HasDripSource(ipc), Is.False, "a chassis does not drip.");
                Assert.That(entities.HasComponent<WolfmedBleedDripComponent>(ipc), Is.False);
                Assert.That(drips.TryDrip(ipc), Is.False);
            });
        });
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type, BodyPartSymmetry symmetry) =>
        entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;

    private static DamageSpecifier Damage(params (string Type, int Amount)[] types)
    {
        var damage = new DamageSpecifier();
        foreach (var (type, amount) in types)
            damage.DamageDict[type] = FixedPoint2.New(amount);

        return damage;
    }

    private static string? Collection(SoundSpecifier? sound) => (sound as SoundCollectionSpecifier)?.Collection;

    private static IEnumerable<string> Files(IPrototypeManager prototypes, string collection) =>
        prototypes.Index<SoundCollectionPrototype>(collection).PickFiles.Select(file => file.ToString());

    /// <summary>Audio entities on this body whose file starts with the prefix.</summary>
    private static int Played(IEntityManager entities, EntityUid body, string prefix)
    {
        var count = 0;
        var query = entities.EntityQueryEnumerator<AudioComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var audio, out var xform))
        {
            var file = audio.FileName;
            if (!entities.IsQueuedForDeletion(uid) && xform.ParentUid == body && file.StartsWith(prefix))
                count++;
        }

        return count;
    }
}
