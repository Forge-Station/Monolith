using Content.Client._Forge.Radio.Ui;
using Content.Client._NC.Radio.UI;
using Content.Shared._Forge.Radio;
using Content.Shared._NC.Radio;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests.UserInterface;

[TestFixture]
public sealed class RadioFrequencyInputTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task StateUpdatesPreserveDraftUntilSubmitted(bool handheld)
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Client.WaitAssertion(() =>
        {
            using var menu = handheld
                ? (Control) new HandheldRadioMenu()
                : new ConfigurableEncryptionKeyMenu();
            string submitted = null;
            Action<int> update;
            if (menu is HandheldRadioMenu radio)
            {
                radio.OnFrequencyChanged += text => submitted = text;
                update = frequency => radio.Update(new HandheldRadioBoundUIState(true, true, frequency, 1000, 9999));
            }
            else
            {
                var key = (ConfigurableEncryptionKeyMenu) menu;
                key.OnFrequencyChanged += text => submitted = text;
                update = frequency => key.Update(new ConfigurableEncryptionKeyBoundUIState(frequency, 1000, 9999));
            }

            var input = menu.FindControl<LineEdit>("FrequencyLineEdit");
            var presets = menu.FindControl<OptionButton>("PopularFrequencyOptions");
            update(1459);
            Assert.That(input.Text, Is.EqualTo("1459"));

            foreach (var draft in new[] { "", "1", "12", "123", "1234" })
            {
                input.SetText(draft, invokeEvent: true);
                update(1459);
                Assert.That(input.Text, Is.EqualTo(draft));
                Assert.That(presets.SelectedId, Is.EqualTo(RadioFrequencyPresetUi.CustomOptionId));
                Assert.That(submitted, Is.Null);
            }

            input.ForceSubmitText();
            Assert.That(submitted, Is.EqualTo("1234"));
            update(1234);
            Assert.That(input.Text, Is.EqualTo("1234"));

            input.SetText("invalid", invokeEvent: true);
            update(1234);
            Assert.That(input.Text, Is.EqualTo("invalid"));
            input.ForceSubmitText();
            update(1234);
            Assert.That(input.Text, Is.EqualTo("1234"));

            update(1459);
            Assert.That(input.Text, Is.EqualTo("1459"));
        });
        await pair.CleanReturnAsync();
    }
}
