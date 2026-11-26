using System.Globalization;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

/// <summary>A typed percentage beside the height and width sliders, kept in step with them both ways.</summary>
public sealed partial class HumanoidProfileEditor
{
    private void InitializeSizePercent()
    {
        BindPercent(HeightSlider, HeightPercent);
        BindPercent(WidthSlider, WidthPercent);
    }

    private static void BindPercent(Slider slider, LineEdit edit)
    {
        edit.Text = Percent(slider.Value);
        slider.OnValueChanged += _ => edit.Text = Percent(slider.Value);
        edit.OnTextEntered += _ => Apply(slider, edit);
        edit.OnFocusExit += _ => Apply(slider, edit);
    }

    /// <summary>
    /// Sets the slider from typed text, rounded to a whole percent and clamped to its range; text that isn't a finite
    /// number is put back.
    /// </summary>
    private static void Apply(Slider slider, LineEdit edit)
    {
        var text = edit.Text.Trim().TrimEnd('%').Trim();
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) && float.IsFinite(percent))
            slider.Value = Math.Clamp(MathF.Round(percent) / 100f, slider.MinValue, slider.MaxValue);

        edit.Text = Percent(slider.Value);
    }

    private static string Percent(float value)
    {
        return MathF.Round(value * 100f).ToString(CultureInfo.InvariantCulture);
    }
}
