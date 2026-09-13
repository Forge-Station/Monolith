namespace Content.Shared._WF.Genitals.Components;

/// <summary>Marks a surgery singleton as an anatomy surgery: hidden from opted-out viewers, surgeon checked.</summary>
[RegisterComponent]
public sealed partial class GenitalSurgeryComponent : Component;

/// <summary>Surgery condition: the patient must be eligible with strict master consent.</summary>
[RegisterComponent]
public sealed partial class SurgeryGenitalConsentConditionComponent : Component;
