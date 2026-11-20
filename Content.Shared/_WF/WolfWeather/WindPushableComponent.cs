namespace Content.Shared._WF.WolfWeather;

/// <summary>Lets the wind push this entity.</summary>
[RegisterComponent]
public sealed partial class WindPushableComponent : Component
{
    [DataField]
    public bool Enabled = true;

    [DataField]
    public float Factor = 1f;
}
