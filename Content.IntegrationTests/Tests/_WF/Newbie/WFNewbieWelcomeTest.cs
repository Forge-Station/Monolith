#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Client._WF.Newbie;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._WF.Newbie;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Newbie;

/// <summary>
/// A new player gets the welcome popup once per join, then the crew popup if it's their first hour, and can turn the
/// welcome off for good.
/// </summary>
[TestFixture]
[TestOf(typeof(Content.Server._WF.Newbie.WFNewbieWelcomeSystem))]
public sealed class WFNewbieWelcomeTest : GameTest
{
    private async Task<(int Welcome, int Crew)> OpenWindows()
    {
        var result = (0, 0);
        await Client.WaitPost(() =>
        {
            var root = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.ToList();
            result = (root.OfType<WFNewbieWelcomeWindow>().Count(), root.OfType<WFNewbieCrewWindow>().Count());
        });
        return result;
    }

    private async Task CloseAll()
    {
        await Client.WaitPost(() =>
        {
            var root = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.ToList();
            foreach (var window in root.OfType<BaseWindow>())
            {
                if (window is WFNewbieWelcomeWindow or WFNewbieCrewWindow)
                    window.Close();
            }
        });
        await RunTicksSync(2);
    }

    /// <summary>Plays the server's event into the client, as a later join would.</summary>
    private async Task Rejoin(bool fresh)
    {
        await Client.WaitPost(() =>
            CEntMan.EventBus.RaiseEvent(EventSource.Network, new WFNewbieWelcomeEvent(fresh)));
        await RunTicksSync(2);
    }

    [Test]
    public async Task WelcomeThenCrewTest()
    {
        Assert.That(ServerSession, Is.Not.Null, "This test needs a connected pair.");
        await OverrideCVar(Side.Client, WFNewbieCVars.ShowWelcome, true);
        await CloseAll();

        // The pool keeps popups off; turning them on greets this fresh player once.
        await OverrideCVar(Side.Server, WFNewbieCVars.Welcome, true);
        await Server.WaitPost(() => SEntMan.System<Content.Server._WF.Newbie.WFNewbieWelcomeSystem>().GreetAll());
        await RunTicksSync(5);
        var first = await OpenWindows();

        await Server.WaitPost(() => SEntMan.System<Content.Server._WF.Newbie.WFNewbieWelcomeSystem>().GreetAll());
        await RunTicksSync(5);
        var again = await OpenWindows();

        // The text is parsed as markup, not shown with its tags escaped.
        string? welcomeText = null, crewText = null;
        await Client.WaitPost(() => welcomeText = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
            .OfType<WFNewbieWelcomeWindow>().Single().FindControl<RichTextLabel>("WelcomeLabel").GetMessage());

        // Ticking "Don't show this again" and closing the welcome brings up the crew advice and saves the choice.
        var stopped = true;
        await Client.WaitPost(() =>
        {
            var welcome = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
                .OfType<WFNewbieWelcomeWindow>().Single();
            welcome.FindControl<CheckBox>("DontShowAgainCheckBox").Pressed = true;
            welcome.Close();
        });
        await RunTicksSync(2);
        var afterWelcome = await OpenWindows();
        await Client.WaitPost(() => crewText = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
            .OfType<WFNewbieCrewWindow>().SingleOrDefault()?.FindControl<RichTextLabel>("CrewLabel").GetMessage());
        await Client.WaitPost(() => stopped = !Client.CfgMan.GetCVar(WFNewbieCVars.ShowWelcome));
        await CloseAll();

        // On a later join the welcome stays shut; only a first-hour player still gets the crew advice.
        await Rejoin(false);
        var laterVeteran = await OpenWindows();
        await Rejoin(true);
        var laterFresh = await OpenWindows();
        await CloseAll();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo((1, 0)), "a fresh player didn't get the welcome alone first.");
            Assert.That(again, Is.EqualTo((1, 0)), "the welcome came twice in one join.");
            Assert.That(welcomeText, Does.Contain("[bold]").And.Not.Contain(@"\["), "the welcome shows its markup tags.");
            Assert.That(crewText, Does.Contain("[bold]").And.Not.Contain(@"\["), "the crew advice shows its markup tags.");
            Assert.That(afterWelcome, Is.EqualTo((0, 1)), "closing the welcome didn't bring up the crew advice.");
            Assert.That(stopped, Is.True, "\"Don't show this again\" wasn't saved.");
            Assert.That(laterVeteran, Is.EqualTo((0, 0)), "the welcome came back after being turned off.");
            Assert.That(laterFresh, Is.EqualTo((0, 1)), "turning the welcome off also hid the crew advice.");
        });
    }
}
