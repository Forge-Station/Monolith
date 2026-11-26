using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.ExamineCard;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.ExamineCard;

[TestFixture]
public sealed class ExamineCardTest
{
    /// <summary>A real examine request from a connected client brings the card to that client.</summary>
    [Test]
    public async Task ExamineSendsCardTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var client = pair.Client;

        EntityUid player = default;
        EntityUid target = default;
        await server.WaitPost(() =>
        {
            player = pair.Player!.AttachedEntity!.Value;
            target = server.EntMan.SpawnEntity("MobHuman", server.EntMan.GetComponent<TransformComponent>(player).Coordinates);
        });
        await pair.RunTicksSync(10);

        var net = server.EntMan.GetNetEntity(target);
        await client.WaitAssertion(() =>
        {
            var examine = client.System<Client.Examine.ExamineSystem>();
            examine.DoExamine(client.EntMan.GetEntity(net));
            Assert.That(Descendants(examine.TooltipBox!).OfType<ExamineCardControl>(), Is.Not.Empty,
                "the card shows at once, before the server answers");
            Assert.That(examine.TooltipBox!.GetChild(0).Visible, Is.False, "the plain header with its little sprite is hidden");
            var healthText = Loc.GetString("health-examinable-verb-text");
            Assert.That(Descendants(examine.TooltipBox!).OfType<Client.Examine.ExamineButton>().Where(b => b.Verb.Text == healthText),
                Is.Empty, "the instant card already hides the health button the server's card folds in");
        });
        await pair.RunTicksSync(10);

        var cards = client.System<Client._WF.ExamineCard.ExamineCardSystem>();
        Assert.That(cards.Pending, Is.Null, $"card for {net} still pending: {cards.Pending?.Target}");
        Assert.That(cards.Shown, Is.EqualTo(1), "the tooltip took the card");
        Assert.That(cards.LastShown?.Target, Is.EqualTo(net), "the server answered for the examined character");

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var grandchild in Descendants(child))
                yield return grandchild;
        }
    }

}
