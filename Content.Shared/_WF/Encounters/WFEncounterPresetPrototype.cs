using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Encounters;

/// <summary>How busy and how dangerous the storyteller makes a round. Players vote for one in the lobby.</summary>
[Prototype("wfEncounterPreset")]
public sealed partial class WFEncounterPresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>Whether it is offered in the lobby vote.</summary>
    [DataField]
    public bool Votable = true;

    /// <summary>The summed cost of running encounters the storyteller stays within.</summary>
    [DataField]
    public int Budget = 3;

    /// <summary>Budget added per connected player.</summary>
    [DataField]
    public float BudgetPerPlayer;

    /// <summary>Multiplies the wait between scheduled encounters.</summary>
    [DataField]
    public float IntervalScale = 1f;

    /// <summary>The most encounters placed at round start.</summary>
    [DataField]
    public int RoundStart = 1;

    /// <summary>Weight multiplier per category; a category left out keeps its weight, zero keeps it out.</summary>
    [DataField]
    public Dictionary<WFEncounterCategory, float> Weights = new();
}
