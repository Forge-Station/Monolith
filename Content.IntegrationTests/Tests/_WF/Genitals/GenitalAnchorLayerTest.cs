using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Content.Client._WF.Genitals;
using Content.Client.Humanoid;
using Content.Client.Lobby;
using Content.IntegrationTests.Pair;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>
/// Anatomy anchors in every humanoid sprite list, the species offsets, the keyed layers a creator doll gets,
/// and the rule that keeps tails off the anatomy - no tail marking draws a south-facing pixel inside the anatomy zone on a
/// layer above it.
/// </summary>
[TestFixture]
[TestOf(typeof(GenitalsVisualizerSystem))]
public sealed class GenitalAnchorLayerTest
{
    private const string BehindAnchor = "enum.GenitalVisualLayers.Behind";
    private const string UnderAnchor = "enum.GenitalVisualLayers.Under";
    private const string OverAnchor = "enum.GenitalVisualLayers.Over";
    private const string HumanoidLayer = "enum.HumanoidVisualLayers.";

    // Plain strings: the YAML linter validates static prototype-id fields.
    private const string HumanDoll = "MobHumanDummy";
    private const string ResomiDoll = "MobResomiDummy";
    private const string ResomiSpecies = "Resomi";
    private const string Boxers = "UndergarmentBottomBoxers";

    /// <summary>Layer key of the boxers marking sprite (HumanoidAppearanceSystem names marking layers id-state).</summary>
    private const string BoxersLayer = "UndergarmentBottomBoxers-boxers";

    /// <summary>Keyed layers above each anchor, bottom to top.</summary>
    private static readonly string[] BehindKeys = { "wf-gen-behind-breasts", "wf-gen-behind-testicles", "wf-gen-behind-penis" };

    private static readonly string[] UnderKeys =
    {
        "wf-gen-under-vagina", "wf-gen-under-testicles", "wf-gen-under-breasts",
        "wf-gen-under-sheath-outer", "wf-gen-under-sheath-inner", "wf-gen-under-penis",
    };

    private static readonly string[] OverKeys =
    {
        "wf-gen-over-vagina", "wf-gen-over-testicles", "wf-gen-over-breasts",
        "wf-gen-over-sheath-outer", "wf-gen-over-sheath-inner", "wf-gen-over-penis",
    };

    /// <summary>Under-set layers of the groin organs, which an undergarment bottom covers.</summary>
    private static readonly string[] GroinKeys =
    {
        "wf-gen-under-vagina", "wf-gen-under-testicles", "wf-gen-under-sheath-outer",
        "wf-gen-under-sheath-inner", "wf-gen-under-penis", "wf-gen-behind-testicles", "wf-gen-behind-penis",
    };

    /// <summary>
    /// Every entity prototype with HumanoidAppearance and a Sprite layer list containing Chest and undergarment layers has
    /// Behind directly after TailBehind and before the body, Under directly after gloves and Over directly after
    /// outerClothing, with both anchors below the tail layers. Every roundstart species body and doll is among the lists
    /// checked.
    /// </summary>
    [Test]
    public async Task AnchorLayersTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var factory = client.ResolveDependency<IComponentFactory>();

        await client.WaitAssertion(() =>
        {
            var spriteName = factory.GetComponentName(typeof(SpriteComponent));
            var humanoidName = factory.GetComponentName(typeof(HumanoidAppearanceComponent));
            var checkedIds = new HashSet<string>();

            Assert.Multiple(() =>
            {
                foreach (var entity in proto.EnumeratePrototypes<EntityPrototype>())
                {
                    if (entity.Abstract
                        || pair.IsTestPrototype(entity)
                        || !entity.Components.ContainsKey(humanoidName)
                        || !entity.Components.TryGetValue(spriteName, out var sprite))
                        continue;

                    var keys = ReadLayerKeys(sprite.Mapping);
                    if (IndexOf(keys, HumanoidLayer + "Chest") < 0)
                        continue;

                    // Lists without undergarment layers (monkeys, shade NPCs) never carry anatomy.
                    if (IndexOf(keys, HumanoidLayer + "UndergarmentBottom") < 0
                        && IndexOf(keys, HumanoidLayer + "UndergarmentTop") < 0
                        && IndexOf(keys, "undershirt") < 0)
                        continue;

                    checkedIds.Add(entity.ID);
                    CheckList(entity.ID, keys);
                }

                Assert.That(checkedIds, Is.Not.Empty, "No humanoid sprite lists were found.");

                // A new player species whose list lacks undergarment layers would otherwise be skipped silently.
                foreach (var species in proto.EnumeratePrototypes<SpeciesPrototype>())
                {
                    if (!species.RoundStart || pair.IsTestPrototype(species))
                        continue;

                    Assert.That(checkedIds, Does.Contain(species.Prototype.Id), $"{species.ID}: body {species.Prototype} was not checked.");
                    Assert.That(checkedIds, Does.Contain(species.DollPrototype.Id), $"{species.ID}: doll {species.DollPrototype} was not checked.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every speciesOffsets key names a species and a single region.</summary>
    [Test]
    public async Task SpeciesOffsetsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();

        await client.WaitAssertion(() =>
        {
            var settings = proto.Index(GenitalSettingsPrototype.DefaultId);
            Assert.Multiple(() =>
            {
                foreach (var (species, regions) in settings.SpeciesOffsets)
                {
                    Assert.That(proto.HasIndex(species), $"speciesOffsets names unknown species {species}.");
                    foreach (var region in regions.Keys)
                    {
                        Assert.That(region is GenitalRegion.Chest or GenitalRegion.Groin,
                            $"{species}: offset region {region} must be Chest or Groin.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A creator doll gets the keyed layers directly above the anchors, draws the sheath and arousal states, hides covered
    /// organs, hides undergarments only in Nude mode, applies species offsets, and draws nothing once the viewer turns
    /// adult content off.
    /// </summary>
    [Test]
    public async Task PreviewDollLayersTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var entMan = client.EntMan;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var sprites = client.System<SpriteSystem>();
        var humanoid = client.System<HumanoidAppearanceSystem>();
        var preview = client.System<GenitalPreviewSystem>();

        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);

        var profile = MakeProfile().WithGenitals(FullProfile());
        var withBoxers = profile.WithCharacterAppearance(profile.Appearance.WithMarkings(new List<Marking> { new(Boxers, 1) }));
        EntityUid doll = default;
        EntityUid resomi = default;

        try
        {
            // Resting, nothing worn: a sheathed penis shows the sheath instead of the shaft.
            await client.WaitPost(() =>
            {
                doll = entMan.SpawnEntity(HumanDoll, MapCoordinates.Nullspace);
                humanoid.LoadProfile(doll, profile);
            });
            await pair.RunTicksSync(1);

            await client.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<GenitalsComponent>(doll).IsPreview, Is.True);
                var sprite = DollSprite(entMan, doll);
                Assert.Multiple(() =>
                {
                    AssertKeyedAbove(sprites, sprite, GenitalVisualLayers.Behind, BehindKeys);
                    AssertKeyedAbove(sprites, sprite, GenitalVisualLayers.Under, UnderKeys);
                    AssertKeyedAbove(sprites, sprite, GenitalVisualLayers.Over, OverKeys);
                    AssertState(sprites, sprite, "wf-gen-under-sheath-outer", "sheath_normal_0_FRONT");
                    AssertState(sprites, sprite, "wf-gen-under-sheath-inner", "sheath_normal_0_FRONT_inner");
                    AssertHidden(sprites, sprite, "wf-gen-under-penis");
                    AssertHidden(sprites, sprite, "wf-gen-behind-penis");
                    AssertState(sprites, sprite, "wf-gen-under-testicles", "testicles_single_2_0_FRONT");
                    AssertState(sprites, sprite, "wf-gen-behind-testicles", "testicles_single_2_0_BEHIND");
                    AssertState(sprites, sprite, "wf-gen-under-vagina", "vagina_human_1_0_FRONT");
                    AssertState(sprites, sprite, "wf-gen-under-breasts", "breasts_pair_c_0_FRONT");
                    AssertHidden(sprites, sprite, "wf-gen-behind-breasts"); // no BEHIND art below cup D
                    foreach (var key in OverKeys)
                    {
                        AssertHidden(sprites, sprite, key);
                    }
                });
            });

            // Full arousal: the erect shaft over the sheath base; the vulva uses its aroused art from Full (settings).
            await client.WaitPost(() =>
            {
                preview.Arousal = ArousalState.Full;
                humanoid.LoadProfile(doll, profile);
            });

            await client.WaitAssertion(() =>
            {
                var sprite = DollSprite(entMan, doll);
                Assert.Multiple(() =>
                {
                    AssertState(sprites, sprite, "wf-gen-under-penis", "penis_knotted_1_1_FRONT");
                    AssertState(sprites, sprite, "wf-gen-behind-penis", "penis_knotted_1_0_BEHIND");
                    AssertState(sprites, sprite, "wf-gen-under-sheath-outer", "sheath_normal_1_FRONT");
                    AssertHidden(sprites, sprite, "wf-gen-under-sheath-inner");
                    AssertState(sprites, sprite, "wf-gen-under-vagina", "vagina_human_1_1_FRONT");
                });
            });

            // Boxers worn, As worn: the groin is covered, the chest is not.
            await client.WaitPost(() =>
            {
                preview.Arousal = null;
                humanoid.LoadProfile(doll, withBoxers);
            });

            await client.WaitAssertion(() =>
            {
                var sprite = DollSprite(entMan, doll);
                Assert.Multiple(() =>
                {
                    AssertVisible(sprites, sprite, BoxersLayer);
                    foreach (var key in GroinKeys)
                    {
                        AssertHidden(sprites, sprite, key);
                    }

                    AssertVisible(sprites, sprite, "wf-gen-under-breasts");
                    Assert.That(entMan.GetComponent<GenitalsComponent>(doll).LastHiddenUndergarments, Is.EqualTo(UndergarmentFlags.None));
                });
            });

            // Nude: the boxers are not drawn and the groin organs are.
            await client.WaitPost(() =>
            {
                preview.Mode = GenitalPreviewMode.Nude;
                humanoid.LoadProfile(doll, withBoxers);
            });

            await client.WaitAssertion(() =>
            {
                var sprite = DollSprite(entMan, doll);
                Assert.Multiple(() =>
                {
                    AssertHidden(sprites, sprite, BoxersLayer);
                    AssertVisible(sprites, sprite, "wf-gen-under-sheath-outer");
                    AssertVisible(sprites, sprite, "wf-gen-under-testicles");
                    AssertVisible(sprites, sprite, "wf-gen-under-vagina");
                    Assert.That(entMan.GetComponent<GenitalsComponent>(doll).LastHiddenUndergarments,
                        Is.EqualTo(UndergarmentFlags.TopRemoved | UndergarmentFlags.BottomRemoved));
                    AssertOffset(sprites, sprite, "wf-gen-under-breasts", Vector2.Zero);
                });
            });

            // Species offsets move every layer of the region.
            await client.WaitPost(() =>
            {
                resomi = entMan.SpawnEntity(ResomiDoll, MapCoordinates.Nullspace);
                humanoid.LoadProfile(resomi, MakeProfile(ResomiSpecies).WithGenitals(FullProfile()));
            });

            await client.WaitAssertion(() =>
            {
                var settings = proto.Index(GenitalSettingsPrototype.DefaultId);
                var expected = Vector2.Zero;
                if (settings.SpeciesOffsets.TryGetValue(ResomiSpecies, out var regions)
                    && regions.TryGetValue(GenitalRegion.Chest, out var pixels))
                    expected = new Vector2(pixels.X, pixels.Y) / EyeManager.PixelsPerMeter;

                AssertOffset(sprites, DollSprite(entMan, resomi), "wf-gen-under-breasts", expected);
            });

            // The viewer turns adult content off: nothing is drawn and the boxers come back.
            await GenitalConsentTestHelpers.SetPlayerConsent(pair);

            await client.WaitAssertion(() =>
            {
                var sprite = DollSprite(entMan, doll);
                Assert.Multiple(() =>
                {
                    foreach (var key in BehindKeys.Concat(UnderKeys).Concat(OverKeys))
                    {
                        AssertHidden(sprites, sprite, key);
                    }

                    AssertVisible(sprites, sprite, BoxersLayer);
                    Assert.That(entMan.GetComponent<GenitalsComponent>(doll).LastHiddenUndergarments, Is.EqualTo(UndergarmentFlags.None));
                });
            });
        }
        finally
        {
            await client.WaitPost(() =>
            {
                preview.Mode = GenitalPreviewMode.AsWorn;
                preview.Arousal = null;
                if (entMan.EntityExists(doll))
                    entMan.DeleteEntity(doll);
                if (entMan.EntityExists(resomi))
                    entMan.DeleteEntity(resomi);
            });
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The creator's doll, loaded as LobbyUIController.LoadProfileEntity loads it with DollWearsClothes: As worn wears the
    /// job clothes, which cover the groin; Underwear only and Nude load an undressed doll whose Under-set anatomy is drawn.
    /// </summary>
    [Test]
    public async Task PreviewModeClothesTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var entMan = client.EntMan;
        var sprites = client.System<SpriteSystem>();
        var preview = client.System<GenitalPreviewSystem>();
        var coverage = client.System<GenitalCoverageSystem>();
        var lobby = client.ResolveDependency<IUserInterfaceManager>().GetUIController<LobbyUIController>();

        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);

        var profile = MakeProfile().WithGenitals(FullProfile());
        EntityUid doll = default;

        try
        {
            foreach (var mode in new[] { GenitalPreviewMode.AsWorn, GenitalPreviewMode.UnderwearOnly, GenitalPreviewMode.Nude })
            {
                await client.WaitPost(() =>
                {
                    if (entMan.EntityExists(doll))
                        entMan.DeleteEntity(doll);

                    preview.Mode = mode;
                    doll = lobby.LoadProfileEntity(profile, null, preview.DollWearsClothes(true));
                });
                await pair.RunTicksSync(1);

                await client.WaitAssertion(() =>
                {
                    var sprite = DollSprite(entMan, doll);
                    var worn = entMan.GetComponent<InventoryComponent>(doll).Containers.Count(c => c.ContainedEntity != null);
                    Assert.Multiple(() =>
                    {
                        Assert.That(preview.DollWearsClothes(true), Is.EqualTo(mode == GenitalPreviewMode.AsWorn), $"{mode}: DollWearsClothes");
                        Assert.That(preview.DollWearsClothes(false), Is.False, $"{mode}: the clothes toggle off never dresses the doll.");

                        if (mode == GenitalPreviewMode.AsWorn)
                        {
                            Assert.That(coverage.IsRegionCoveredByClothing(doll, GenitalRegion.Groin), Is.True,
                                "As worn: the job clothes must cover the groin.");
                            foreach (var key in GroinKeys)
                            {
                                AssertHidden(sprites, sprite, key);
                            }
                        }
                        else
                        {
                            Assert.That(worn, Is.Zero, $"{mode}: the doll must wear nothing, or the Under set draws on top of its jumpsuit.");
                            AssertVisible(sprites, sprite, "wf-gen-under-sheath-outer");
                            AssertVisible(sprites, sprite, "wf-gen-under-testicles");
                            AssertVisible(sprites, sprite, "wf-gen-under-vagina");
                            AssertVisible(sprites, sprite, "wf-gen-under-breasts");
                        }
                    });
                });
            }
        }
        finally
        {
            await client.WaitPost(() =>
            {
                preview.Mode = GenitalPreviewMode.AsWorn;
                if (entMan.EntityExists(doll))
                    entMan.DeleteEntity(doll);
            });
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The tail layers draw above both anatomy anchors, so a tail may only reach exposed anatomy from behind the body: no
    /// sprite of a Tail-bodyPart marking that draws above the Under anchor may put a south-facing pixel inside the anatomy
    /// zone. The zone is the union of the south frame of every FRONT anatomy state - each shape at every size and arousal
    /// step, plus the sheath and slit art - shifted by the chest and groin offsets of each species the marking is allowed
    /// on, so no configuration of anatomy can be covered. South art outside the zone stays on the front layer where the
    /// artist drew it: Tools/_WF/tails/split_tails_batch.py --mode zone cuts each state along that line, moving the whole
    /// south frame behind the body only where the body was hiding it anyway. The south-facing frames of a state are the
    /// direction-0 frames of its RSI sheet, or the whole sheet for a single-direction state. Every sprite that is layered
    /// to TailBehind must also be colour-linked to an earlier sprite of the same marking, so the picker shows no extra
    /// colour box and MarkingSet.EnsureValid pads saved colours onto it. Markings in the Special category are exempt:
    /// bodyPart: Tail is used there to draw a head ornament on the top-most layer (the Skrell headdresses), so the art sits
    /// on the skull, never reaches anatomy, and behind the body would only end up behind the head.
    /// </summary>
    [Test]
    public async Task TailSouthFramesBehindTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var cache = client.ResolveDependency<IResourceCache>();
        var resMan = client.ResolveDependency<IResourceManager>();
        const string behindLayer = nameof(HumanoidVisualLayers.TailBehind);

        await client.WaitAssertion(() =>
        {
            var metas = new Dictionary<ResPath, RsiFrames>();
            var south = new Dictionary<(ResPath Rsi, string State), HashSet<(int X, int Y)>>();
            var checkedSprites = 0;

            var settings = proto.Index(GenitalSettingsPrototype.DefaultId);
            var everySpecies = proto.EnumeratePrototypes<SpeciesPrototype>()
                .Where(s => !pair.IsTestPrototype(s))
                .Select(s => s.ID)
                .ToList();
            var regions = ReadAnatomyZone(proto, pair, resMan, metas);
            var zones = new Dictionary<string, HashSet<(int X, int Y)>>();

            Assert.That(regions.Values.Sum(r => r.Count), Is.GreaterThan(0),
                "No FRONT anatomy art was read, so the anatomy zone would be empty and this test would pass on anything.");

            Assert.Multiple(() =>
            {
                foreach (var marking in proto.EnumeratePrototypes<MarkingPrototype>())
                {
                    if (pair.IsTestPrototype(marking) || marking.BodyPart != HumanoidVisualLayers.Tail)
                        continue;

                    if (marking.MarkingCategory == MarkingCategories.Special)
                        continue; // a head ornament on the tail layer, not a tail

                    var states = new List<string>();
                    foreach (var sprite in marking.Sprites)
                    {
                        if (sprite is SpriteSpecifier.Rsi rsi)
                            states.Add(rsi.RsiState);
                    }

                    for (var i = 0; i < marking.Sprites.Count; i++)
                    {
                        if (marking.Sprites[i] is not SpriteSpecifier.Rsi rsi)
                            continue;

                        var layer = marking.Layering != null && marking.Layering.TryGetValue(rsi.RsiState, out var named)
                            ? named
                            : marking.BodyPart.ToString();

                        if (layer == behindLayer)
                        {
                            // A BEHIND half must follow another sprite's colour, and that sprite must come first so a
                            // colour list saved before the split pads onto it.
                            var parent = marking.ColorLinks != null && marking.ColorLinks.TryGetValue(rsi.RsiState, out var link)
                                ? link
                                : null;
                            Assert.That(parent, Is.Not.Null,
                                $"{marking.ID}: sprite {rsi.RsiState} draws on {behindLayer} but has no colorLinks parent.");
                            if (parent == null)
                                continue;

                            var parentIndex = states.IndexOf(parent);
                            Assert.That(parentIndex, Is.GreaterThanOrEqualTo(0),
                                $"{marking.ID}: colorLinks sends {rsi.RsiState} to {parent}, which is not a sprite of this marking.");
                            Assert.That(parentIndex, Is.LessThan(i),
                                $"{marking.ID}: colorLinks parent {parent} of {rsi.RsiState} must come before it in sprites.");
                            continue;
                        }

                        checkedSprites++;
                        var path = GenitalSpriteResolver.RsiPath(rsi.RsiPath);
                        if (!cache.TryGetResource<RSIResource>(path, out var resource)
                            || !resource.RSI.TryGetState(rsi.RsiState, out _))
                        {
                            Assert.Fail($"{marking.ID}: RSI state {rsi.RsiState} of {rsi.RsiPath} does not exist.");
                            continue;
                        }

                        if (!south.TryGetValue((path, rsi.RsiState), out var pixels))
                        {
                            pixels = SouthPixels(resMan, metas, path, rsi.RsiState, out var error);
                            if (error != null)
                            {
                                Assert.Fail($"{marking.ID}: {error}");
                                continue;
                            }

                            south[(path, rsi.RsiState)] = pixels;
                        }

                        var key = string.Join(',', (marking.SpeciesRestrictions ?? everySpecies).OrderBy(id => id));
                        if (!zones.TryGetValue(key, out var zone))
                            zones[key] = zone = ZoneFor(regions, settings, marking.SpeciesRestrictions, everySpecies);

                        var meta = metas[path];
                        var over = ZoneOverlap(zone, pixels, meta.FrameWidth, meta.FrameHeight);
                        Assert.That(over, Is.Zero,
                            $"{marking.ID}: sprite {rsi.RsiState} draws on {layer}, above the anatomy anchors, and {over} of its "
                            + $"{pixels.Count} south-facing pixel(s) land inside the anatomy zone. Move those pixels into a state "
                            + $"layered to {behindLayer} (Tools/_WF/tails/split_tails_batch.py --mode zone --apply).");
                    }
                }

                Assert.That(checkedSprites, Is.GreaterThan(100), "Too few tail marking sprites were checked; the scan found nothing.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Frame size of one RSI and the frame count of each of its states' first (south) direction.</summary>
    private sealed record RsiFrames(int FrameWidth, int FrameHeight, Dictionary<string, int> SouthFrames);

    /// <summary>The anatomy art is drawn on one 32x32 tile centred on the mob, whatever a marking's own frame size is.</summary>
    private const int AnatomyTile = 32;

    /// <summary>
    /// Opaque pixels in the south-facing frames of one RSI state, in frame coordinates, with every frame of the facing
    /// laid over the same tile - a wag swings out of the first frame and still has to stay off anatomy. A state's sheet
    /// holds its frames direction-major with the south direction first (RSIResource.FoldDelays), so the south frames are
    /// the first ones on the sheet; a single-direction state draws its only frames for every facing, and meta.json then
    /// lists them as that one direction, which is the same range.
    /// </summary>
    private static HashSet<(int X, int Y)> SouthPixels(
        IResourceManager resMan,
        Dictionary<ResPath, RsiFrames> metas,
        ResPath rsi,
        string state,
        out string error)
    {
        var found = new HashSet<(int X, int Y)>();
        error = null;
        if (!metas.TryGetValue(rsi, out var meta))
        {
            if (!resMan.TryContentFileRead(rsi / "meta.json", out var manifest))
            {
                error = $"{rsi} has no meta.json.";
                return found;
            }

            using (manifest)
            {
                meta = ReadRsiFrames(manifest);
            }

            metas[rsi] = meta;
        }

        if (!meta.SouthFrames.TryGetValue(state, out var frames))
        {
            error = $"{rsi}: meta.json has no state {state}.";
            return found;
        }

        if (!resMan.TryContentFileRead(rsi / (state + ".png"), out var file))
        {
            error = $"{rsi}: state {state} has no {state}.png.";
            return found;
        }

        using var stream = file;
        using var image = Image.Load<Rgba32>(stream);
        var (width, height) = (meta.FrameWidth, meta.FrameHeight);
        var columns = image.Width / width;
        if (columns <= 0)
        {
            error = $"{rsi}: {state}.png is narrower than one {width}x{height} frame.";
            return found;
        }

        for (var i = 0; i < frames; i++)
        {
            var left = i % columns * width;
            var top = i / columns * height;
            if (left + width > image.Width || top + height > image.Height)
            {
                error = $"{rsi}: {state}.png is too small for {frames} south frame(s) of {width}x{height}.";
                return found;
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (image[left + x, top + y].A > 0)
                        found.Add((x, y));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The south-facing pixels exposed anatomy can occupy, per region, in the coordinates of the anatomy tile.
    ///
    /// Every FRONT state of every shape at every size and arousal step, plus the sheath and slit art, which is drawn from
    /// its own prototype rather than a shape. BEHIND art is left out: it draws below the body, where a tail on a tail
    /// layer is no worse for it than the body itself.
    /// </summary>
    private static Dictionary<GenitalRegion, HashSet<(int X, int Y)>> ReadAnatomyZone(
        IPrototypeManager proto,
        TestPair pair,
        IResourceManager resMan,
        Dictionary<ResPath, RsiFrames> metas)
    {
        var zone = new Dictionary<GenitalRegion, HashSet<(int X, int Y)>>
        {
            [GenitalRegion.Chest] = new(),
            [GenitalRegion.Groin] = new(),
        };

        void Add(GenitalRegion region, ResPath sprite, string state)
        {
            if (string.IsNullOrEmpty(state))
                return;

            var path = GenitalSpriteResolver.RsiPath(sprite);
            var pixels = SouthPixels(resMan, metas, path, state, out var error);
            if (error != null)
                return; // a shape may list a step whose art never shipped; GenitalShapeArtTest is what catches that

            Assert.That((metas[path].FrameWidth, metas[path].FrameHeight), Is.EqualTo((AnatomyTile, AnatomyTile)),
                $"{path}: anatomy art must be {AnatomyTile}x{AnatomyTile}, or the zone lands in the wrong place.");
            zone[region].UnionWith(pixels);
        }

        foreach (var shape in proto.EnumeratePrototypes<GenitalShapePrototype>())
        {
            if (pair.IsTestPrototype(shape))
                continue;

            foreach (var step in shape.Sizes)
            {
                Add(shape.Region, shape.Sprite, GenitalSpriteResolver.FormatState(shape, step, false, false));
            }

            foreach (var step in shape.ArousedSizes)
            {
                Add(shape.Region, shape.Sprite, GenitalSpriteResolver.FormatState(shape, step, true, false));
            }
        }

        foreach (var sheath in proto.EnumeratePrototypes<GenitalSheathPrototype>())
        {
            if (pair.IsTestPrototype(sheath))
                continue;

            foreach (var state in new[]
                     {
                         sheath.RetractedOuter, sheath.RetractedInner,
                         sheath.EmergingOuter, sheath.EmergingInner, sheath.ErectOuter,
                     })
            {
                Add(GenitalRegion.Groin, sheath.Sprite, state);
            }
        }

        return zone;
    }

    /// <summary>
    /// The zone one marking has to stay out of: each region shifted by the offsets of every species the marking is allowed
    /// on, so whichever species wears it and wherever that species keeps its anatomy, the tail cannot reach it. A marking
    /// with no speciesRestriction is measured against every species.
    /// </summary>
    private static HashSet<(int X, int Y)> ZoneFor(
        Dictionary<GenitalRegion, HashSet<(int X, int Y)>> regions,
        GenitalSettingsPrototype settings,
        List<string> allowed,
        List<string> everySpecies)
    {
        var zone = new HashSet<(int X, int Y)>();
        foreach (var species in allowed is { Count: > 0 } ? allowed : everySpecies)
        {
            settings.SpeciesOffsets.TryGetValue(species, out var offsets);
            foreach (var (region, pixels) in regions)
            {
                var (dx, dy) = (0, 0);
                if (offsets != null && offsets.TryGetValue(region, out var shift))
                    (dx, dy) = (shift.X, -shift.Y); // the prototype is in sprite pixels, +y up; sheet rows run down

                foreach (var (x, y) in pixels)
                {
                    var (nx, ny) = (x + dx, y + dy);
                    if (nx >= 0 && nx < AnatomyTile && ny >= 0 && ny < AnatomyTile)
                        zone.Add((nx, ny));
                }
            }
        }

        return zone;
    }

    /// <summary>
    /// How many of a marking frame's pixels land inside the anatomy zone. The anatomy tile is drawn centred on the
    /// marking's own frame, and a frame whose margin is a half pixel wide is counted at both roundings, so nothing slips
    /// out either way.
    /// </summary>
    private static int ZoneOverlap(HashSet<(int X, int Y)> zone, HashSet<(int X, int Y)> pixels, int width, int height)
    {
        var left = width - AnatomyTile;
        var top = height - AnatomyTile;
        var origins = new HashSet<(int X, int Y)> { (left / 2, top / 2), ((left + 1) / 2, (top + 1) / 2) };
        var count = 0;

        foreach (var (x, y) in pixels)
        {
            foreach (var (ox, oy) in origins)
            {
                if (zone.Contains((x - ox, y - oy)))
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    /// <summary>Frame size and per-state south frame count from an RSI meta.json.</summary>
    private static RsiFrames ReadRsiFrames(Stream manifest)
    {
        using var json = JsonDocument.Parse(manifest);
        var root = json.RootElement;
        var sizeNode = root.GetProperty("size");
        var width = sizeNode.GetProperty("x").GetInt32();
        var height = sizeNode.GetProperty("y").GetInt32();
        var frames = new Dictionary<string, int>();

        foreach (var state in root.GetProperty("states").EnumerateArray())
        {
            var name = state.GetProperty("name").GetString();
            if (name == null)
                continue;

            // One delay list per direction, south first; a state without delays is a single frame per direction.
            var count = 1;
            if (state.TryGetProperty("delays", out var delays) && delays.GetArrayLength() > 0)
                count = delays[0].GetArrayLength();

            frames[name] = count;
        }

        return new RsiFrames(width, height, frames);
    }

    /// <summary>
    /// Anchor order of one sprite list. Under sits directly after gloves and Over directly after outerClothing, so
    /// clothing, neckwear, hair and the tail layers all draw over exposed anatomy; a tail is kept off the anatomy by the
    /// anatomy zone of every tail marking's south frame living on TailBehind instead (TailSouthFramesBehindTest).
    /// </summary>
    private static void CheckList(string id, List<HashSet<string>> keys)
    {
        var behind = IndexOf(keys, BehindAnchor);
        var under = IndexOf(keys, UnderAnchor);
        var over = IndexOf(keys, OverAnchor);
        var tailBehind = IndexOf(keys, HumanoidLayer + "TailBehind");
        var chest = IndexOf(keys, HumanoidLayer + "Chest");
        var lastTail = Math.Max(IndexOf(keys, HumanoidLayer + "Tail"), IndexOf(keys, HumanoidLayer + "TailOversuit"));
        var bottom = IndexOf(keys, HumanoidLayer + "UndergarmentBottom");
        var underBase = bottom >= 0 ? bottom : IndexOf(keys, "undershirt");
        var gloves = IndexOf(keys, "gloves");
        var outer = IndexOf(keys, "outerClothing");
        var jumpsuit = IndexOf(keys, "jumpsuit");

        Assert.That(tailBehind, Is.GreaterThanOrEqualTo(0), $"{id}: the sprite list has no TailBehind layer.");
        Assert.That(behind, Is.EqualTo(tailBehind + 1), $"{id}: {BehindAnchor} must directly follow TailBehind.");
        Assert.That(behind, Is.LessThan(chest), $"{id}: {BehindAnchor} must come before the body (Chest).");

        Assert.That(gloves, Is.GreaterThanOrEqualTo(0), $"{id}: the sprite list has no gloves layer.");
        Assert.That(under, Is.EqualTo(gloves + 1),
            $"{id}: {UnderAnchor} must directly follow gloves, so shoes, belts, bags, hair, neckwear, outer clothing and tails draw over exposed anatomy.");
        Assert.That(outer, Is.GreaterThanOrEqualTo(0), $"{id}: the sprite list has no outerClothing layer.");
        Assert.That(over, Is.EqualTo(outer + 1), $"{id}: {OverAnchor} must directly follow outerClothing.");

        Assert.That(under, Is.GreaterThan(underBase), $"{id}: {UnderAnchor} must follow UndergarmentBottom (undershirt for silicon).");
        Assert.That(under, Is.LessThan(over), $"{id}: {UnderAnchor} must come below {OverAnchor}.");
        if (jumpsuit >= 0)
            Assert.That(jumpsuit, Is.LessThan(under), $"{id}: jumpsuit must come below {UnderAnchor}.");

        Assert.That(lastTail, Is.GreaterThanOrEqualTo(0), $"{id}: the sprite list has no Tail or TailOversuit layer.");
        Assert.That(lastTail, Is.GreaterThan(over),
            $"{id}: the tail layers must stay above both anatomy anchors, so tails still draw over cloaks facing north.");

        foreach (var anchor in new[] { BehindAnchor, UnderAnchor, OverAnchor })
        {
            Assert.That(keys.Count(k => k.Contains(anchor)), Is.EqualTo(1), $"{id}: {anchor} must be mapped exactly once.");
        }
    }

    /// <summary>The map keys of each layer of a Sprite component mapping, in list order.</summary>
    private static List<HashSet<string>> ReadLayerKeys(MappingDataNode sprite)
    {
        var result = new List<HashSet<string>>();
        if (!sprite.TryGet<SequenceDataNode>("layers", out var layers))
            return result;

        foreach (var node in layers)
        {
            var keys = new HashSet<string>();
            if (node is MappingDataNode layer && layer.TryGet<SequenceDataNode>("map", out var map))
            {
                foreach (var key in map)
                {
                    if (key is ValueDataNode value)
                        keys.Add(value.Value);
                }
            }

            result.Add(keys);
        }

        return result;
    }

    private static int IndexOf(List<HashSet<string>> keys, string key)
    {
        return keys.FindIndex(k => k.Contains(key));
    }

    private static Entity<SpriteComponent> DollSprite(IEntityManager entMan, EntityUid doll)
    {
        return (doll, entMan.GetComponent<SpriteComponent>(doll));
    }

    private static void AssertKeyedAbove(SpriteSystem sprites, Entity<SpriteComponent> sprite, GenitalVisualLayers anchor, string[] keys)
    {
        if (!sprites.LayerMapTryGet((sprite.Owner, sprite.Comp), anchor, out var anchorIndex, false))
        {
            Assert.Fail($"The doll has no {anchor} anchor layer.");
            return;
        }

        for (var i = 0; i < keys.Length; i++)
        {
            Assert.That(sprites.LayerMapTryGet((sprite.Owner, sprite.Comp), keys[i], out var index, false), Is.True, $"Missing keyed layer {keys[i]}.");
            Assert.That(index, Is.EqualTo(anchorIndex + 1 + i), $"{keys[i]} must sit {i + 1} above the {anchor} anchor.");
        }
    }

    private static void AssertState(SpriteSystem sprites, Entity<SpriteComponent> sprite, string key, string state)
    {
        if (!sprites.TryGetLayer((sprite.Owner, sprite.Comp), key, out var layer, false))
        {
            Assert.Fail($"Missing layer {key}.");
            return;
        }

        Assert.That(layer.Visible, Is.True, $"{key} should be drawn.");
        Assert.That(layer.State.Name, Is.EqualTo(state), $"{key} draws the wrong state.");
    }

    private static void AssertVisible(SpriteSystem sprites, Entity<SpriteComponent> sprite, string key)
    {
        if (!sprites.TryGetLayer((sprite.Owner, sprite.Comp), key, out var layer, false))
        {
            Assert.Fail($"Missing layer {key}.");
            return;
        }

        Assert.That(layer.Visible, Is.True, $"{key} should be drawn.");
    }

    private static void AssertHidden(SpriteSystem sprites, Entity<SpriteComponent> sprite, string key)
    {
        if (!sprites.TryGetLayer((sprite.Owner, sprite.Comp), key, out var layer, false))
        {
            Assert.Fail($"Missing layer {key}.");
            return;
        }

        Assert.That(layer.Visible, Is.False, $"{key} should not be drawn.");
    }

    private static void AssertOffset(SpriteSystem sprites, Entity<SpriteComponent> sprite, string key, Vector2 expected)
    {
        if (!sprites.TryGetLayer((sprite.Owner, sprite.Comp), key, out var layer, false))
        {
            Assert.Fail($"Missing layer {key}.");
            return;
        }

        Assert.That(layer.Visible, Is.True, $"{key} should be drawn.");
        Assert.That(layer.Offset.X, Is.EqualTo(expected.X).Within(0.0001f), $"{key}: wrong x offset.");
        Assert.That(layer.Offset.Y, Is.EqualTo(expected.Y).Within(0.0001f), $"{key}: wrong y offset.");
    }
}
