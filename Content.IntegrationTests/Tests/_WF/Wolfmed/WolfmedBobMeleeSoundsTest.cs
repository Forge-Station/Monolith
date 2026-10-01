#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Sounds;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Weapons.Melee;
using NUnit.Framework;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 4, second round: fists punch (the Punch collection), crowbars clang over their thud, and a super heavy
/// Blunt or Slash hit on flesh gets the meaty chop over the weapon's own sound. The fire axe and spear keep their
/// own hit sounds.
/// </summary>
[TestFixture]
public sealed class WolfmedBobMeleeSoundsTest : GameTest
{
    private static DamageSpecifier Damage(string type, float amount) =>
        new() { DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) } };

    [TestCase("Crowbar")]
    [TestCase("CrowbarRed")]
    [TestCase("CrowbarPocket")]
    public async Task CrowbarsCarryTheOverlayTest(string proto)
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = SProtoMan.Index<EntityPrototype>(proto);
            Assert.That(prototype.Components.TryGetValue("WolfmedHitOverlaySound", out var entry), Is.True, $"{proto} has no overlay.");
            var overlay = (WolfmedHitOverlaySoundComponent) entry!.Component;
            Assert.That(((SoundCollectionSpecifier) overlay.Sound).Collection, Is.EqualTo("WFWolfmedCrowbarHit"));
            Assert.That(((SoundCollectionSpecifier) ((MeleeWeaponComponent) prototype.Components["MeleeWeapon"].Component).HitSound!).Collection,
                Is.EqualTo("MetalThud"), "the crowbar lost its own thud.");
        });
    }

    [TestCase("FireAxe", "MetalThud")]
    [TestCase("Spear", null)]
    public async Task HeavyWeaponsKeepTheirOwnSoundTest(string proto, string? collection)
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = SProtoMan.Index<EntityPrototype>(proto);
            Assert.That(prototype.Components.ContainsKey("WolfmedHitOverlaySound"), Is.False, $"{proto} carries an overlay.");
            var melee = (MeleeWeaponComponent) prototype.Components["MeleeWeapon"].Component;
            if (collection != null)
                Assert.That(((SoundCollectionSpecifier) melee.HitSound!).Collection, Is.EqualTo(collection));
            else
                Assert.That(melee.HitSound, Is.TypeOf<SoundPathSpecifier>());
        });
    }

    [Test]
    public async Task ChopIsForSuperHeavyHitsTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.ChopSoundDamage, 40f);
        await Server.WaitAssertion(() =>
        {
            var sounds = SEntMan.System<WolfmedOrganicSoundSystem>();
            Assert.That(sounds.IsChop(Damage("Slash", 45)), Is.True, "a wielded fire axe swing does not chop.");
            Assert.That(sounds.IsChop(Damage("Blunt", 40)), Is.True, "a 40 Blunt hit does not chop.");
            Assert.That(sounds.IsChop(Damage("Slash", 32)), Is.False, "a machete chops.");
            Assert.That(sounds.IsChop(Damage("Piercing", 60)), Is.False, "a stab chops.");
            Assert.That(SProtoMan.Index<SoundCollectionPrototype>("WFWolfmedChop").PickFiles, Has.Count.EqualTo(3));
            Assert.That(SProtoMan.Index<SoundCollectionPrototype>("WFWolfmedPunch").PickFiles, Has.Count.EqualTo(3));
        });
    }
}
