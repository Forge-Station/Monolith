using Content.Client.Stylesheets;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Loadouts;

/// <summary>Colour a small label reads in.</summary>
public enum LoadoutTone : byte
{
    Normal,
    Muted,
    Caution,
    Danger,
    Good,
}

/// <summary>Sets a label's colour through the stylesheet's label classes, so it follows the skin.</summary>
public static class LoadoutTones
{
    /// <summary>Modulate that darkens a control without fading it, so nothing behind shows through.</summary>
    public static readonly Color Dimmed = new(0.5f, 0.5f, 0.5f);

    private const string ClassCaution = "Caution";
    private const string ClassDanger = "Danger";
    private const string ClassGood = "Good";

    public static void Set(Label label, LoadoutTone tone)
    {
        Toggle(label, StyleBase.StyleClassLabelSubText, tone == LoadoutTone.Muted);
        Toggle(label, ClassCaution, tone == LoadoutTone.Caution);
        Toggle(label, ClassDanger, tone == LoadoutTone.Danger);
        Toggle(label, ClassGood, tone == LoadoutTone.Good);
    }

    private static void Toggle(Label label, string styleClass, bool on)
    {
        if (label.HasStyleClass(styleClass) == on)
            return;

        if (on)
            label.AddStyleClass(styleClass);
        else
            label.RemoveStyleClass(styleClass);
    }
}
