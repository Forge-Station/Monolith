#nullable enable
using System.Threading.Tasks;
using Content.Client._WF.Synthetics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.Synthetics;
using Robust.Client.Graphics;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>Playtest: "there's no overlay for opening the bay". The owner's client shows it while their bay is open.</summary>
public sealed class WFChassisBayOverlayTest : InteractionTest
{
    [Test]
    public async Task OwnerSeesBayOverlayTest()
    {
        var overlays = Client.ResolveDependency<IOverlayManager>();

        await Server.WaitPost(() =>
        {
            var chassis = SEntMan.EnsureComponent<WFChassisComponent>(SPlayer);
            chassis.BayOpen = true;
            SEntMan.Dirty(SPlayer, chassis);
        });
        await RunTicks(30);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetComponent(CPlayer, out WFChassisComponent? chassis) && chassis.BayOpen,
                "the open bay never reached the client.");
            Assert.That(overlays.TryGetOverlay(out WFChassisBayOverlay? overlay), "the bay overlay was not added.");
            Assert.That(overlay!.BayAlpha, Is.GreaterThan(0.5f), "the bay overlay is not fading in.");
        });

        await Server.WaitPost(() =>
        {
            var chassis = SEntMan.GetComponent<WFChassisComponent>(SPlayer);
            chassis.BayOpen = false;
            SEntMan.Dirty(SPlayer, chassis);
        });
        await RunTicks(90);

        await Client.WaitAssertion(() =>
        {
            Assert.That(overlays.HasOverlay<WFChassisBayOverlay>(), Is.False, "the bay overlay stayed after the bay closed.");
        });
    }
}
