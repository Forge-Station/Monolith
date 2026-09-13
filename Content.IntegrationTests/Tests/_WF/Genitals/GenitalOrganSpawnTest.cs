using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Genitals;
using Content.Server.Humanoid.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Organs are built from the profile once the owner's master consent is known, and the body mirror follows them.</summary>
[TestFixture]
[TestOf(typeof(GenitalOrganSystem))]
public sealed class GenitalOrganSpawnTest
{
    /// <summary>Consent, then the profile load: five slots, five organs with skin-resolved colours and a matching mirror. A second load duplicates nothing.</summary>
    [Test]
    public async Task BuildTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid mob = default;
        await server.WaitPost(() => mob = SpawnWithAnatomy(entMan, map.MapCoords));

        await server.WaitAssertion(() =>
        {
            AssertFullAnatomy(entMan, mob);

            var before = GenitalOrgans(entMan, mob).Select(o => o.Owner).ToList();
            entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(mob, MakeProfile().WithGenitals(FullProfile()));

            Assert.That(GenitalOrgans(entMan, mob).Select(o => o.Owner), Is.EquivalentTo(before),
                "A second load rebuilt or duplicated organs.");
            AssertFullAnatomy(entMan, mob);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Without consent the load creates the component and slots but no organs. Consent builds them; master off empties the mirror and keeps the organs.</summary>
    [Test]
    public async Task ConsentTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entMan.Spawn("MobHuman", map.MapCoords);
            entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(mob, MakeProfile().WithGenitals(FullProfile()));
        });

        await server.WaitAssertion(() =>
        {
            var genitals = entMan.GetComponent<GenitalsComponent>(mob);
            Assert.Multiple(() =>
            {
                Assert.That(HasAllSlots(entMan, mob), "Slots are created at the first load.");
                Assert.That(GenitalOrgans(entMan, mob), Is.Empty, "Organs wait for the owner's consent.");
                Assert.That(genitals.OrgansBuilt, Is.False);
                Assert.That(genitals.SourceProfile, Is.Not.Null);
                AssertMirrorEmpty(genitals);
            });

            GrantConsent(entMan, mob);
            AssertFullAnatomy(entMan, mob);

            // Master off: the organs stay in the body, only the mirror empties.
            RevokeConsent(entMan, mob);
            Assert.Multiple(() =>
            {
                Assert.That(GenitalOrgans(entMan, mob), Has.Count.EqualTo(5));
                AssertMirrorEmpty(genitals);
            });

            GrantConsent(entMan, mob);
            AssertFullAnatomy(entMan, mob);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A profile load before MapInit, like the ComponentInit default load, creates nothing.</summary>
    [Test]
    public async Task LoadBeforeMapInitTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap(false);

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entMan.Spawn("MobHuman", map.MapCoords);
            GrantConsent(entMan, mob);
            entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(mob, MakeProfile().WithGenitals(FullProfile()));
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<MetaDataComponent>(mob).EntityLifeStage, Is.LessThan(EntityLifeStage.MapInitialized));
            Assert.That(entMan.HasComponent<GenitalsComponent>(mob), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The DNA scrambler loads a random profile: organs and template stay, skin-matched colours follow the new skin, custom colours stay.</summary>
    [Test]
    public async Task DnaScramblerTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var anatomy = GenitalProfile.Empty
            .WithPenis(new PenisProfile(PenisKnotted, 20, SheathType.Sheath, matchSkin: false, color: Color.FromHex("#C28A8A")))
            .WithTesticles(new TesticlesProfile(TesticleType.External, 2))
            .WithBreasts(new BreastsProfile(BreastsPair, 3));

        EntityUid mob = default;
        await server.WaitPost(() => mob = SpawnWithAnatomy(entMan, map.MapCoords, anatomy));

        await server.WaitAssertion(() =>
        {
            var genitals = entMan.GetComponent<GenitalsComponent>(mob);
            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(mob);
            var organsBefore = GenitalOrgans(entMan, mob).Select(o => o.Owner).ToList();
            var template = genitals.SourceProfile;
            Assert.That(organsBefore, Has.Count.EqualTo(3));
            Assert.That(genitals.Penis, Is.Not.Null);
            var customColor = genitals.Penis!.Value.Color;

            // SubdermalImplantSystem: LoadProfile(RandomWithSpecies(species)).
            var random = HumanoidCharacterProfile.RandomWithSpecies(humanoid.Species);
            entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(mob, random, humanoid);
            var skin = humanoid.SkinColor;

            Assert.Multiple(() =>
            {
                Assert.That(GenitalOrgans(entMan, mob).Select(o => o.Owner), Is.EquivalentTo(organsBefore));
                Assert.That(genitals.SourceProfile, Is.SameAs(template));
                Assert.That(genitals.Penis?.Color, Is.EqualTo(customColor), "A custom colour was re-tinted.");
                Assert.That(genitals.Penis?.SheathColor, Is.EqualTo(skin), "The skin-matched sheath did not follow the new skin.");
                Assert.That(genitals.Testicles?.Color, Is.EqualTo(skin));
                Assert.That(genitals.Breasts?.Color, Is.EqualTo(skin));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Random humanoids (event spawns, NPCs) never carry anatomy.</summary>
    [Test]
    public async Task RandomHumanoidTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mob = entMan.System<RandomHumanoidSystem>().SpawnRandomHumanoid("EventHumanoid", map.GridCoords, "anatomy test");
            Assert.That(entMan.HasComponent<GenitalsComponent>(mob), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Minors and excluded species never get anatomy or slots; a later load with an ineligible age removes it.</summary>
    [Test]
    public async Task EligibilityTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid minor = default;
        EntityUid ipc = default;
        EntityUid adult = default;
        await server.WaitPost(() =>
        {
            var humanoid = entMan.System<SharedHumanoidAppearanceSystem>();

            minor = entMan.Spawn("MobHuman", map.MapCoords);
            humanoid.LoadProfile(minor, MakeProfile(age: 17).WithGenitals(FullProfile()));
            GrantConsent(entMan, minor);

            ipc = entMan.Spawn("MobIPC", map.MapCoords);
            humanoid.LoadProfile(ipc, MakeProfile("IPC").WithGenitals(FullProfile()));
            GrantConsent(entMan, ipc);

            adult = SpawnWithAnatomy(entMan, map.MapCoords);
        });

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<GenitalsComponent>(minor), Is.False, "Age 17 got anatomy.");
                Assert.That(HasAnySlot(entMan, minor), Is.False, "Age 17 got slots.");
                Assert.That(entMan.HasComponent<GenitalsComponent>(ipc), Is.False, "An adult IPC got anatomy.");
                Assert.That(HasAnySlot(entMan, ipc), Is.False, "An adult IPC got slots.");
                Assert.That(GenitalOrgans(entMan, adult), Has.Count.EqualTo(5));
            });

            entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(adult, MakeProfile(age: 17).WithGenitals(FullProfile()));

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<GenitalsComponent>(adult), Is.False, "A later ineligible load kept the anatomy.");
                Assert.That(GenitalOrgans(entMan, adult), Is.Empty, "A later ineligible load kept the organs.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>With the kill switch off nothing is built and every mirror empties; switching it back on builds what waited and restores the mirrors.</summary>
    [Test]
    public async Task KillSwitchTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid built = default;
        EntityUid waiting = default;
        await server.WaitPost(() =>
        {
            built = SpawnWithAnatomy(entMan, map.MapCoords);
            waiting = entMan.Spawn("MobHuman", map.MapCoords);
            entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(waiting, MakeProfile().WithGenitals(FullProfile()));
        });

        try
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WolfgateCVars.AnatomyEnabled, false));
            await server.WaitAssertion(() =>
            {
                // Consent while the switch is off builds nothing.
                GrantConsent(entMan, waiting);
                Assert.Multiple(() =>
                {
                    Assert.That(GenitalOrgans(entMan, built), Has.Count.EqualTo(5), "The organs stay in the body.");
                    AssertMirrorEmpty(entMan.GetComponent<GenitalsComponent>(built));
                    Assert.That(GenitalOrgans(entMan, waiting), Is.Empty, "Organs were built with the kill switch off.");
                    AssertMirrorEmpty(entMan.GetComponent<GenitalsComponent>(waiting));
                });
            });

            await server.WaitPost(() => server.CfgMan.SetCVar(WolfgateCVars.AnatomyEnabled, true));
            await server.WaitAssertion(() =>
            {
                AssertFullAnatomy(entMan, built);
                AssertFullAnatomy(entMan, waiting);
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WolfgateCVars.AnatomyEnabled, true));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>EnsureAnatomy dirties the torso, so the client sees the runtime slots in BodyPartComponent.Organs.</summary>
    [Test]
    public async Task ClientSeesSlotsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var (server, client) = (pair.Server, pair.Client);
        var map = await pair.CreateTestMap();

        EntityUid torso = default;
        await server.WaitPost(() =>
        {
            var mob = server.EntMan.Spawn("MobHuman", map.MapCoords);
            server.EntMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(mob, MakeProfile().WithGenitals(FullProfile()));
            torso = RootPart(server.EntMan, mob) ?? EntityUid.Invalid;
        });

        await pair.RunTicksSync(10);
        Assert.That(torso.IsValid(), "The mob has no root part.");
        var clientTorso = pair.ToClientUid(torso);

        await client.WaitAssertion(() =>
        {
            var part = client.EntMan.GetComponent<BodyPartComponent>(clientTorso);
            Assert.Multiple(() =>
            {
                foreach (var slot in GenitalOrganSystem.Slots)
                {
                    Assert.That(part.Organs.ContainsKey(GenitalOrganSystem.SlotId(slot)), $"The client lacks the {slot} slot.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Five slots, one organ per slot with skin-resolved colours (FullProfile matches skin everywhere), and a mirror equal to the organs.</summary>
    private static void AssertFullAnatomy(IEntityManager entMan, EntityUid mob)
    {
        var genitals = entMan.GetComponent<GenitalsComponent>(mob);
        var skin = entMan.GetComponent<HumanoidAppearanceComponent>(mob).SkinColor;
        var organs = GenitalOrgans(entMan, mob);

        Assert.Multiple(() =>
        {
            Assert.That(HasAllSlots(entMan, mob), "The torso lacks a genital slot.");
            Assert.That(organs, Has.Count.EqualTo(5));
            Assert.That(genitals.OrgansBuilt);
            Assert.That(genitals.SourceProfile, Is.Not.Null);

            Assert.That(genitals.Penis, Is.Not.Null);
            Assert.That(genitals.Testicles, Is.Not.Null);
            Assert.That(genitals.Vagina, Is.Not.Null);
            Assert.That(genitals.Breasts, Is.Not.Null);
            Assert.That(genitals.Womb, Is.True);

            Assert.That(genitals.Penis, Is.EqualTo(OrganState(organs, GenitalSlot.Penis)));
            Assert.That(genitals.Testicles, Is.EqualTo(OrganState(organs, GenitalSlot.Testicles)));
            Assert.That(genitals.Vagina, Is.EqualTo(OrganState(organs, GenitalSlot.Vagina)));
            Assert.That(genitals.Breasts, Is.EqualTo(OrganState(organs, GenitalSlot.Breasts)));

            Assert.That(genitals.Penis?.Sheath, Is.EqualTo(SheathType.Sheath));
            Assert.That(genitals.Penis?.Color, Is.EqualTo(skin));
            Assert.That(genitals.Penis?.SheathColor, Is.EqualTo(skin));
            Assert.That(genitals.Testicles?.Color, Is.EqualTo(skin));
            Assert.That(genitals.Vagina?.Color, Is.EqualTo(skin));
            Assert.That(genitals.Breasts?.Color, Is.EqualTo(skin));
        });
    }
}
