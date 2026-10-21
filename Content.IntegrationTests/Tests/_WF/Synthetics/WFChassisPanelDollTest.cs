#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Client._WF.Synthetics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._WF.CombatConsole;
using Content.Shared._Shitmed.Targeting;
using Robust.Client.UserInterface.Controls;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>The chassis panel's body doll, which needs a connected client for its sprite systems.</summary>
[TestFixture]
public sealed class WFChassisPanelDollTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    /// <summary>Clicking a limb on the doll lights its row; clicking a row lights the limb.</summary>
    [Test]
    public async Task DollClicksSelectRowsTest()
    {
        WFChassisPanelWindow? window = null;
        await Client.WaitPost(() =>
        {
            window = new WFChassisPanelWindow();
            window.OpenCentered();
            window.Update(WFChassisPanelWindowTest.State());
        });
        await RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var panel = window!;
            var doll = panel.Doll;
            Assert.That(doll, Is.Not.Null, "a connected client draws the doll.");
            Assert.That(WFChassisPanelWindowTest.Descendants(panel.TabHost.GetChild(0)), Does.Contain(doll),
                "the doll lives in the parts tab.");
            Assert.That(WFChassisPanelWindowTest.Descendants(panel.TabHost.GetChild(1)), Does.Not.Contain(doll));

            int Lit() => WFChassisPanelWindowTest.Descendants(doll!).OfType<TextureRect>().Count(r => r.Visible);
            BaseButton Button(string style) => WFChassisPanelWindowTest.Descendants(doll!).OfType<BaseButton>()
                .Single(b => b.StyleClasses.Contains(style));

            Assert.That(Lit(), Is.EqualTo(0));

            WFButtonTestInput.Click(Button("TargetDollButtonLeftArm"));
            Assert.That(panel.Selected, Is.EqualTo(TargetBodyPart.LeftArm));
            Assert.That(WFChassisPanelWindowTest.Background(panel.Parts[2].Cells[0]),
                Is.EqualTo(WFChassisPanelWindowTest.SelectedBackground));
            Assert.That(Lit(), Is.EqualTo(1));

            WFButtonTestInput.Click(Button("TargetDollButtonGroin"));
            Assert.That(panel.Selected, Is.EqualTo(TargetBodyPart.Torso), "the groin picks the torso row.");

            WFChassisPanelWindowTest.ClickCell(panel.Parts[3].Cells[0]);
            Assert.That(panel.Selected, Is.EqualTo(TargetBodyPart.RightArm));
            Assert.That(Lit(), Is.EqualTo(1));

            // The same limb again clears the pick.
            WFButtonTestInput.Click(Button("TargetDollButtonRightArm"));
            Assert.That(panel.Selected, Is.Null);
            Assert.That(Lit(), Is.EqualTo(0));

            // New states redraw the doll without trouble, and a shorter list is fine.
            var next = WFChassisPanelWindowTest.State();
            next.Parts.RemoveAt(9);
            panel.Update(next);
            panel.Update(WFChassisPanelWindowTest.State(bayOpen: true));
        });

        await Client.WaitPost(() => window!.Dispose());
    }
}
