namespace Content.Server._WF.Genitals;

/// <summary>A body's adult description from its character profile. Server only; the examine card sends it to examiners who may read anatomy.</summary>
[RegisterComponent]
public sealed partial class NsfwDescriptionComponent : Component
{
    [DataField]
    public string Text = string.Empty;
}
