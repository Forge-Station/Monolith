using Robust.Shared.Serialization;

namespace Content.Shared._WF.RoundSetup;

/// <summary>Optional starting rosters whose openings remain individually editable.</summary>
[Serializable, NetSerializable]
public enum WFCrewRoster : byte { Medical, Salvage, Service }

/// <summary>Ordinary ship professions; station command and antagonist jobs remain outside crew recruitment.</summary>
public static class WFShipCrewJobs
{
    public static readonly string[] Jobs =
    {
        "Contractor", "Pilot", "Mercenary", "StationEngineer", "AtmosphericTechnician",
        "MedicalDoctor", "MdMedic", "Chemist", "Paramedic", "SalvageSpecialist",
        "Scientist", "CargoTechnician", "Botanist", "Chef", "Bartender", "Janitor",
    };

    /// <summary>Supplies localized role labels, real jobs and capacities without assigning any player.</summary>
    public static (string Name, string Job, int Capacity)[] Roster(WFCrewRoster roster) => roster switch
    {
        WFCrewRoster.Medical => new[] { ("captain", "Contractor", 1), ("responders", "MdMedic", 3), ("chemist", "Chemist", 1), ("engineer", "StationEngineer", 1) },
        WFCrewRoster.Salvage => new[] { ("captain", "Contractor", 1), ("salvagers", "SalvageSpecialist", 4), ("engineer", "StationEngineer", 1) },
        WFCrewRoster.Service => new[] { ("captain", "Contractor", 1), ("chef", "Chef", 2), ("bartender", "Bartender", 2), ("botanist", "Botanist", 1) },
        _ => Array.Empty<(string, string, int)>(),
    };
}
