using System.Linq;
using Content.Shared._Forge.Radio;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.Radio.Ui;

public static class RadioFrequencyPresetUi
{
    public const int CustomOptionId = -1;

    public static void Populate(OptionButton options, IPrototypeManager prototypes)
    {
        options.Clear();
        options.AddItem(Loc.GetString("handheld-radio-preset-custom"), CustomOptionId);

        if (!prototypes.TryIndex<RadioFrequencyPresetListPrototype>(RadioFrequencyPresetListPrototype.PopularHandheld, out var list))
            return;

        var channel = 1;
        foreach (var frequency in list.Frequencies.Take(RadioFrequencyPresetListPrototype.MaxPopularCount))
        {
            options.AddItem(
                Loc.GetString("handheld-radio-preset-option", ("channel", channel), ("frequency", frequency)),
                frequency);
            channel++;
        }
    }

    public static void Select(OptionButton options, int frequency)
    {
        if (!options.TrySelectId(frequency))
            options.TrySelectId(CustomOptionId);
    }
}
