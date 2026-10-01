#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// P6: the <c>damage</c> command's optional 5th argument routes the hit to one named body part.
/// </summary>
/// <remarks>
/// The uid stays at args[3], so the existing four-argument form is untouched; only the five-argument form
/// parses args[2] as <c>ignoreResistances</c> and goes through Wolfmed's routing instead of the flat path.
/// </remarks>
[TestFixture]
public sealed class WolfmedDamageCommandTest : GameTest
{
    [Test]
    public async Task DamageCommandHitsTheNamedPartTest()
    {
        var map = await Pair.CreateTestMap();
        var entities = Server.EntMan;

        EntityUid body = default;
        await Server.WaitPost(() => body = entities.SpawnEntity("MobHuman", map.GridCoords));

        await Pair.WaitCommand($"damage Blunt 20 true {entities.GetNetEntity(body)} LeftArm");

        await Server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            var damage = entities.System<WolfmedDamageableSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var left = parts.Single(part =>
                part.Component.PartType == BodyPartType.Arm && part.Component.Symmetry == BodyPartSymmetry.Left).Id;
            var right = parts.Single(part =>
                part.Component.PartType == BodyPartType.Arm && part.Component.Symmetry == BodyPartSymmetry.Right).Id;

            Assert.Multiple(() =>
            {
                Assert.That(damage.GetAllDamage(left).GetTotal(), Is.GreaterThan(FixedPoint2.Zero),
                    "the named arm took nothing.");
                Assert.That(damage.GetAllDamage(right).GetTotal(), Is.EqualTo(FixedPoint2.Zero),
                    "the hit was not confined to the named arm.");
            });
        });
    }

    /// <summary>
    /// M1b (P31): the part form is an admin's hit with no origin, so it bypasses the ambient per-part ceiling and
    /// lands in full; the same Heat with no origin and no command stops at 0.8 of the arm's lowest destruction line.
    /// </summary>
    [Test]
    public async Task DamageCommandPassesTheAmbientCeilingTest()
    {
        var map = await Pair.CreateTestMap();
        var entities = Server.EntMan;

        EntityUid admin = default, ambient = default;
        await Server.WaitPost(() =>
        {
            admin = entities.SpawnEntity("MobHuman", map.GridCoords);
            ambient = entities.SpawnEntity("MobHuman", map.GridCoords);
        });

        await Pair.WaitCommand($"damage Heat 200 true {entities.GetNetEntity(admin)} LeftArm");

        await Server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            var damage = entities.System<WolfmedDamageableSystem>();
            EntityUid LeftArm(EntityUid body) => graph.GetBodyChildren(body).Single(part =>
                part.Component.PartType == BodyPartType.Arm && part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            var ambientArm = LeftArm(ambient);
            entities.System<Content.Shared._Onyx.Wounds.WoundDamageRoutingSystem>().TryApplyPartDamage(ambient,
                ambientArm, new Content.Shared.Damage.DamageSpecifier
                {
                    DamageDict = { ["Heat"] = FixedPoint2.New(200) },
                }, null, ignoreResistances: true);

            Assert.Multiple(() =>
            {
                Assert.That(damage.GetAllDamage(LeftArm(admin)).GetTotal(), Is.EqualTo(FixedPoint2.New(200)),
                    "the admin command was held at the ceiling.");
                // M3 (P19): the arm's lowest destruction line is Slash 210 since the Blunt and Heat rungs rose.
                Assert.That(damage.GetAllDamage(ambientArm).GetTotal(), Is.EqualTo(FixedPoint2.New(168)),
                    "ambient Heat was not held at 0.8 x 210.");
            });
        });
    }

    /// <summary>The four-argument form still routes normally instead of being read as a part name.</summary>
    [Test]
    public async Task DamageCommandWithoutAPartStillWorksTest()
    {
        var map = await Pair.CreateTestMap();
        var entities = Server.EntMan;

        EntityUid body = default;
        await Server.WaitPost(() => body = entities.SpawnEntity("MobHuman", map.GridCoords));

        await Pair.WaitCommand($"damage Blunt 20 true {entities.GetNetEntity(body)}");

        await Server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            var damage = entities.System<WolfmedDamageableSystem>();
            var total = graph.GetBodyChildren(body)
                .Aggregate(FixedPoint2.Zero, (sum, part) => sum + damage.GetAllDamage(part.Id).GetTotal());

            Assert.That(total, Is.GreaterThan(FixedPoint2.Zero), "the flat form stopped landing on a wound host.");
        });
    }
}
