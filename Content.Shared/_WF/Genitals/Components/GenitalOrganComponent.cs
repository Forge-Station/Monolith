namespace Content.Shared._WF.Genitals.Components;

/// <summary>Configuration carried by a genital organ; travels with it on transplant. Server data only.</summary>
[RegisterComponent]
public sealed partial class GenitalOrganComponent : Component
{
    [DataField(required: true)]
    public GenitalSlot Slot;

    [DataField]
    public GenitalOrganState State;
}
