using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Prototypes;
using Robust.Client.ResourceManagement;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Every genital shape and sheath state resolves to art that exists; step mapping helpers.</summary>
[TestFixture]
[TestOf(typeof(GenitalSpriteResolver))]
public sealed class GenitalShapeArtTest
{
    [Test]
    public async Task ShapeStatesExistTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var cache = client.ResolveDependency<IResourceCache>();
        var loc = client.ResolveDependency<ILocalizationManager>();

        await client.WaitAssertion(() =>
        {
            var shapes = proto.EnumeratePrototypes<GenitalShapePrototype>().Where(s => !pair.IsTestPrototype(s)).ToList();
            Assert.That(shapes, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                foreach (var shape in shapes)
                {
                    Assert.That(loc.HasString(shape.Name.Id), $"{shape.ID}: missing locale key {shape.Name.Id}.");
                    if (shape.ExamineName is { } examine)
                        Assert.That(loc.HasString(examine.Id), $"{shape.ID}: missing locale key {examine.Id}.");

                    if (!cache.TryGetResource<RSIResource>(GenitalSpriteResolver.RsiPath(shape.Sprite), out var rsi))
                    {
                        Assert.Fail($"{shape.ID}: RSI {shape.Sprite} not found.");
                        continue;
                    }

                    if (shape.Sizes.Count == 0)
                    {
                        Assert.Fail($"{shape.ID} lists no sizes.");
                        continue;
                    }

                    // Every listed art step exists on its layer.
                    CheckListed(rsi, shape, shape.Sizes, false, false);
                    CheckListed(rsi, shape, shape.ArousedSizes, true, false);
                    CheckListed(rsi, shape, shape.BehindSizes, false, true);
                    CheckListed(rsi, shape, shape.BehindArousedSizes, true, true);

                    foreach (var target in shape.StateOverrides.Values)
                    {
                        Assert.That(rsi.RSI.TryGetState(target, out _), $"{shape.ID}: stateOverrides target {target} does not exist.");
                    }

                    if (shape.ArtSteps is { } artSteps)
                    {
                        foreach (var artStep in artSteps)
                        {
                            Assert.That(shape.Sizes, Does.Contain(artStep), $"{shape.ID}: artSteps entry {artStep} has no FRONT art.");
                        }
                    }

                    // Every logical step the game can request resolves to real art; FRONT always resolves.
                    var maxStep = shape.ArtSteps?.Count ?? shape.Sizes.Max();
                    for (var step = 1; step <= maxStep; step++)
                    {
                        foreach (var aroused in new[] { false, true })
                        {
                            foreach (var behind in new[] { false, true })
                            {
                                if (GenitalSpriteResolver.TryGetState(shape, step, aroused, behind, out var state))
                                {
                                    Assert.That(rsi.RSI.TryGetState(state, out _),
                                        $"{shape.ID} step {step} (aroused {aroused}, behind {behind}) resolves to missing state {state}.");
                                }
                                else
                                {
                                    Assert.That(behind, Is.True, $"{shape.ID} step {step}: FRONT art did not resolve.");
                                }
                            }
                        }
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SheathStatesExistTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var cache = client.ResolveDependency<IResourceCache>();

        await client.WaitAssertion(() =>
        {
            var sheaths = proto.EnumeratePrototypes<GenitalSheathPrototype>().Where(s => !pair.IsTestPrototype(s)).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(sheaths.Select(s => s.Type), Is.EquivalentTo(new[] { SheathType.Sheath, SheathType.Slit }),
                    "There must be exactly one genitalSheath per sheath type.");

                foreach (var sheath in sheaths)
                {
                    if (!cache.TryGetResource<RSIResource>(GenitalSpriteResolver.RsiPath(sheath.Sprite), out var rsi))
                    {
                        Assert.Fail($"{sheath.ID}: RSI {sheath.Sprite} not found.");
                        continue;
                    }

                    var states = new[] { sheath.RetractedOuter, sheath.RetractedInner, sheath.EmergingOuter, sheath.EmergingInner, sheath.ErectOuter };
                    foreach (var state in states)
                    {
                        if (state == null)
                            continue;

                        Assert.That(rsi.RSI.TryGetState(state, out _), $"{sheath.ID}: state {state} does not exist in {sheath.Sprite}.");
                    }
                }

                // The slit vulva reuses the ported slit line.
                Assert.That(proto.TryIndex(VaginaSlit, out var slit), $"{VaginaSlit.Id} is missing.");
                if (slit == null)
                    return;

                Assert.That(GenitalSpriteResolver.TryGetState(slit, 1, false, false, out var slitState), Is.True);
                Assert.That(cache.TryGetResource<RSIResource>(GenitalSpriteResolver.RsiPath(slit.Sprite), out var slitRsi), Is.True);
                Assert.That(slitRsi!.RSI.TryGetState(slitState!, out _), $"Slit vulva state {slitState} does not exist.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StepMappingTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var settings = GenitalProfileValidator.GetSettings(pair.Server.ResolveDependency<IPrototypeManager>());
        var lengths = new (int Cm, int Step)[] { (5, 1), (17, 1), (18, 2), (24, 2), (25, 3), (32, 3), (33, 4), (44, 4), (45, 5), (60, 5) };

        Assert.Multiple(() =>
        {
            // Missing art steps: the highest available not above the request, else the lowest.
            Assert.That(GenitalSpriteResolver.ResolveStep(new[] { 1, 2, 3, 5 }, 4), Is.EqualTo(3));
            Assert.That(GenitalSpriteResolver.ResolveStep(new[] { 1, 2, 3, 5 }, 9), Is.EqualTo(5));
            Assert.That(GenitalSpriteResolver.ResolveStep(new[] { 4, 5, 6 }, 3), Is.EqualTo(4));
            Assert.That(GenitalSpriteResolver.ResolveStep(System.Array.Empty<int>(), 2), Is.EqualTo(2));

            // Length to sprite step and the migration inverse.
            foreach (var (cm, step) in lengths)
            {
                Assert.That(GenitalStateBuilder.LengthToStep(cm, settings), Is.EqualTo(step), $"{cm} cm");
            }

            for (var step = 1; step <= 5; step++)
            {
                Assert.That(GenitalStateBuilder.LengthToStep(GenitalStateBuilder.StepToLength(step), settings), Is.EqualTo(step));
            }
        });

        await pair.CleanReturnAsync();
    }

    private static void CheckListed(RSIResource rsi, GenitalShapePrototype shape, List<int> steps, bool aroused, bool behind)
    {
        foreach (var step in steps)
        {
            var state = GenitalSpriteResolver.FormatState(shape, step, aroused, behind);
            Assert.That(rsi.RSI.TryGetState(state, out _),
                $"{shape.ID}: listed step {step} (aroused {aroused}, behind {behind}) needs missing state {state}.");
        }
    }
}
