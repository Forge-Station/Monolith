using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Genitals;
using Content.Server.Polymorph.Systems;
using Content.Shared._NF.Cloning;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Cloning;
using Content.Shared.Humanoid;
using Content.Shared.Polymorph;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Cloning copies the genetic template; polymorph moves the current organs and runtime state to the new body.</summary>
[TestFixture]
[TestOf(typeof(GenitalOrganSystem))]
public sealed class GenitalCloningPolymorphTest
{
    /// <summary>MobHuman with transferHumanoidAppearance (Polymorphs/polymorph.yml).</summary>
    private const string TestHumanMorph = "TestHumanMorph";

    /// <summary>Follows CloningSystem's order. The clone's organs wait for its owner's consent, and a removed organ grows back.</summary>
    [Test]
    public async Task CloningTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var serialization = server.ResolveDependency<ISerializationManager>();
        var map = await pair.CreateTestMap();

        EntityUid source = default;
        EntityUid clone = default;
        await server.WaitPost(() =>
        {
            source = SpawnWithAnatomy(entMan, map.MapCoords);

            // The source loses its penis; the genetic template keeps it.
            if (GenitalOrgan(entMan, source, GenitalSlot.Penis) is { } penis)
            {
                entMan.System<SharedBodySystem>().RemoveOrgan(penis);
                entMan.DeleteEntity(penis);
            }
        });

        await server.WaitPost(() =>
        {
            // CloningSystem: spawn the species prototype, CloneAppearance, copy ITransferredByCloning, raise CloningEvent on the source.
            clone = entMan.Spawn("MobHuman", map.MapCoords);
            entMan.System<SharedHumanoidAppearanceSystem>().CloneAppearance(source, clone);

            foreach (var comp in entMan.GetComponents(source).ToList())
            {
                if (comp is not ITransferredByCloning)
                    continue;

                var copy = serialization.CreateCopy(comp, notNullableOverride: true);
                entMan.AddComponent(clone, copy, overwrite: true);
            }

            var ev = new CloningEvent(source, clone);
            entMan.EventBus.RaiseLocalEvent(source, ref ev);
        });

        await server.WaitAssertion(() =>
        {
            var sourceGenitals = entMan.GetComponent<GenitalsComponent>(source);
            var cloneGenitals = entMan.GetComponent<GenitalsComponent>(clone);

            Assert.Multiple(() =>
            {
                Assert.That(sourceGenitals.Penis, Is.Null, "The source's penis was not removed.");
                Assert.That(cloneGenitals.SourceProfile, Is.Not.Null);
                Assert.That(cloneGenitals.SourceProfile, Is.SameAs(sourceGenitals.SourceProfile));
                Assert.That(cloneGenitals.OrgansBuilt, Is.False);
                Assert.That(GenitalOrgans(entMan, clone), Is.Empty, "The clone's organs did not wait for its owner's consent.");
            });

            GrantConsent(entMan, clone);

            var organs = GenitalOrgans(entMan, clone);
            Assert.Multiple(() =>
            {
                Assert.That(organs, Has.Count.EqualTo(5));
                Assert.That(OrganState(organs, GenitalSlot.Penis), Is.Not.Null, "The removed organ did not grow back.");
                Assert.That(cloneGenitals.Penis, Is.Not.Null);
                Assert.That(cloneGenitals.OrgansBuilt);
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>TestHumanMorph: the current organs (not the template's), the runtime state and the template move to the new body.</summary>
    [Test]
    public async Task PolymorphTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid? child = null;
        GenitalProfile template = null;
        var expected = new Dictionary<GenitalSlot, GenitalOrganState>();

        await server.WaitPost(() =>
        {
            var source = SpawnWithAnatomy(entMan, map.MapCoords);

            var genitals = entMan.GetComponent<GenitalsComponent>(source);
            genitals.Arousal = 40;
            genitals.RevealMode = GenitalRevealMode.ClothingRemoval;
            genitals.Visibility = genitals.Visibility.With(GenitalSlot.Penis, GenitalVisibility.AlwaysHidden);
            genitals.Undergarments = UndergarmentFlags.TopRemoved;
            template = genitals.SourceProfile;

            // A transformation keeps what the person has now: the breasts are gone although the template has them.
            if (GenitalOrgan(entMan, source, GenitalSlot.Breasts) is { } breasts)
            {
                entMan.System<SharedBodySystem>().RemoveOrgan(breasts);
                entMan.DeleteEntity(breasts);
            }

            foreach (var organ in GenitalOrgans(entMan, source))
            {
                expected[organ.Comp1.Slot] = organ.Comp1.State;
            }

            child = entMan.System<PolymorphSystem>().PolymorphEntity(source, TestHumanMorph);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(child, Is.Not.Null, "The polymorph failed.");
            var newBody = child!.Value;
            var target = entMan.GetComponent<GenitalsComponent>(newBody);
            var organs = GenitalOrgans(entMan, newBody);

            Assert.Multiple(() =>
            {
                Assert.That(expected, Has.Count.EqualTo(4));
                Assert.That(organs, Has.Count.EqualTo(expected.Count));
                foreach (var (slot, state) in expected)
                {
                    Assert.That(OrganState(organs, slot), Is.EqualTo(state), $"{slot} state");
                }

                Assert.That(OrganState(organs, GenitalSlot.Breasts), Is.Null, "A polymorph regrew a removed organ.");
                Assert.That(target.SourceProfile, Is.SameAs(template));
                Assert.That(target.OrgansBuilt);
                Assert.That(target.Arousal, Is.EqualTo(40));
                Assert.That(target.RevealMode, Is.EqualTo(GenitalRevealMode.ClothingRemoval));
                Assert.That(target.Visibility.Penis, Is.EqualTo(GenitalVisibility.AlwaysHidden));
                Assert.That(target.Undergarments, Is.EqualTo(UndergarmentFlags.TopRemoved));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Organs still waiting for consent keep waiting on the new body, which builds them when its owner consents.</summary>
    [Test]
    public async Task PolymorphBeforeConsentTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid? child = null;
        await server.WaitPost(() =>
        {
            var source = entMan.Spawn("MobHuman", map.MapCoords);
            entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(source, MakeProfile().WithGenitals(FullProfile()));
            child = entMan.System<PolymorphSystem>().PolymorphEntity(source, TestHumanMorph);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(child, Is.Not.Null, "The polymorph failed.");
            var newBody = child!.Value;
            var target = entMan.GetComponent<GenitalsComponent>(newBody);
            Assert.Multiple(() =>
            {
                Assert.That(target.SourceProfile, Is.Not.Null);
                Assert.That(target.OrgansBuilt, Is.False, "Organs that were never built must not count as built.");
                Assert.That(GenitalOrgans(entMan, newBody), Is.Empty);
            });

            GrantConsent(entMan, newBody);
            Assert.Multiple(() =>
            {
                Assert.That(GenitalOrgans(entMan, newBody), Has.Count.EqualTo(5), "Consent on the new body did not build the organs.");
                Assert.That(target.OrgansBuilt);
                Assert.That(target.Penis, Is.Not.Null);
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Nothing moves without TransferHumanoidAppearance, or when the new body is not eligible by its own age.</summary>
    [Test]
    public async Task PolymorphWithoutTransferTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid? plain = null;
        EntityUid? minor = null;
        await server.WaitPost(() =>
        {
            var polymorph = entMan.System<PolymorphSystem>();

            var first = SpawnWithAnatomy(entMan, map.MapCoords);
            plain = polymorph.PolymorphEntity(first, new PolymorphConfiguration
            {
                Entity = "MobHuman",
                TransferHumanoidAppearance = false,
            });

            // CloneAppearance copies the age, so a source that is now 17 makes the new body ineligible.
            var second = SpawnWithAnatomy(entMan, map.MapCoords);
            entMan.GetComponent<HumanoidAppearanceComponent>(second).Age = 17;
            minor = polymorph.PolymorphEntity(second, TestHumanMorph);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(plain, Is.Not.Null, "The plain polymorph failed.");
            Assert.That(minor, Is.Not.Null, "The ineligible polymorph failed.");
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<GenitalsComponent>(plain!.Value), Is.False, "Anatomy moved without TransferHumanoidAppearance.");
                Assert.That(GenitalOrgans(entMan, plain.Value), Is.Empty);
                Assert.That(entMan.HasComponent<GenitalsComponent>(minor!.Value), Is.False, "Anatomy moved to an ineligible body.");
                Assert.That(GenitalOrgans(entMan, minor.Value), Is.Empty);
            });
        });

        await pair.CleanReturnAsync();
    }
}
