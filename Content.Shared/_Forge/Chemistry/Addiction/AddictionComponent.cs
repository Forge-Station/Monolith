using Robust.Shared.GameObjects;

namespace Content.Shared._Forge.Chemistry.Addiction;

[RegisterComponent]
public sealed partial class AddictionComponent : Component
{
    // Are addiction enabled for this mob?
    [DataField]
    public bool Enabled = true;

    // Active addictions keyed by prototype ID
    [DataField]
    public Dictionary<string, AddictionData> Addictions { get; set; } = new();

    [DataField]
    public float TickInterval { get; set; } = 2f;

    public float TickTimer { get; set; } = 0f;
}
