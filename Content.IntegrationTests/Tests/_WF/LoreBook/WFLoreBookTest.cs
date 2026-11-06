#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Client._WF.LoreBook;
using Content.Client.Guidebook;
using Content.Client.Guidebook.Controls;
using Content.Client.Guidebook.Richtext;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared._WF.LoreBook;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.LoreBook;

/// <summary>The lore book holds the lore chapters, the guidebook doesn't, and every link between them resolves.</summary>
[TestFixture]
public sealed class WFLoreBookTest
{
    private static readonly Regex LinkPattern = new("\\[(textlink|lorelink)=\"[^\"]*\" link=\"([^\"]+)\"\\]");

    /// <summary>However hard a page is corrupted, it still parses, and some of its words turn to glyphs.</summary>
    [Test]
    public async Task CorruptedPagesStillParse()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        await client.WaitIdleAsync();
        var lore = client.System<WFLoreBookSystem>();
        var prototypes = client.ResolveDependency<IPrototypeManager>();
        var parsing = client.ResolveDependency<DocumentParsingManager>();
        var resources = client.ResolveDependency<IResourceManager>();

        await client.WaitAssertion(() =>
        {
            var glyphs = 0;
            var rng = new System.Random(5);
            foreach (var entry in prototypes.EnumeratePrototypes<GuideEntryPrototype>().Where(e => e.Unease > 0f && lore.IsLore(e.ID)))
            {
                using var file = resources.ContentFileReadText(entry.Text);
                var text = file.ReadToEnd();
                for (var i = 0; i < 20; i++)
                {
                    var corrupt = WFLoreBookWindow.Distort(rng, text, 0.45f, 0.3f, 0.35f);
                    glyphs += Regex.Matches(corrupt, "WFLoreGlyphs").Count;
                    Assert.That(parsing.TryAddMarkup(new Robust.Client.UserInterface.Controls.BoxContainer(), corrupt), Is.True,
                        $"{entry.ID} didn't parse once corrupted: {corrupt}");
                }
            }

            Assert.That(glyphs, Is.GreaterThan(0), "no word ever turned to glyphs.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LoreAndGuideAreSplit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        await client.WaitIdleAsync();
        var lore = client.System<WFLoreBookSystem>();
        var prototypes = client.ResolveDependency<IPrototypeManager>();

        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var id in new[] { "BasicLore", "Timeline", "Factions", "TSFMC_Entry", "TSFMC_SOP", "WFAsf", "MonoStarSystem", "Aerumna", "WFLoreCarcinoma", "WFLoreThreats", "WFLoreXenonids", "WFLoreForbidden", "WFLoreUnmooring", "WFLoreChoir" })
                    Assert.That(lore.IsLore(id), $"{id} is a lore page.");

                foreach (var id in new[] { "WFAds", "Smuggling", "WFFactionsGuide", "WFLoreBookGuide", "WFPlanetFlight", "WFRemadeGuide" })
                    Assert.That(lore.IsLore(id), Is.False, $"{id} stays in the guidebook.");

                foreach (var link in prototypes.EnumeratePrototypes<WFLoreLinkPrototype>())
                    Assert.That(lore.IsLore(link.Entry), $"{link.ID} opens {link.Entry}, which isn't a lore page.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LinksResolve()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        await client.WaitIdleAsync();
        var lore = client.System<WFLoreBookSystem>();
        var prototypes = client.ResolveDependency<IPrototypeManager>();
        var resources = client.ResolveDependency<IResourceManager>();
        var parser = client.ResolveDependency<DocumentParsingManager>();

        var pages = prototypes.EnumeratePrototypes<GuideEntryPrototype>()
            .Where(p => lore.IsLore(p.ID) || p.ID is "WFLoreBookGuide" or "WFFactionsGuide")
            .Select(p => p.ID)
            .ToList();
        Assert.That(pages, Has.Count.GreaterThan(20));

        foreach (var id in pages)
        {
            await client.WaitAssertion(() =>
            {
                using var reader = resources.ContentFileReadText(prototypes.Index<GuideEntryPrototype>(id).Text);
                var text = reader.ReadToEnd();
                var document = new Document();
                Assert.That(parser.TryAddMarkup(document, text), $"{id} doesn't parse.");
                Assert.That(Errors(document), Is.Empty, $"{id} has a guidebook error in it.");

                foreach (Match match in LinkPattern.Matches(text))
                    Assert.That(prototypes.HasIndex<GuideEntryPrototype>(match.Groups[2].Value), $"{id} links to a missing page {match.Groups[2].Value}.");
            });

            await client.WaitRunTicks(1);
        }

        // Every lore link anywhere points into the book.
        await client.WaitAssertion(() =>
        {
            foreach (var entry in prototypes.EnumeratePrototypes<GuideEntryPrototype>())
            {
                using var reader = resources.ContentFileReadText(entry.Text);
                foreach (Match match in LinkPattern.Matches(reader.ReadToEnd()))
                {
                    if (match.Groups[1].Value == "lorelink")
                        Assert.That(lore.IsLore(match.Groups[2].Value), $"{entry.ID} has a lore link to {match.Groups[2].Value}, which isn't a lore page.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EntitiesAndGuidebookOpenTheBook()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        await client.WaitIdleAsync();
        var lore = client.System<WFLoreBookSystem>();
        var ui = client.ResolveDependency<IUserInterfaceManager>();

        await client.WaitAssertion(() =>
        {
            // A child of a linked prototype inherits its page.
            var uniform = client.EntMan.Spawn("ClothingUniformTSFMCCrocus", MapCoordinates.Nullspace);
            Assert.That(lore.TryGetEntityLore(uniform, out var entry), "The Crocus uniform inherits the TSF page from its parent.");
            Assert.That(entry.Id, Is.EqualTo("TSFMC_Entry"));
            client.EntMan.DeleteEntity(uniform);

            var book = ui.GetUIController<WFLoreBookUIController>();
            var guide = ui.GetUIController<GuidebookUIController>();

            book.Open("Kyphrus");
            var window = book.Window;
            Assert.That(window, Is.Not.Null, "The lore book opened.");
            Assert.That(window!.Current?.Id, Is.EqualTo("Kyphrus"));

            book.Open("WFLoreXenonids");
            Assert.That(window.Current?.Id, Is.EqualTo("WFLoreXenonids"), "Opening again turns the open book.");

            // A guide link from inside the book leaves it alone.
            window.HandleClick("WFLoreCarcinoma");
            Assert.That(window.Current?.Id, Is.EqualTo("WFLoreCarcinoma"));

            book.Toggle();
            Assert.That(book.Window, Is.Null, "Toggle closes the book.");

            // A guidebook request for lore only opens the lore book at that page.
            guide.OpenGuidebook(new List<ProtoId<GuideEntryPrototype>> { "Merak" });
            Assert.That(book.Window?.Current?.Id, Is.EqualTo("Merak"), "The guidebook handed a lore page to the lore book.");
            book.Toggle();
        });

        await pair.CleanReturnAsync();
    }

    private static List<GuidebookError> Errors(Control control)
    {
        var found = new List<GuidebookError>();
        foreach (var child in control.Children)
        {
            if (child is GuidebookError error)
                found.Add(error);

            found.AddRange(Errors(child));
        }

        return found;
    }

    /// <summary>A heading behind a marker comment stays plain: headings print glyph tags as text.</summary>
    [Test]
    public void MarkedHeadingsNeverTurnToGlyphs()
    {
        const string page = "<!-- WOLFGATE END -->### With Regards to Prisoners\nSome body words follow here.";
        var corrupt = WFLoreBookWindow.Distort(new System.Random(1), page, 0f, 0f, 1f);
        var heading = corrupt.Split('\n')[0];
        Assert.That(heading, Does.Not.Contain("WFLoreGlyphs"), $"a marked heading turned to glyphs: {heading}");
        Assert.That(corrupt.Split('\n')[1], Does.Contain("WFLoreGlyphs"), "the body never turned to glyphs.");
    }
}
