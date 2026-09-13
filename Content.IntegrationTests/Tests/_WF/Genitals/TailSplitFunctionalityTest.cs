using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server.Wagging;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Wagging;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>
/// Moving every tail marking's south frame onto TailBehind must not break the rest of the tail features. Saved
/// colours still pad onto the added halves, the wagging swap keeps the sprite structure and the colours, and clothing
/// that hides the Tail layer hides the BEHIND half as well as the FRONT one.
/// </summary>
[TestFixture]
public sealed class TailSplitFunctionalityTest
{
    /// <summary>A Vulpkanin (the species carries WaggingComponent) that also hides its Tail layer under an outer garment.</summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFTailSplitTestMob
  parent: MobVulpkanin
  components:
  - type: HumanoidAppearance
    species: Vulpkanin
    hideLayersOnEquip:
    - Hair
    - HeadTop
    - Snout
    - Tail

- type: entity
  id: WFTailSplitTestCoat
  name: tail hide test coat
  components:
  - type: Item
  - type: Clothing
    slots: [outerClothing]
  - type: HideLayerClothing
    layers:
      Tail: OUTERCLOTHING
";

    // Plain strings: the YAML linter validates static prototype-id fields and does not load test prototypes.
    private const string TestMob = "WFTailSplitTestMob";
    private const string TestCoat = "WFTailSplitTestCoat";
    private const string TestSpecies = "Vulpkanin";
    private const string OuterSlot = "outerClothing";

    /// <summary>Distinct colours, so a colour that moved to the wrong sprite is visible in the failure.</summary>
    private static readonly Color[] Palette =
    {
        Color.FromHex("#3A7BD5"),
        Color.FromHex("#D55E3A"),
        Color.FromHex("#3AD57B"),
        Color.FromHex("#7B3AD5"),
    };

    /// <summary>
    /// A colour list saved before the split keeps every colour it had and pads the added halves from their colorLinks
    /// parents, for every split tail marking and along the profile and humanoid path a character actually takes.
    /// </summary>
    [Test]
    public async Task SplitTailKeepsSavedColoursTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ProtoMan;
        var markingMan = server.ResolveDependency<MarkingManager>();
        var humanoidSys = entMan.System<SharedHumanoidAppearanceSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var splits = SplitTails(pair, proto);
            Assert.That(splits, Is.Not.Empty,
                "No tail marking draws a sprite on TailBehind, so the south-frame split has not been applied.");

            Assert.Multiple(() =>
            {
                foreach (var tail in splits)
                {
                    // Colours a character saved before the split: everything up to the trailing run of linked sprites.
                    // A tail that already had BEHIND sprites keeps its sprite count, and then nothing needs padding.
                    var savedCount = tail.Sprites.Count;
                    while (savedCount > 1 && tail.IsColorLinked(savedCount - 1))
                    {
                        savedCount--;
                    }

                    var saved = new List<Color>();
                    for (var i = 0; i < savedCount; i++)
                    {
                        saved.Add(Palette[i % Palette.Length]);
                    }

                    var padded = Validate(markingMan, new Marking(tail.ID, saved) { Visible = false });
                    Assert.That(padded.MarkingColors, Has.Count.EqualTo(tail.Sprites.Count),
                        $"{tail.ID}: EnsureValid must pad the colour list to the new sprite count.");
                    Assert.That(padded.Visible, Is.False, $"{tail.ID}: EnsureValid must keep the saved visibility.");

                    for (var i = 0; i < tail.Sprites.Count; i++)
                    {
                        if (i < savedCount)
                        {
                            Assert.That(padded.MarkingColors[i], Is.EqualTo(saved[i]),
                                $"{tail.ID}: sprite {i} ({StateOf(tail, i)}) lost its saved colour.");
                            continue;
                        }

                        var parent = ParentIndex(tail, i);
                        Assert.That(parent, Is.InRange(0, savedCount - 1),
                            $"{tail.ID}: sprite {i} ({StateOf(tail, i)}) must follow the colour of an earlier saved sprite.");
                        if (parent < 0 || parent >= savedCount)
                            continue;

                        Assert.That(padded.MarkingColors[i], Is.EqualTo(saved[parent]),
                            $"{tail.ID}: sprite {i} ({StateOf(tail, i)}) was not padded from {StateOf(tail, parent)}.");
                    }
                }
            });

            // The profile and humanoid path: one saved colour on a split tail reaches every layer of the loaded mob.
            var chosen = LiveSplitTail(pair, proto);
            Assert.That(chosen, Is.Not.Null, $"No split tail marking is allowed on {TestSpecies}.");
            if (chosen == null)
                return;

            var one = new List<Color> { Palette[0] };
            var profile = MakeProfile(TestSpecies);
            var appearance = profile.Appearance.WithMarkings(new List<Marking> { new(chosen.ID, one) });
            var valid = HumanoidCharacterAppearance.EnsureValid(appearance, TestSpecies, profile.Sex);
            var validTail = valid.Markings.FirstOrDefault(m => m.MarkingId == chosen.ID);

            Assert.That(validTail, Is.Not.Null, $"{chosen.ID}: profile validation dropped the tail marking.");
            if (validTail == null)
                return;

            Assert.That(validTail.MarkingColors, Has.Count.EqualTo(chosen.Sprites.Count),
                $"{chosen.ID}: profile validation must pad the saved colour onto the added halves.");

            var mob = entMan.SpawnEntity(TestMob, map.GridCoords);
            humanoidSys.LoadProfile(mob, profile.WithCharacterAppearance(valid));

            var loaded = LoadedTail(entMan, mob, chosen.ID);
            Assert.That(loaded, Is.Not.Null, $"{chosen.ID}: the loaded humanoid has no such tail marking.");
            if (loaded == null)
                return;

            var drawn = chosen.ResolveLinkedColors(loaded.MarkingColors);
            Assert.That(drawn, Is.All.EqualTo(Palette[0]),
                $"{chosen.ID}: every sprite of a tail saved with one colour must be drawn in that colour.");

            entMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The wagging action swaps a tail marking for its Animated variant and back. SetMarkingId copies colours by index,
    /// so both variants must offer the same colour boxes in the same places, and any half only one of them gained from
    /// the split must follow another sprite's colour rather than be left white.
    /// </summary>
    [Test]
    public async Task WaggingKeepsTailStructureTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ProtoMan;
        var humanoidSys = entMan.System<SharedHumanoidAppearanceSystem>();
        var waggingSys = entMan.System<WaggingSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var suffix = new WaggingComponent().Suffix;
            var pairs = new List<(MarkingPrototype Static, MarkingPrototype Animated)>();

            foreach (var tail in TailMarkings(pair, proto))
            {
                if (proto.TryIndex<MarkingPrototype>(tail.ID + suffix, out var animated))
                    pairs.Add((tail, animated));
            }

            Assert.That(pairs, Is.Not.Empty, "No tail marking has an animated variant, so wagging was not covered.");

            Assert.Multiple(() =>
            {
                foreach (var (still, animated) in pairs)
                {
                    // SetMarkingId rebuilds the marking from the new prototype and copies the old colours by index, so
                    // the two variants must offer the same colour boxes in the same places.
                    Assert.That(ColorBoxes(animated), Is.EqualTo(ColorBoxes(still)),
                        $"{animated.ID} must offer as many colour boxes as {still.ID}, or wagging changes which colours a character picked.");

                    var shared = Math.Min(still.Sprites.Count, animated.Sprites.Count);
                    for (var i = 0; i < shared; i++)
                    {
                        Assert.That(LayerOf(animated, i), Is.EqualTo(LayerOf(still, i)),
                            $"{animated.ID}: sprite {i} draws on a different layer than in {still.ID}.");
                        Assert.That(animated.IsColorLinked(i), Is.EqualTo(still.IsColorLinked(i)),
                            $"{animated.ID}: sprite {i} is colour-linked differently than in {still.ID}.");
                    }

                    // Only one variant may need splitting, because a state with an empty south frame is left alone. The
                    // halves the other one gains get no colour from the swap, so they must follow another sprite.
                    var longer = animated.Sprites.Count > still.Sprites.Count ? animated : still;
                    for (var i = shared; i < longer.Sprites.Count; i++)
                    {
                        Assert.That(longer.IsColorLinked(i), Is.True,
                            $"{longer.ID}: sprite {i} ({StateOf(longer, i)}) has no counterpart in the other wagging variant and no colorLinks parent, so the swap would draw it white.");
                    }
                }
            });

            // The live swap on a wag-capable species, with a split tail in distinct colours.
            var chosen = pairs
                .Where(p => AllowedOn(p.Static, TestSpecies)
                            && AllowedOn(p.Animated, TestSpecies)
                            && p.Static.MarkingCategory == MarkingCategories.Tail
                            && !p.Static.ForcedColoring
                            && p.Static.Sprites.Count == p.Animated.Sprites.Count
                            && DrawsBehind(p.Static))
                .Select(p => p.Static)
                .FirstOrDefault();

            Assert.That(chosen, Is.Not.Null,
                $"No split tail marking with an animated variant is allowed on {TestSpecies}.");
            if (chosen == null)
                return;

            var colors = new List<Color>();
            for (var i = 0; i < chosen.Sprites.Count; i++)
            {
                colors.Add(Palette[i % Palette.Length]);
            }

            var profile = MakeProfile(TestSpecies);
            profile = profile.WithCharacterAppearance(
                profile.Appearance.WithMarkings(new List<Marking> { new(chosen.ID, colors) }));

            var mob = entMan.SpawnEntity(TestMob, map.GridCoords);
            humanoidSys.LoadProfile(mob, profile);

            Assert.That(entMan.HasComponent<WaggingComponent>(mob), Is.True,
                $"{TestSpecies} must carry WaggingComponent for this test.");
            Assert.That(LoadedTail(entMan, mob, chosen.ID), Is.Not.Null, "Precondition: the tail marking is loaded.");

            Assert.That(waggingSys.TryToggleWagging(mob), Is.True, "Wagging did not toggle on.");
            var wagging = LoadedTail(entMan, mob, chosen.ID + suffix);
            Assert.Multiple(() =>
            {
                Assert.That(wagging, Is.Not.Null, $"Wagging did not swap {chosen.ID} for {chosen.ID}{suffix}.");
                Assert.That(wagging?.MarkingColors, Is.EqualTo(colors),
                    "The animated variant must keep every colour of the static one.");
            });

            Assert.That(waggingSys.TryToggleWagging(mob), Is.True, "Wagging did not toggle off.");
            var stopped = LoadedTail(entMan, mob, chosen.ID);
            Assert.Multiple(() =>
            {
                Assert.That(stopped, Is.Not.Null, $"Wagging did not swap {chosen.ID}{suffix} back for {chosen.ID}.");
                Assert.That(stopped?.MarkingColors, Is.EqualTo(colors),
                    "The static variant must come back with every colour it had.");
            });

            entMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A garment that hides the Tail layer hides every sprite of the tail marking, the BEHIND half on TailBehind
    /// included: the client hides marking layers by the marking's own bodyPart, not by the layer it is drawn in.
    /// </summary>
    [Test]
    public async Task HiddenTailLayerHidesBehindHalfTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var cEntMan = client.EntMan;
        var humanoidSys = sEntMan.System<SharedHumanoidAppearanceSystem>();
        var inventory = sEntMan.System<InventorySystem>();
        var sprites = client.System<SpriteSystem>();

        // The client must be next to the mob, or PVS never sends it.
        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);

        MarkingPrototype tail = null;
        await server.WaitAssertion(() =>
        {
            tail = LiveSplitTail(pair, server.ProtoMan);
            Assert.That(tail, Is.Not.Null, $"No split tail marking is allowed on {TestSpecies}.");
        });

        if (tail == null)
        {
            await pair.CleanReturnAsync();
            return;
        }

        // Layer keys the client gives this marking's sprites, with the index each one takes its colour from.
        var keys = new List<(string Key, int Index)>();
        for (var i = 0; i < tail.Sprites.Count; i++)
        {
            if (tail.Sprites[i] is SpriteSpecifier.Rsi rsi)
                keys.Add(($"{tail.ID}-{rsi.RsiState}", i));
        }

        var colors = new List<Color>();
        for (var i = 0; i < tail.Sprites.Count; i++)
        {
            colors.Add(Palette[i % Palette.Length]);
        }

        var profile = MakeProfile(TestSpecies);
        profile = profile.WithCharacterAppearance(
            profile.Appearance.WithMarkings(new List<Marking> { new(tail.ID, colors) }));

        EntityUid mob = default;
        EntityUid coat = default;

        try
        {
            await server.WaitPost(() =>
            {
                mob = sEntMan.SpawnEntity(TestMob, Beside(player));
                humanoidSys.LoadProfile(mob, profile);
                coat = sEntMan.SpawnEntity(TestCoat, Beside(player));
            });
            await pair.RunTicksSync(5);

            var clientMob = pair.ToClientUid(mob);

            // Whatever colours the loaded mob ended up with, the client must draw them with colorLinks applied.
            List<Color> expected = null;
            await server.WaitAssertion(() =>
            {
                var loaded = LoadedTail(sEntMan, mob, tail.ID);
                Assert.That(loaded, Is.Not.Null, $"{tail.ID}: the loaded mob has no such tail marking.");
                expected = loaded == null ? new List<Color>() : tail.ResolveLinkedColors(loaded.MarkingColors);
            });

            await client.WaitAssertion(() =>
            {
                Assert.That(cEntMan.EntityExists(clientMob), Is.True, "Precondition: the client sees the mob.");
                Entity<SpriteComponent> sprite = (clientMob, cEntMan.GetComponent<SpriteComponent>(clientMob));
                Assert.Multiple(() =>
                {
                    Assert.That(keys, Has.Count.GreaterThan(1),
                        $"{tail.ID}: a split tail must have more than one sprite.");

                    foreach (var (key, index) in keys)
                    {
                        if (!sprites.TryGetLayer(sprite, key, out var layer, false))
                        {
                            Assert.Fail($"The mob has no tail layer {key}.");
                            continue;
                        }

                        Assert.That(layer.Visible, Is.True, $"Precondition: {key} is drawn before the coat is worn.");
                        if (index < expected.Count)
                            Assert.That(layer.Color, Is.EqualTo(expected[index]), $"{key} is drawn in the wrong colour.");
                    }
                });
            });

            await server.WaitAssertion(() =>
                Assert.That(inventory.TryEquip(mob, coat, OuterSlot, silent: true, force: true), Is.True,
                    "Could not equip the tail-hiding coat."));
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
                Assert.That(sEntMan.GetComponent<HumanoidAppearanceComponent>(mob).HiddenLayers,
                    Does.ContainKey(HumanoidVisualLayers.Tail),
                    "The coat did not hide the Tail layer."));

            await client.WaitAssertion(() =>
            {
                Entity<SpriteComponent> sprite = (clientMob, cEntMan.GetComponent<SpriteComponent>(clientMob));
                Assert.Multiple(() =>
                {
                    foreach (var (key, _) in keys)
                    {
                        if (!sprites.TryGetLayer(sprite, key, out var layer, false))
                        {
                            Assert.Fail($"The mob has no tail layer {key}.");
                            continue;
                        }

                        Assert.That(layer.Visible, Is.False,
                            $"{key} must be hidden while a garment hides the Tail layer, wherever the sprite is drawn.");
                    }
                });
            });

            await server.WaitAssertion(() =>
                Assert.That(inventory.TryUnequip(mob, OuterSlot, force: true), Is.True, "Could not remove the coat."));
            await pair.RunTicksSync(5);

            await client.WaitAssertion(() =>
            {
                Entity<SpriteComponent> sprite = (clientMob, cEntMan.GetComponent<SpriteComponent>(clientMob));
                Assert.Multiple(() =>
                {
                    foreach (var (key, _) in keys)
                    {
                        if (!sprites.TryGetLayer(sprite, key, out var layer, false))
                        {
                            Assert.Fail($"The mob has no tail layer {key}.");
                            continue;
                        }

                        Assert.That(layer.Visible, Is.True, $"{key} must come back once the coat is removed.");
                    }
                });
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (sEntMan.EntityExists(coat))
                    sEntMan.DeleteEntity(coat);
                if (sEntMan.EntityExists(mob))
                    sEntMan.DeleteEntity(mob);
            });
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Every non-test marking whose bodyPart is Tail, ordered by id so a failure names the same marking twice.</summary>
    private static List<MarkingPrototype> TailMarkings(TestPair pair, IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<MarkingPrototype>()
            .Where(m => !pair.IsTestPrototype(m) && m.BodyPart == HumanoidVisualLayers.Tail)
            .OrderBy(m => m.ID)
            .ToList();
    }

    /// <summary>
    /// A split tail the live tests can drive: allowed on the test species, in the Tail category so the wagging system and
    /// the marking points reach it, not force-coloured so the colours a profile saves survive LoadProfile, and with one
    /// colour box, so a character who saved a single colour keeps it on every half.
    /// </summary>
    private static MarkingPrototype LiveSplitTail(TestPair pair, IPrototypeManager proto)
    {
        return SplitTails(pair, proto, TestSpecies)
            .FirstOrDefault(m => !m.ForcedColoring
                                 && m.MarkingCategory == MarkingCategories.Tail
                                 && m.Sprites.Count > 1
                                 // One colour box: every sprite after the first follows another sprite's colour.
                                 && Enumerable.Range(1, m.Sprites.Count - 1).All(m.IsColorLinked));
    }

    /// <summary>Tail markings the split touched: at least one sprite is drawn on TailBehind.</summary>
    private static List<MarkingPrototype> SplitTails(TestPair pair, IPrototypeManager proto, string species = null)
    {
        return TailMarkings(pair, proto)
            .Where(m => DrawsBehind(m) && (species == null || AllowedOn(m, species)))
            .ToList();
    }

    /// <summary>Whether any sprite of the marking is redirected onto TailBehind.</summary>
    private static bool DrawsBehind(MarkingPrototype marking)
    {
        for (var i = 0; i < marking.Sprites.Count; i++)
        {
            if (LayerOf(marking, i) == nameof(HumanoidVisualLayers.TailBehind))
                return true;
        }

        return false;
    }

    /// <summary>The humanoid layer one sprite of a marking is drawn in: its layering entry, or the marking's bodyPart.</summary>
    private static string LayerOf(MarkingPrototype marking, int index)
    {
        if (marking.Sprites[index] is SpriteSpecifier.Rsi rsi
            && marking.Layering != null
            && marking.Layering.TryGetValue(rsi.RsiState, out var layer))
        {
            return layer;
        }

        return marking.BodyPart.ToString();
    }

    /// <summary>Sprites of a marking that carry a colour of their own, which is what the picker shows as colour boxes.</summary>
    private static int ColorBoxes(MarkingPrototype marking)
    {
        var boxes = 0;
        for (var i = 0; i < marking.Sprites.Count; i++)
        {
            if (!marking.IsColorLinked(i))
                boxes++;
        }

        return boxes;
    }

    private static bool AllowedOn(MarkingPrototype marking, string species)
    {
        return marking.SpeciesRestrictions == null || marking.SpeciesRestrictions.Contains(species);
    }

    private static string StateOf(MarkingPrototype marking, int index)
    {
        return marking.Sprites[index] is SpriteSpecifier.Rsi rsi ? rsi.RsiState : marking.Sprites[index].ToString();
    }

    /// <summary>Index of the sprite one colour-linked sprite takes its colour from, or -1.</summary>
    private static int ParentIndex(MarkingPrototype marking, int index)
    {
        if (marking.Sprites[index] is not SpriteSpecifier.Rsi rsi
            || marking.ColorLinks == null
            || !marking.ColorLinks.TryGetValue(rsi.RsiState, out var parent))
        {
            return -1;
        }

        for (var i = 0; i < marking.Sprites.Count; i++)
        {
            if (marking.Sprites[i] is SpriteSpecifier.Rsi candidate && candidate.RsiState == parent)
                return i;
        }

        return -1;
    }

    /// <summary>Runs <see cref="MarkingSet.EnsureValid"/> on one tail marking and returns what is left of it.</summary>
    private static Marking Validate(MarkingManager markings, Marking marking)
    {
        var set = new MarkingSet();
        set.AddBack(MarkingCategories.Tail, marking);
        set.EnsureValid(markings);
        return set.Markings[MarkingCategories.Tail].Single();
    }

    /// <summary>The tail marking of that id on a loaded humanoid, or null.</summary>
    private static Marking LoadedTail(IEntityManager entMan, EntityUid mob, string id)
    {
        return entMan.GetComponent<HumanoidAppearanceComponent>(mob).MarkingSet
            .TryGetCategory(MarkingCategories.Tail, out var markings)
            ? markings.FirstOrDefault(m => m.MarkingId == id)
            : null;
    }

    /// <summary>A point one tile from the player, well inside its view.</summary>
    private static MapCoordinates Beside(ConsentTestPlayer player)
    {
        return new MapCoordinates(player.Map.MapCoords.Position + new Vector2(1f, 0f), player.Map.MapCoords.MapId);
    }
}
