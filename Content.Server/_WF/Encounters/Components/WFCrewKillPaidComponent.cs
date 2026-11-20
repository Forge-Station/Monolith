namespace Content.Server._WF.Encounters.Components;

/// <summary>Marks a crewman whose kill bounty and cash drop have been given out, so a revived one never pays twice.</summary>
[RegisterComponent]
public sealed partial class WFCrewKillPaidComponent : Component;
