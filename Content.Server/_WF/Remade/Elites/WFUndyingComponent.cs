using Robust.Shared.Audio;

namespace Content.Server._WF.Remade.Elites;

/// <summary>The Undying. Its arrival is announced to everyone on its grid.</summary>
[RegisterComponent]
public sealed partial class WFUndyingComponent : Component
{
    [DataField]
    public SoundSpecifier Arrival = new SoundCollectionSpecifier("WFRemadeUndyingUbermorphSpawn");

    [DataField]
    public float Volume = -2f;

    /// <summary>The people who heard it arrive.</summary>
    [ViewVariables]
    public HashSet<EntityUid> HeardBy = new();
}
