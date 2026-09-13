using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Foldable;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>The exposure truth table, the clothing and undergarment coverage sources, and the vest coverage data.</summary>
[TestFixture]
public sealed class GenitalCoverageTest
{
    private const string OuterBase = "ClothingOuterBase";
    private const string Jumpsuit = "ClothingUniformJumpsuitColorGrey";
    private const string Vest = "ClothingOuterVest";
    private const string OpenedLabCoat = "ClothingOuterCoatLabOpened";
    private const string NoDataOuter = "WFGenitalCoverageTestOuter";
    private const string TopMarking = "UndergarmentTopTanktop";
    private const string BottomMarking = "UndergarmentBottomBoxers";
    private const string JumpsuitSlot = "jumpsuit";
    private const string OuterSlot = "outerClothing";

    private static readonly ProtoId<GenitalShapePrototype> Udders = "GenitalShapeBreastsUdders";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFGenitalCoverageTestOuter
  name: coverage test outer
  components:
  - type: Item
  - type: Clothing
    slots: [outerClothing]
";

    /// <summary>Every row of the exposure truth table for a groin organ, with each "any" expanded to both values.</summary>
    [Test]
    public async Task ExposureTruthTableTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = new TestBody(server.EntMan, map.GridCoords);
            var jumpsuit = body.Spawn(Jumpsuit);

            Assert.Multiple(() =>
            {
                foreach (var row in TruthTable())
                {
                    body.Wear(JumpsuitSlot, row.Clothing ? jumpsuit : null);
                    body.SetUndergarment(MarkingCategories.UndergarmentBottom, BottomMarking, row.Undergarment);
                    body.Genitals.RevealMode = row.Mode;
                    body.Genitals.Visibility = body.Genitals.Visibility.With(GenitalSlot.Penis, row.Visibility);

                    var exposure = body.Exposure(GenitalSlot.Penis);
                    var name = row.ToString();

                    Assert.That(exposure.Present, Is.True, name);
                    Assert.That(exposure.Exposed, Is.EqualTo(row.Exposed), name);
                    Assert.That(exposure.Layer, Is.EqualTo(row.Layer), name);
                    Assert.That(exposure.CoveredBy, Is.EqualTo(row.Clothing ? jumpsuit : (EntityUid?) null), name);
                    Assert.That(exposure.CoveredByUndergarment,
                        Is.EqualTo(row.Undergarment && row.Mode == GenitalRevealMode.UndergarmentRemoval),
                        name);
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A vest covers the chest only, an opened lab coat nothing, a closed one everything; the outermost item is reported.</summary>
    [Test]
    public async Task ClothingCoverageTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = new TestBody(server.EntMan, map.GridCoords);
            var foldables = server.EntMan.System<FoldableSystem>();
            var jumpsuit = body.Spawn(Jumpsuit);
            var vest = body.Spawn(Vest);
            var coat = body.Spawn(OpenedLabCoat);
            var noData = body.Spawn(NoDataOuter);
            var coatFold = server.EntMan.GetComponent<FoldableComponent>(coat);

            Assert.Multiple(() =>
            {
                // Item data.
                Assert.That(body.Coverage.GetItemRegions(jumpsuit), Is.EqualTo(GenitalRegion.All));
                Assert.That(body.Coverage.GetItemRegions(vest), Is.EqualTo(GenitalRegion.Chest));
                Assert.That(coatFold.IsFolded, Is.True, "The opened lab coat should spawn folded (unzipped).");
                Assert.That(body.Coverage.GetItemRegions(coat), Is.EqualTo(GenitalRegion.None));
                Assert.That(body.Coverage.GetItemRegions(noData), Is.EqualTo(GenitalRegion.All),
                    "Items without coverage data cover everything.");

                // Nothing worn.
                var coverage = body.GetCoverage();
                Assert.That(coverage.Clothing, Is.EqualTo(GenitalRegion.None));
                Assert.That(body.Exposure(GenitalSlot.Penis).Exposed, Is.True);
                Assert.That(body.Exposure(GenitalSlot.Breasts).Exposed, Is.True);

                // Vest only: the chest is covered, the groin is not.
                body.Wear(OuterSlot, vest);
                coverage = body.GetCoverage();
                Assert.That(coverage.Clothing, Is.EqualTo(GenitalRegion.Chest));
                Assert.That(coverage.ChestCover, Is.EqualTo(vest));
                Assert.That(coverage.GroinCover, Is.Null);
                Assert.That(body.Exposure(GenitalSlot.Penis).Exposed, Is.True, "A vest leaves the groin uncovered.");
                var breasts = body.Exposure(GenitalSlot.Breasts);
                Assert.That(breasts.Exposed, Is.False);
                Assert.That(breasts.CoveredBy, Is.EqualTo(vest));
                Assert.That(body.Coverage.IsRegionCoveredByClothing(body.Mob, GenitalRegion.Chest), Is.True);
                Assert.That(body.Coverage.IsRegionCoveredByClothing(body.Mob, GenitalRegion.Groin), Is.False);

                // Jumpsuit under the vest: each region reports its outermost item.
                body.Wear(JumpsuitSlot, jumpsuit);
                coverage = body.GetCoverage();
                Assert.That(coverage.Clothing, Is.EqualTo(GenitalRegion.All));
                Assert.That(coverage.ChestCover, Is.EqualTo(vest));
                Assert.That(coverage.GroinCover, Is.EqualTo(jumpsuit));
                Assert.That(body.Exposure(GenitalSlot.Penis).CoveredBy, Is.EqualTo(jumpsuit));

                // The opened (folded) lab coat alone covers nothing.
                body.Wear(JumpsuitSlot, null);
                body.Wear(OuterSlot, coat);
                Assert.That(body.GetCoverage().Clothing, Is.EqualTo(GenitalRegion.None));
                Assert.That(body.Exposure(GenitalSlot.Penis).Exposed, Is.True);

                // Opened coat over a jumpsuit: the jumpsuit covers.
                body.Wear(JumpsuitSlot, jumpsuit);
                Assert.That(body.GetCoverage().GroinCover, Is.EqualTo(jumpsuit));

                // Closed coat over a jumpsuit: the coat is outermost.
                foldables.SetFolded(coat, coatFold, false);
                Assert.That(body.Coverage.GetItemRegions(coat), Is.EqualTo(GenitalRegion.All));
                coverage = body.GetCoverage();
                Assert.That(coverage.ChestCover, Is.EqualTo(coat));
                Assert.That(coverage.GroinCover, Is.EqualTo(coat));
                Assert.That(body.Exposure(GenitalSlot.Penis).CoveredBy, Is.EqualTo(coat));

                // Closed coat alone.
                body.Wear(JumpsuitSlot, null);
                Assert.That(body.Exposure(GenitalSlot.Penis).Exposed, Is.False);

                // An outer item without coverage data covers both regions.
                body.Wear(OuterSlot, noData);
                Assert.That(body.GetCoverage().Clothing, Is.EqualTo(GenitalRegion.All));
                Assert.That(body.Exposure(GenitalSlot.Penis).CoveredBy, Is.EqualTo(noData));
                Assert.That(body.Exposure(GenitalSlot.Breasts).CoveredBy, Is.EqualTo(noData));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Undergarment markings cover their region unless removed or hidden; ClothingRemoval and clothing checks ignore them.</summary>
    [Test]
    public async Task UndergarmentCoverageTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = new TestBody(server.EntMan, map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(body.GetCoverage().Undergarments, Is.EqualTo(GenitalRegion.None), "No undergarment markings.");

                // Bottom only.
                body.SetUndergarment(MarkingCategories.UndergarmentBottom, BottomMarking, true);
                Assert.That(body.GetCoverage().Undergarments, Is.EqualTo(GenitalRegion.Groin));
                var penis = body.Exposure(GenitalSlot.Penis);
                Assert.That(penis.Exposed, Is.False);
                Assert.That(penis.Layer, Is.EqualTo(GenitalLayerSet.Hidden));
                Assert.That(penis.CoveredByUndergarment, Is.True);
                Assert.That(penis.CoveredBy, Is.Null);
                Assert.That(body.Exposure(GenitalSlot.Breasts).Exposed, Is.True, "A bottom undergarment leaves the chest uncovered.");
                Assert.That(body.Coverage.IsRegionCoveredByClothing(body.Mob, GenitalRegion.Groin), Is.False,
                    "Undergarments are not clothing.");

                // Top and bottom.
                body.SetUndergarment(MarkingCategories.UndergarmentTop, TopMarking, true);
                Assert.That(body.GetCoverage().Undergarments, Is.EqualTo(GenitalRegion.All));
                Assert.That(body.Exposure(GenitalSlot.Breasts).Exposed, Is.False);

                // Removal flags.
                body.Genitals.Undergarments = UndergarmentFlags.TopRemoved;
                Assert.That(body.GetCoverage().Undergarments, Is.EqualTo(GenitalRegion.Groin));
                Assert.That(body.Exposure(GenitalSlot.Breasts).Exposed, Is.True);
                Assert.That(body.Exposure(GenitalSlot.Penis).Exposed, Is.False, "Removing the top leaves the groin covered.");

                body.Genitals.Undergarments = UndergarmentFlags.BottomRemoved | UndergarmentFlags.BottomByOther;
                Assert.That(body.GetCoverage().Undergarments, Is.EqualTo(GenitalRegion.Chest));
                Assert.That(body.Exposure(GenitalSlot.Penis).Exposed, Is.True);

                body.Genitals.Undergarments = UndergarmentFlags.TopByOther | UndergarmentFlags.BottomByOther;
                Assert.That(body.GetCoverage().Undergarments, Is.EqualTo(GenitalRegion.All), "Only the Removed bits uncover.");

                // A hidden marking does not cover.
                body.Genitals.Undergarments = UndergarmentFlags.None;
                Assert.That(body.Humanoid.MarkingSet.TryGetMarking(MarkingCategories.UndergarmentBottom, BottomMarking, out var bottom),
                    Is.True);
                if (bottom != null)
                {
                    bottom.Visible = false;
                    Assert.That(body.GetCoverage().Undergarments, Is.EqualTo(GenitalRegion.Chest), "A hidden marking does not cover.");
                    bottom.Visible = true;
                }

                // ClothingRemoval: undergarments never count.
                body.Genitals.RevealMode = GenitalRevealMode.ClothingRemoval;
                penis = body.Exposure(GenitalSlot.Penis);
                Assert.That(penis.Exposed, Is.True);
                Assert.That(penis.Layer, Is.EqualTo(GenitalLayerSet.Under));
                Assert.That(penis.CoveredByUndergarment, Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Slot regions (udders sit in the groin), presence of internal organs, and the creator preview overrides.</summary>
    [Test]
    public async Task RegionPresencePreviewTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = new TestBody(server.EntMan, map.GridCoords);
            var jumpsuit = body.Spawn(Jumpsuit);
            var vest = body.Spawn(Vest);

            Assert.Multiple(() =>
            {
                Assert.That(body.Coverage.GetRegion(body.Ent, GenitalSlot.Penis), Is.EqualTo(GenitalRegion.Groin));
                Assert.That(body.Coverage.GetRegion(body.Ent, GenitalSlot.Testicles), Is.EqualTo(GenitalRegion.Groin));
                Assert.That(body.Coverage.GetRegion(body.Ent, GenitalSlot.Vagina), Is.EqualTo(GenitalRegion.Groin));
                Assert.That(body.Coverage.GetRegion(body.Ent, GenitalSlot.Womb), Is.EqualTo(GenitalRegion.None));
                Assert.That(body.Coverage.GetRegion(body.Ent, GenitalSlot.Breasts), Is.EqualTo(GenitalRegion.Chest));

                // Udders are groin anatomy, so a vest does not cover them.
                body.Genitals.Breasts = BreastsState(Udders);
                Assert.That(body.Coverage.GetRegion(body.Ent, GenitalSlot.Breasts), Is.EqualTo(GenitalRegion.Groin));
                body.Wear(OuterSlot, vest);
                Assert.That(body.Exposure(GenitalSlot.Breasts).Exposed, Is.True, "A vest leaves udders uncovered.");
                body.Wear(OuterSlot, null);

                // Missing organs, the womb and internal testicles are never present.
                Assert.That(body.Exposure(GenitalSlot.Vagina),
                    Is.EqualTo(new GenitalExposure(false, false, GenitalLayerSet.Hidden, null, false)));
                body.Genitals.Womb = true;
                Assert.That(body.Exposure(GenitalSlot.Womb).Present, Is.False);
                body.Genitals.Testicles = new GenitalOrganState { Testicles = TesticleType.Internal };
                var testicles = body.Exposure(GenitalSlot.Testicles);
                Assert.That(testicles.Present, Is.False);
                Assert.That(testicles.Exposed, Is.False);
                body.Genitals.Testicles = new GenitalOrganState { Testicles = TesticleType.External, Step = 2 };
                Assert.That(body.Exposure(GenitalSlot.Testicles).Exposed, Is.True);
                body.Genitals.Vagina = new GenitalOrganState { Shape = GenitalTestHelpers.VaginaHuman };
                Assert.That(body.Exposure(GenitalSlot.Vagina).Exposed, Is.True);

                // Always hidden stays present but is never exposed.
                body.Genitals.Visibility = body.Genitals.Visibility.With(GenitalSlot.Vagina, GenitalVisibility.AlwaysHidden);
                var vagina = body.Exposure(GenitalSlot.Vagina);
                Assert.That(vagina.Present, Is.True);
                Assert.That(vagina.Exposed, Is.False);

                // Preview overrides: UnderwearOnly ignores clothing, Nude also ignores undergarments.
                body.Wear(JumpsuitSlot, jumpsuit);
                body.SetUndergarment(MarkingCategories.UndergarmentBottom, BottomMarking, true);
                body.Genitals.IsPreview = true;

                body.Genitals.PreviewMode = GenitalPreviewMode.AsWorn;
                var penis = body.Exposure(GenitalSlot.Penis);
                Assert.That(penis.Exposed, Is.False);
                Assert.That(penis.CoveredBy, Is.EqualTo(jumpsuit));

                body.Genitals.PreviewMode = GenitalPreviewMode.UnderwearOnly;
                penis = body.Exposure(GenitalSlot.Penis);
                Assert.That(penis.Exposed, Is.False);
                Assert.That(penis.CoveredBy, Is.Null);
                Assert.That(penis.CoveredByUndergarment, Is.True);

                body.Genitals.PreviewMode = GenitalPreviewMode.Nude;
                penis = body.Exposure(GenitalSlot.Penis);
                Assert.That(penis.Exposed, Is.True);
                Assert.That(penis.Layer, Is.EqualTo(GenitalLayerSet.Under));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Re-derives the vest set (ids containing "Vest" under ClothingOuterBase, hardsuits excluded) and checks each covers the chest only,
    /// so a vest added later without data fails here.
    /// </summary>
    [Test]
    public async Task DerivedVestCoverageTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var vests = protoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => proto.ID.Contains("Vest", StringComparison.Ordinal))
                .Where(proto =>
                {
                    var lineage = protoMan.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true)
                        .Select(parent => parent.id)
                        .ToList();

                    return lineage.Contains(OuterBase)
                           && !lineage.Any(id => id.Contains("Hardsuit", StringComparison.Ordinal));
                })
                .ToList();

            Assert.That(vests.Select(proto => proto.ID), Does.Contain(Vest), "The vest derivation found nothing.");

            Assert.Multiple(() =>
            {
                foreach (var vest in vests)
                {
                    var hasData = vest.TryGetComponent<GenitalCoverageComponent>(out var coverage, factory);
                    Assert.That(hasData, Is.True, $"{vest.ID} has no GenitalCoverage data, so it would cover the groin.");
                    if (coverage != null)
                        Assert.That(coverage.Regions, Is.EqualTo(GenitalRegion.Chest), $"{vest.ID} should cover the chest only.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>One row of the exposure truth table for a groin organ.</summary>
    private readonly record struct Row(
        GenitalVisibility Visibility,
        GenitalRevealMode Mode,
        bool Clothing,
        bool Undergarment,
        bool Exposed,
        GenitalLayerSet Layer);

    /// <summary>The exposure truth table with every "any" expanded to both values.</summary>
    private static IEnumerable<Row> TruthTable()
    {
        var bools = new[] { false, true };
        var modes = new[] { GenitalRevealMode.UndergarmentRemoval, GenitalRevealMode.ClothingRemoval };
        var undergarmentRemoval = GenitalRevealMode.UndergarmentRemoval;
        var clothingRemoval = GenitalRevealMode.ClothingRemoval;

        // Normal, UndergarmentRemoval: exposed only when neither clothing nor an undergarment covers.
        yield return new Row(GenitalVisibility.Normal, undergarmentRemoval, false, false, true, GenitalLayerSet.Under);
        yield return new Row(GenitalVisibility.Normal, undergarmentRemoval, false, true, false, GenitalLayerSet.Hidden);
        foreach (var under in bools)
        {
            yield return new Row(GenitalVisibility.Normal, undergarmentRemoval, true, under, false, GenitalLayerSet.Hidden);
        }

        // Normal, ClothingRemoval: undergarments never count.
        foreach (var under in bools)
        {
            yield return new Row(GenitalVisibility.Normal, clothingRemoval, false, under, true, GenitalLayerSet.Under);
            yield return new Row(GenitalVisibility.Normal, clothingRemoval, true, under, false, GenitalLayerSet.Hidden);
        }

        // AlwaysHidden: never exposed.
        foreach (var mode in modes)
        {
            foreach (var cloth in bools)
            {
                foreach (var under in bools)
                {
                    yield return new Row(GenitalVisibility.AlwaysHidden, mode, cloth, under, false, GenitalLayerSet.Hidden);
                }
            }
        }

        // ShowThroughClothing: always exposed, drawn Over while clothing covers the region.
        foreach (var mode in modes)
        {
            foreach (var under in bools)
            {
                yield return new Row(GenitalVisibility.ShowThroughClothing, mode, false, under, true, GenitalLayerSet.Under);
                yield return new Row(GenitalVisibility.ShowThroughClothing, mode, true, under, true, GenitalLayerSet.Over);
            }
        }
    }

    private static GenitalOrganState PenisState()
    {
        return new GenitalOrganState { Shape = GenitalTestHelpers.PenisHuman, Step = 1, LengthCm = 15 };
    }

    private static GenitalOrganState BreastsState(ProtoId<GenitalShapePrototype> shape)
    {
        return new GenitalOrganState { Shape = shape, Step = 3 };
    }

    /// <summary>A MobHuman with a penis and a breasts mirror, no undergarment markings and nothing worn. Server thread only.</summary>
    private sealed class TestBody
    {
        public readonly IEntityManager EntMan;
        public readonly GenitalCoverageSystem Coverage;
        public readonly InventorySystem Inventory;
        public readonly SharedHumanoidAppearanceSystem Humanoids;
        public readonly EntityCoordinates Coords;
        public readonly EntityUid Mob;
        public readonly GenitalsComponent Genitals;
        public readonly HumanoidAppearanceComponent Humanoid;

        public TestBody(IEntityManager entMan, EntityCoordinates coords)
        {
            EntMan = entMan;
            Coverage = entMan.System<GenitalCoverageSystem>();
            Inventory = entMan.System<InventorySystem>();
            Humanoids = entMan.System<SharedHumanoidAppearanceSystem>();
            Coords = coords;

            Mob = entMan.SpawnEntity("MobHuman", coords);
            Humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(Mob);
            Humanoid.MarkingSet.RemoveCategory(MarkingCategories.UndergarmentTop);
            Humanoid.MarkingSet.RemoveCategory(MarkingCategories.UndergarmentBottom);

            Genitals = GenitalTestHelpers.SetMirror(entMan, Mob, penis: PenisState(), breasts: BreastsState(GenitalTestHelpers.BreastsPair));
            Genitals.RevealMode = GenitalRevealMode.UndergarmentRemoval;
            Genitals.Visibility = default;
            Genitals.Undergarments = UndergarmentFlags.None;
            Genitals.PreviewMode = GenitalPreviewMode.AsWorn;
            Genitals.IsPreview = false;
        }

        public Entity<GenitalsComponent> Ent => (Mob, Genitals);

        public EntityUid Spawn(string id)
        {
            return EntMan.SpawnEntity(id, Coords);
        }

        public GenitalCoverage GetCoverage()
        {
            return Coverage.GetCoverage((Mob, Genitals, Humanoid));
        }

        public GenitalExposure Exposure(GenitalSlot slot)
        {
            return Coverage.GetExposure(Ent, slot, GetCoverage());
        }

        /// <summary>Puts the item in the slot, or empties the slot when the item is null.</summary>
        public void Wear(string slot, EntityUid? item)
        {
            if (Inventory.TryGetSlotEntity(Mob, slot, out var current))
            {
                if (current == item)
                    return;

                Assert.That(Inventory.TryUnequip(Mob, slot, silent: true, force: true), $"Could not empty {slot}.");
            }

            if (item is { } uid)
                Assert.That(Inventory.TryEquip(Mob, uid, slot, silent: true, force: true), $"Could not equip {uid} in {slot}.");
        }

        /// <summary>Adds or removes the undergarment marking of one category.</summary>
        public void SetUndergarment(MarkingCategories category, string marking, bool worn)
        {
            Humanoid.MarkingSet.RemoveCategory(category);
            if (!worn)
                return;

            Humanoids.AddMarking(Mob, marking, forced: true, humanoid: Humanoid);
            Assert.That(Humanoid.MarkingSet.TryGetMarking(category, marking, out _), Is.True, $"Marking {marking} was not added.");
        }
    }
}
