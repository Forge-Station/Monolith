using Robust.Shared.Serialization;

namespace Content.Shared._WF.RoundSetup;

/// <summary>Factions supported by crew deployment; existing jobs remain the source of identity and equipment.</summary>
[Serializable, NetSerializable]
public enum WFCrewFaction : byte { None, TSF, PDV, VG, USSP, Underground }

/// <summary>Explicit command tiers, independent of job allocation weights and custom titles.</summary>
public static class WFFactionCrewPolicy
{
    /// <summary>Every faction a crew can deploy as, in menu order.</summary>
    public static readonly WFCrewFaction[] All =
        { WFCrewFaction.TSF, WFCrewFaction.PDV, WFCrewFaction.VG, WFCrewFaction.USSP, WFCrewFaction.Underground };

    /// <summary>Regular ranks and specialist postings supported by faction crews.</summary>
    public static readonly string[] TsfJobs = { "Cadet", "Deputy", "Brigmedic", "TsfEngineer", "NFDetective",
        "PublicAffairsLiaison", "SeniorOfficer", "Bailiff", "Sheriff" };
    /// <summary>Regular PDV ranks and specialist postings.</summary>
    public static readonly string[] PdvJobs = { "Pirate", "PDVInfiltrator", "WFPdvJarrah", "PDVDenasvar", "PirateFirstMate", "PirateCaptain" };
    /// <summary>Viper Group ranks.</summary>
    public static readonly string[] VgJobs = { "VGInfanteer", "VGLieutenant", "VGCommander" };
    /// <summary>USSP ranks.</summary>
    public static readonly string[] UsspJobs = { "USSPRifleman", "USSPSergeant", "USSPCommissar" };
    /// <summary>Underground ranks.</summary>
    public static readonly string[] UndergroundJobs = { "Fugitive", "Godfather" };

    /// <summary>Only explicitly supported jobs can establish a faction or recruitment ceiling.</summary>
    public static int Rank(WFCrewFaction faction, string? job) => (faction, job) switch
    {
        (WFCrewFaction.TSF, "Cadet") => 0,
        (WFCrewFaction.TSF, "Deputy" or "Brigmedic" or "TsfEngineer" or "NFDetective" or "PublicAffairsLiaison") => 1,
        (WFCrewFaction.TSF, "SeniorOfficer") => 2,
        (WFCrewFaction.TSF, "Bailiff") => 3,
        (WFCrewFaction.TSF, "Sheriff") => 4,
        (WFCrewFaction.PDV, "Pirate" or "PDVInfiltrator" or "WFPdvJarrah") => 1,
        (WFCrewFaction.PDV, "PDVDenasvar") => 2,
        (WFCrewFaction.PDV, "PirateFirstMate") => 3,
        (WFCrewFaction.PDV, "PirateCaptain") => 4,
        (WFCrewFaction.VG, "VGInfanteer") => 1,
        (WFCrewFaction.VG, "VGLieutenant") => 2,
        (WFCrewFaction.VG, "VGCommander") => 3,
        (WFCrewFaction.USSP, "USSPRifleman") => 1,
        (WFCrewFaction.USSP, "USSPSergeant") => 2,
        (WFCrewFaction.USSP, "USSPCommissar") => 3,
        (WFCrewFaction.Underground, "Fugitive") => 1,
        (WFCrewFaction.Underground, "Godfather") => 2,
        _ => -1,
    };

    /// <summary>Specialist eligibility is checked separately; a title never grants rank.</summary>
    public static bool CanRecruit(WFCrewFaction faction, string? hostJob, string? recruitJob) =>
        Rank(faction, recruitJob) is var rank && rank >= 0 && rank <= Rank(faction, hostJob);

    /// <summary>Returns supported jobs without exposing special deployment roles.</summary>
    public static string[] Jobs(WFCrewFaction faction) => faction switch
    {
        WFCrewFaction.TSF => TsfJobs,
        WFCrewFaction.PDV => PdvJobs,
        WFCrewFaction.VG => VgJobs,
        WFCrewFaction.USSP => UsspJobs,
        WFCrewFaction.Underground => UndergroundJobs,
        _ => Array.Empty<string>(),
    };

    /// <summary>The company prototype a faction's jobs and vouchers carry.</summary>
    public static string? Company(WFCrewFaction faction) => faction switch
    {
        WFCrewFaction.TSF => "TSF",
        WFCrewFaction.PDV => "PDV",
        WFCrewFaction.VG => "TheViperGroup",
        WFCrewFaction.USSP => "USSP",
        WFCrewFaction.Underground => "WFUnderground",
        _ => null,
    };

    /// <summary>The command job whose slot marks a station as the faction's base, even when it is occupied.</summary>
    public static string? CommandJob(WFCrewFaction faction) => faction switch
    {
        WFCrewFaction.TSF => "Sheriff",
        WFCrewFaction.PDV => "PirateCaptain",
        WFCrewFaction.VG => "VGCommander",
        WFCrewFaction.USSP => "USSPCommissar",
        WFCrewFaction.Underground => "Godfather",
        _ => null,
    };
}
