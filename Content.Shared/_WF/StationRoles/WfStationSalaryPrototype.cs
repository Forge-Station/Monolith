using Robust.Shared.Prototypes;

namespace Content.Shared._WF.StationRoles;

/// <summary>
/// Hourly wage for a station job. The id is the job id. A sector account, when it is a real
/// <c>SectorBankAccount</c>, is drawn before the player is paid.
/// </summary>
[Prototype("wfStationSalary")]
public sealed partial class WfStationSalaryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public int Wage { get; private set; }

    [DataField]
    public string? Account { get; private set; }
}
