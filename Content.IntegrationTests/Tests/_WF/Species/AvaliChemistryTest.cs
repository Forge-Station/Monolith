#nullable enable
using System.Collections.Generic;
using Content.Server.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Species;

/// <summary>
/// The Avali reagent rules ported from HardLight: amoxla and ammonia are safe for Avali and poison humans, saline,
/// dexalin, dexalin plus and iron poison Avali and not humans, amoxla restores Avali blood, ammonia heals their
/// airloss, and the Avali auto-injector is loaded, assemblable and in every survival loadout group.
/// </summary>
[TestFixture]
public sealed class AvaliChemistryTest
{
    /// <summary>Five seconds: enough heart metabolism passes for every reagent to take effect.</summary>
    private const int MetabolismTicks = 150;

    private const string Avali = "MobAvali";
    private const string Human = "MobHuman";

    /// <summary>Reagents that poison one species and not the other.</summary>
    private static readonly string[] Reagents = { "Amoxla", "Saline", "Ammonia", "Iron", "Dexalin", "DexalinPlus" };

    [Test]
    public async Task ReagentsFollowSpeciesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var bloodstream = entMan.System<BloodstreamSystem>();
        var damageable = entMan.System<DamageableSystem>();
        var asphyxiation = server.ProtoMan.Index<DamageTypePrototype>("Asphyxiation");

        // Keyed by species (or the healing check) and the reagent injected; "" is an untreated control.
        var subjects = new Dictionary<(string Group, string Reagent), EntityUid>();

        void Inject(EntityUid mob, string reagent)
        {
            if (reagent == "")
                return;
            Assert.That(bloodstream.TryAddToChemicals(mob, new Solution(reagent, FixedPoint2.New(10))),
                Is.True, $"Could not inject {reagent} into {entMan.ToPrettyString(mob)}.");
        }

        EntityUid Spawn(string group, string species, string reagent)
        {
            var mob = entMan.SpawnEntity(species, map.GridCoords);
            subjects[(group, reagent)] = mob;
            return mob;
        }

        await server.WaitPost(() =>
        {
            foreach (var species in new[] { Avali, Human })
            {
                foreach (var reagent in Reagents)
                    Inject(Spawn(species, species, reagent), reagent);
            }

            // Healing is judged against an untreated Avali, since the airless test map and the bloodstream's own
            // regeneration move the same numbers. Blood is drained before injecting: spilling it spills chemicals too.
            foreach (var reagent in new[] { "", "Amoxla" })
            {
                var mob = Spawn("blood", Avali, reagent);
                bloodstream.TryModifyBloodLevel(mob, FixedPoint2.New(-100));
                Inject(mob, reagent);
            }
            foreach (var reagent in new[] { "", "Ammonia" })
            {
                var mob = Spawn("airloss", Avali, reagent);
                damageable.TryChangeDamage(mob, new DamageSpecifier(asphyxiation, 30), true);
                Inject(mob, reagent);
            }
        });

        await server.WaitRunTicks(MetabolismTicks);

        await server.WaitAssertion(() =>
        {
            FixedPoint2 Damage(string group, string reagent, string type)
            {
                var damage = entMan.GetComponent<DamageableComponent>(subjects[(group, reagent)]).Damage;
                return damage.DamageDict.GetValueOrDefault(type);
            }

            Assert.Multiple(() =>
            {
                Assert.That(Damage(Avali, "Amoxla", "Poison"), Is.EqualTo(FixedPoint2.Zero), "Amoxla poisoned an Avali.");
                Assert.That(Damage(Human, "Amoxla", "Poison"), Is.GreaterThan(FixedPoint2.Zero), "Amoxla didn't poison a human.");
                Assert.That(Damage(Avali, "Ammonia", "Caustic"), Is.EqualTo(FixedPoint2.Zero), "Ammonia burned an Avali.");
                // Wolfmed: what a metabolised reagent deals a wound host is toxin load, so the caustic arrives as Poison.
                Assert.That(Damage(Human, "Ammonia", "Poison"), Is.GreaterThan(FixedPoint2.Zero), "Ammonia didn't harm a human.");
                foreach (var reagent in new[] { "Saline", "Iron", "Dexalin", "DexalinPlus" })
                {
                    Assert.That(Damage(Avali, reagent, "Poison"), Is.GreaterThan(FixedPoint2.Zero), $"{reagent} didn't poison an Avali.");
                    Assert.That(Damage(Human, reagent, "Poison"), Is.EqualTo(FixedPoint2.Zero), $"{reagent} poisoned a human.");
                }

                Assert.That(bloodstream.GetBloodLevelPercentage(subjects[("blood", "Amoxla")]),
                    Is.GreaterThan(bloodstream.GetBloodLevelPercentage(subjects[("blood", "")])),
                    "Amoxla didn't restore an Avali's blood.");
                Assert.That(Damage("airloss", "Ammonia", "Asphyxiation"), Is.LessThan(Damage("airloss", "", "Asphyxiation")),
                    "Ammonia didn't heal an Avali's airloss.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AutoInjectorTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var map = await pair.CreateTestMap();
        var solutions = entMan.System<SharedSolutionContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var pen = entMan.SpawnEntity("AvaliAutoInjector", map.GridCoords);
            Assert.That(solutions.TryGetSolution(pen, "pen", out _, out var solution), Is.True, "The pen has no solution.");
            Assert.Multiple(() =>
            {
                Assert.That(solution!.GetTotalPrototypeQuantity("Amoxla"), Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(solution.GetTotalPrototypeQuantity("TranexamicAcid"), Is.EqualTo(FixedPoint2.New(5)));
            });

            var pack = protoMan.Index(new ProtoId<LatheRecipePackPrototype>("MedicalAssemblerStatic"));
            Assert.That(pack.Recipes, Does.Contain(new ProtoId<LatheRecipePrototype>("AvaliAutoInjector")),
                "The medical assembler can't make the Avali auto-injector.");
            Assert.That(protoMan.Index(new ProtoId<LatheRecipePrototype>("AvaliAutoInjector")).Result?.Id, Is.EqualTo("AvaliAutoInjector"));

            // Avali get the pen wherever Vox get their nitrogen tank.
            var avaliLoadout = new ProtoId<LoadoutPrototype>("WFLoadoutSpeciesAvaliAutoInjector");
            Assert.That(protoMan.Index(avaliLoadout).Storage["back"], Does.Contain(new EntProtoId("AvaliAutoInjector")));
            var voxLoadout = new ProtoId<LoadoutPrototype>("LoadoutSpeciesVoxNitrogen");
            foreach (var group in protoMan.EnumeratePrototypes<LoadoutGroupPrototype>())
            {
                if (group.Loadouts.Contains(voxLoadout))
                    Assert.That(group.Loadouts, Does.Contain(avaliLoadout), $"{group.ID} gives Vox a tank but Avali no auto-injector.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
