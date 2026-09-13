using System.Collections.Generic;
using System.Linq;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Saved marking colours survive a marking gaining colour-linked sprites, such as the split Protogen tails.</summary>
[TestFixture]
[TestOf(typeof(MarkingSet))]
public sealed class MarkingColorPaddingTest
{
    // An empty species list keeps these out of every picker and random appearance.
    [TestPrototypes]
    private const string Prototypes = @"
- type: marking
  id: WFTestUnlinkedTail
  bodyPart: Tail
  markingCategory: Tail
  speciesRestriction: []
  sprites:
  - sprite: _WF/Mobs/Customization/protogen_tails.rsi
    state: tail_protogen_FRONT
  - sprite: _WF/Mobs/Customization/protogen_tails.rsi
    state: tail_protogen_BEHIND

- type: marking
  id: WFTestPartlyLinkedTail
  bodyPart: Tail
  markingCategory: Tail
  speciesRestriction: []
  colorLinks:
    shark_tail_protogen_BEHIND: tail_protogen_FRONT
  sprites:
  - sprite: _WF/Mobs/Customization/protogen_tails.rsi
    state: tail_protogen_FRONT
  - sprite: _WF/Mobs/Customization/protogen_tails.rsi
    state: shark_tail_protogen_FRONT
  - sprite: _WF/Mobs/Customization/protogen_tails.rsi
    state: shark_tail_protogen_BEHIND

- type: marking
  id: WFTestForwardLinkedTail
  bodyPart: Tail
  markingCategory: Tail
  speciesRestriction: []
  colorLinks:
    tail_protogen_BEHIND: shark_tail_protogen_FRONT
    shark_tail_protogen_FRONT: tail_protogen_FRONT
  sprites:
  - sprite: _WF/Mobs/Customization/protogen_tails.rsi
    state: tail_protogen_FRONT
  - sprite: _WF/Mobs/Customization/protogen_tails.rsi
    state: tail_protogen_BEHIND
  - sprite: _WF/Mobs/Customization/protogen_tails.rsi
    state: shark_tail_protogen_FRONT
";

    private static readonly string[] ProtogenTails = { "ProtogenTail", "ProtogenTailBushy", "ProtogenTailShark" };

    private static readonly Color Saved = Color.FromHex("#3A7BD5");
    private static readonly Color Other = Color.FromHex("#D55E3A");

    /// <summary>Runs <see cref="MarkingSet.EnsureValid"/> on one tail marking and returns what is left of it.</summary>
    private static Marking Validate(MarkingManager markings, Marking marking)
    {
        var set = new MarkingSet();
        set.AddBack(MarkingCategories.Tail, marking);
        set.EnsureValid(markings);
        return set.Markings[MarkingCategories.Tail].Single();
    }

    [Test]
    public async Task ProtogenTailsAreSplit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var id in ProtogenTails)
            {
                var proto = protoMan.Index<MarkingPrototype>(id);
                Assert.That(proto.Sprites, Has.Count.EqualTo(2), id);

                // FRONT stays at index 0 so a saved single colour maps onto it.
                var front = proto.Sprites[0] as SpriteSpecifier.Rsi;
                var behind = proto.Sprites[1] as SpriteSpecifier.Rsi;
                Assert.That(front, Is.Not.Null, id);
                Assert.That(behind, Is.Not.Null, id);
                Assert.That(front!.RsiState, Does.EndWith("_FRONT"), id);
                Assert.That(behind!.RsiState, Does.EndWith("_BEHIND"), id);

                Assert.That(proto.Layering, Is.Not.Null, id);
                Assert.That(proto.Layering![front.RsiState], Is.EqualTo(nameof(HumanoidVisualLayers.TailOversuit)), id);
                Assert.That(proto.Layering[behind.RsiState], Is.EqualTo(nameof(HumanoidVisualLayers.TailBehind)), id);

                Assert.That(proto.ColorLinks, Is.Not.Null, id);
                Assert.That(proto.ColorLinks![behind.RsiState], Is.EqualTo(front.RsiState), id);
                Assert.That(proto.IsColorLinked(0), Is.False, id);
                Assert.That(proto.IsColorLinked(1), Is.True, id);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EnsureValidPadsLinkedColours()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var markings = server.ResolveDependency<MarkingManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var id in ProtogenTails)
            {
                // A tail saved with one colour before the split keeps it on both halves, and its visibility.
                var tail = Validate(markings, new Marking(id, new List<Color> { Saved }) { Visible = false });
                Assert.That(tail.MarkingColors, Is.EqualTo(new[] { Saved, Saved }), id);
                Assert.That(tail.Visible, Is.False, id);
            }

            // Two of three colours saved: only the missing sprite is filled, from its linked parent.
            var partial = Validate(markings, new Marking("WFTestPartlyLinkedTail", new List<Color> { Saved, Other }));
            Assert.That(partial.MarkingColors, Is.EqualTo(new[] { Saved, Other, Saved }));

            // A list that already matches is left alone.
            var valid = Validate(markings, new Marking("ProtogenTail", new List<Color> { Saved, Other }));
            Assert.That(valid.MarkingColors, Is.EqualTo(new[] { Saved, Other }));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EnsureValidStillResets()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var markings = server.ResolveDependency<MarkingManager>();

        await server.WaitAssertion(() =>
        {
            // A sprite-count change without links.
            Assert.That(Validate(markings, new Marking("WFTestUnlinkedTail", new List<Color> { Saved })).MarkingColors,
                Is.EqualTo(new[] { Color.White, Color.White }));

            // A missing sprite that has no link.
            Assert.That(Validate(markings, new Marking("WFTestPartlyLinkedTail", new List<Color> { Saved })).MarkingColors,
                Is.EqualTo(new[] { Color.White, Color.White, Color.White }));

            // A missing sprite linked to a parent that is missing too.
            Assert.That(Validate(markings, new Marking("WFTestForwardLinkedTail", new List<Color> { Saved })).MarkingColors,
                Is.EqualTo(new[] { Color.White, Color.White, Color.White }));

            // A list that shrinks, and an empty list.
            Assert.That(Validate(markings, new Marking("ProtogenTail", new List<Color> { Saved, Other, Saved })).MarkingColors,
                Is.EqualTo(new[] { Color.White, Color.White }));
            Assert.That(Validate(markings, new Marking("ProtogenTail", new List<Color>())).MarkingColors,
                Is.EqualTo(new[] { Color.White, Color.White }));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ResolveLinkedColorsPads()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            ProtoId<MarkingPrototype> tailId = "ProtogenTail";
            var tail = protoMan.Index(tailId);
            var input = new List<Color> { Saved };
            Assert.That(tail.ResolveLinkedColors(input), Is.EqualTo(new[] { Saved, Saved }));
            Assert.That(input, Is.EqualTo(new[] { Saved }), "the input list must not change");

            // A stale colour on the linked sprite follows its parent.
            Assert.That(tail.ResolveLinkedColors(new List<Color> { Saved, Other }), Is.EqualTo(new[] { Saved, Saved }));

            // Missing sprites without a link are padded white.
            ProtoId<MarkingPrototype> partialId = "WFTestPartlyLinkedTail";
            var partial = protoMan.Index(partialId);
            Assert.That(partial.ResolveLinkedColors(new List<Color> { Saved }), Is.EqualTo(new[] { Saved, Color.White, Saved }));

            // A marking without links is returned as is.
            ProtoId<MarkingPrototype> unlinkedId = "WFTestUnlinkedTail";
            var unlinked = protoMan.Index(unlinkedId);
            Assert.That(unlinked.ResolveLinkedColors(new List<Color> { Saved }), Is.EqualTo(new[] { Saved }));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProfileValidationKeepsSavedTailColour()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var appearance = new HumanoidCharacterAppearance(
                HairStyles.DefaultHairStyle,
                Color.Black,
                HairStyles.DefaultFacialHairStyle,
                Color.Black,
                Color.Black,
                Color.FromHex("#FFDA93"),
                new List<Marking> { new("ProtogenTail", new List<Color> { Saved }) });

            var valid = HumanoidCharacterAppearance.EnsureValid(appearance, "Protogen", Sex.Male);
            var tail = valid.Markings.Single(m => m.MarkingId == "ProtogenTail");
            Assert.That(tail.MarkingColors, Is.EqualTo(new[] { Saved, Saved }));
        });

        await pair.CleanReturnAsync();
    }
}
