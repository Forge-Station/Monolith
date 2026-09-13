using System.Collections.Generic;
using System.Linq;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Humanoid;

/// <summary>
/// One undergarment top and one bottom per character on every species: a marking set built from a points prototype
/// caps both categories at one marking, and saved profiles holding more keep the first of each.
/// </summary>
[TestFixture]
[TestOf(typeof(MarkingSet))]
public sealed class UndergarmentLimitTest
{
    // An explicit entry above one, an explicit zero, and an unrelated category that must keep its budget.
    [TestPrototypes]
    private const string Prototypes = @"
- type: markingPoints
  id: WFTestUndergarmentPoints
  points:
    UndergarmentTop:
      points: 3
      required: false
    UndergarmentBottom:
      points: 0
      required: false
    Chest:
      points: 3
      required: false
";

    private const string TestPoints = "WFTestUndergarmentPoints";

    /// <summary>Its points prototype has no undergarment entry.</summary>
    private const string Species = "Human";

    private const string TopA = "UndergarmentTopTanktop";
    private const string TopB = "UndergarmentTopBra";
    private const string BottomA = "UndergarmentBottomBoxers";
    private const string BottomB = "UndergarmentBottomBriefs";

    private static readonly MarkingCategories[] Categories = { MarkingCategories.UndergarmentTop, MarkingCategories.UndergarmentBottom };

    private static Marking Mark(MarkingManager markings, string id)
    {
        return markings.Markings[id].AsMarking();
    }

    /// <summary>Two tops and two bottoms, interleaved as a saved profile may hold them.</summary>
    private static List<Marking> TwoOfEach(MarkingManager markings)
    {
        return new List<Marking> { Mark(markings, TopA), Mark(markings, BottomA), Mark(markings, TopB), Mark(markings, BottomB) };
    }

    private static string[] Ids(MarkingSet set, MarkingCategories category)
    {
        return set.TryGetCategory(category, out var list) ? list.Select(m => m.MarkingId).ToArray() : Array.Empty<string>();
    }

    private static string[] Ids(IEnumerable<Marking> list, MarkingManager markings, MarkingCategories category)
    {
        return list.Where(m => markings.Markings[m.MarkingId].MarkingCategory == category).Select(m => m.MarkingId).ToArray();
    }

    /// <summary>No entry: an optional budget of one per category with no defaults, the first of two kept, the prototype untouched.</summary>
    [Test]
    public async Task SpeciesWithoutEntryTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var markings = server.ResolveDependency<MarkingManager>();

        await server.WaitAssertion(() =>
        {
            var pointsId = protoMan.Index<SpeciesPrototype>(Species).MarkingPoints;
            var prototype = protoMan.Index<MarkingPointsPrototype>(pointsId);
            foreach (var category in Categories)
                Assert.That(prototype.Points.ContainsKey(category), Is.False, $"Precondition: {pointsId} has no {category} entry.");

            var set = new MarkingSet(TwoOfEach(markings), pointsId, markings, protoMan);

            // No undergarment is added by default.
            var empty = new MarkingSet(pointsId, markings, protoMan);
            empty.EnsureDefault(Color.White, Color.Black, markings);

            Assert.Multiple(() =>
            {
                Assert.That(Ids(set, MarkingCategories.UndergarmentTop), Is.EqualTo(new[] { TopA }));
                Assert.That(Ids(set, MarkingCategories.UndergarmentBottom), Is.EqualTo(new[] { BottomA }));
                foreach (var category in Categories)
                {
                    Assert.That(set.PointsLeft(category), Is.EqualTo(0), $"{category} points left.");
                    Assert.That(set.Points[category].Required, Is.False, $"{category} must stay optional.");
                    Assert.That(set.Points[category].DefaultMarkings, Is.Empty, $"{category} must have no default markings.");
                    Assert.That(prototype.Points.ContainsKey(category), Is.False, $"{pointsId} gained a {category} entry.");
                    Assert.That(Ids(empty, category), Is.Empty, $"{category} gained a default marking.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>PointsLeft reads 1 on an empty set and 0 once one is in; a second is refused at either end until the first is removed.</summary>
    [Test]
    public async Task PointsLeftTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var markings = server.ResolveDependency<MarkingManager>();

        await server.WaitAssertion(() =>
        {
            var pointsId = protoMan.Index<SpeciesPrototype>(Species).MarkingPoints;
            var set = new MarkingSet(pointsId, markings, protoMan);
            var rows = new[]
            {
                (MarkingCategories.UndergarmentTop, TopA, TopB),
                (MarkingCategories.UndergarmentBottom, BottomA, BottomB),
            };

            Assert.Multiple(() =>
            {
                foreach (var (category, first, second) in rows)
                {
                    Assert.That(set.PointsLeft(category), Is.EqualTo(1), $"{category}: empty set.");

                    set.AddBack(category, Mark(markings, first));
                    Assert.That(set.PointsLeft(category), Is.EqualTo(0), $"{category}: one added.");

                    set.AddBack(category, Mark(markings, second));
                    set.AddFront(category, Mark(markings, second));
                    Assert.That(Ids(set, category), Is.EqualTo(new[] { first }), $"{category}: a second marking was accepted.");
                    Assert.That(set.PointsLeft(category), Is.EqualTo(0), $"{category}: after the refused second.");

                    Assert.That(set.Remove(category, first), Is.True, $"{category}: remove.");
                    Assert.That(set.PointsLeft(category), Is.EqualTo(1), $"{category}: removed again.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An explicit entry above one is lowered to one, zero stays zero, and other categories and the prototype keep their values.</summary>
    [Test]
    public async Task ExplicitEntryTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var markings = server.ResolveDependency<MarkingManager>();

        await server.WaitAssertion(() =>
        {
            var prototype = protoMan.Index<MarkingPointsPrototype>(TestPoints);
            var fresh = new MarkingSet(TestPoints, markings, protoMan);
            var set = new MarkingSet(TwoOfEach(markings), TestPoints, markings, protoMan);

            Assert.Multiple(() =>
            {
                Assert.That(fresh.PointsLeft(MarkingCategories.UndergarmentTop), Is.EqualTo(1), "An entry of 3 must be lowered to 1.");
                Assert.That(fresh.PointsLeft(MarkingCategories.UndergarmentBottom), Is.EqualTo(0), "An entry of 0 must stay 0.");
                Assert.That(fresh.PointsLeft(MarkingCategories.Chest), Is.EqualTo(3), "Other categories keep their budget.");

                Assert.That(Ids(set, MarkingCategories.UndergarmentTop), Is.EqualTo(new[] { TopA }));
                Assert.That(Ids(set, MarkingCategories.UndergarmentBottom), Is.Empty);
                Assert.That(set.PointsLeft(MarkingCategories.UndergarmentTop), Is.EqualTo(0));

                Assert.That(prototype.Points[MarkingCategories.UndergarmentTop].Points, Is.EqualTo(3), "The prototype must not change.");
                Assert.That(prototype.Points[MarkingCategories.UndergarmentBottom].Points, Is.EqualTo(0), "The prototype must not change.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every points prototype, with or without an entry, allows at most one of each.</summary>
    [Test]
    public async Task EveryPointsPrototypeTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var markings = server.ResolveDependency<MarkingManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var prototype in protoMan.EnumeratePrototypes<MarkingPointsPrototype>())
                {
                    var set = new MarkingSet(prototype.ID, markings, protoMan);
                    foreach (var category in Categories)
                    {
                        var expected = prototype.Points.TryGetValue(category, out var entry) ? Math.Min(entry.Points, 1) : 1;
                        Assert.That(set.PointsLeft(category), Is.EqualTo(expected), $"{prototype.ID}: {category}");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A saved appearance and profile holding two of each validate without throwing and keep the first of each.</summary>
    [Test]
    public async Task SavedProfileTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var markings = server.ResolveDependency<MarkingManager>();

        await server.WaitAssertion(() =>
        {
            var appearance = new HumanoidCharacterAppearance(
                HairStyles.DefaultHairStyle,
                Color.Black,
                HairStyles.DefaultFacialHairStyle,
                Color.Black,
                Color.Black,
                Color.FromHex("#C0967F"),
                TwoOfEach(markings));

            HumanoidCharacterAppearance valid = default!;
            Assert.DoesNotThrow(() => valid = HumanoidCharacterAppearance.EnsureValid(appearance, Species, Sex.Female));

            // The whole profile, as the server validates it on load and on save.
            var profile = HumanoidCharacterProfile.DefaultWithSpecies(Species).WithCharacterAppearance(appearance);
            HumanoidCharacterProfile validated = default!;
            Assert.DoesNotThrow(() => validated = (HumanoidCharacterProfile) profile.Validated(pair.Player!, IoCManager.Instance!));

            Assert.Multiple(() =>
            {
                Assert.That(Ids(valid.Markings, markings, MarkingCategories.UndergarmentTop), Is.EqualTo(new[] { TopA }));
                Assert.That(Ids(valid.Markings, markings, MarkingCategories.UndergarmentBottom), Is.EqualTo(new[] { BottomA }));
                Assert.That(Ids(validated.Appearance.Markings, markings, MarkingCategories.UndergarmentTop), Is.EqualTo(new[] { TopA }));
                Assert.That(Ids(validated.Appearance.Markings, markings, MarkingCategories.UndergarmentBottom), Is.EqualTo(new[] { BottomA }));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Loading an unvalidated profile holding two of each onto a body puts only the first of each on it.</summary>
    [Test]
    public async Task LoadProfileTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var markings = server.ResolveDependency<MarkingManager>();
        var map = await pair.CreateTestMap();

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies(Species);
            profile = profile.WithCharacterAppearance(profile.Appearance.WithMarkings(TwoOfEach(markings)));
            mob = entMan.Spawn("MobHuman", map.MapCoords);
            entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(mob, profile);
        });

        await server.WaitAssertion(() =>
        {
            var set = entMan.GetComponent<HumanoidAppearanceComponent>(mob).MarkingSet;
            Assert.Multiple(() =>
            {
                Assert.That(Ids(set, MarkingCategories.UndergarmentTop), Is.EqualTo(new[] { TopA }));
                Assert.That(Ids(set, MarkingCategories.UndergarmentBottom), Is.EqualTo(new[] { BottomA }));
                foreach (var category in Categories)
                    Assert.That(set.PointsLeft(category), Is.EqualTo(0), $"{category} points left.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
