using Content.Shared._Forge.Botany;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.PlantAnalyzer;

/// <summary>
///     The information about the last scanned plant/seed is stored here.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlantAnalyzerScannedSeedPlantInformation : BoundUserInterfaceMessage
{
    public NetEntity? TargetEntity;
    public bool IsTray;

    // Tray / plant status
    public bool HasPlant = true;
    public bool Dead;
    public bool HarvestReady;
    public float TrayHealth;
    public float TrayEndurance;
    public float WaterLevel;
    public float NutritionLevel;
    public float Toxins;
    public float WeedLevel;
    public float PestLevel;
    public int Age;
    public bool ImproperHeat;
    public bool ImproperPressure;
    public bool ImproperLight;
    public bool MissingGas;
    public HydroponicsLightMode LightMode;

    //Basic tab
    public string? SeedName;
    public int SeedYield;
    public float SeedPotency;
    public AnalyzerHarvestType HarvestType;
    public float Lifespan;
    public float Maturation;
    public float Production;
    public int GrowthStages;
    public float Endurance;
    public GasFlags ConsumeGases;
    public GasFlags ExudeGases;
    public string[]? SeedChem;
    public string[]? MutationNames;
    //Tolerances tab
    public float NutrientConsumption;
    public float WaterConsumption;
    public float IdealHeat;
    public float HeatTolerance;
    public float IdealLight;
    public float LightTolerance;
    public float ToxinsTolerance;
    public float LowPressureTolerance;
    public float HighPressureTolerance;
    public float PestTolerance;
    public float WeedTolerance;
    //Mutations tab
    public string[]? Speciation; // Currently only available on server, we need to send strings to the client.
    public MutationFlags Mutations;
}

[Flags]
public enum MutationFlags : ushort
{
    None = 0,
    TurnIntoKudzu = 1,
    Seedless = 2,
    Ligneous = 4,
    CanScream = 8,
    Unviable = 16,
    Radioactive = 32,
    CarnivorousGrab = 64,
    CarnivorousPestEater = 128,
    GeneLocked = 256,
}

[Flags]
public enum GasFlags : int
{
    None = 0,
    Nitrogen = 1,
    Oxygen = 2,
    CarbonDioxide = 4,
    Plasma = 8,
    Tritium = 16,
    WaterVapor = 32,
    Ammonia = 64,
    NitrousOxide = 128,
    Frezon = 256,
    BZ = 512,
    Healium = 1024,
    Nitrium = 2048,
    Pluoxium = 4096,
}

public enum AnalyzerHarvestType : byte
{
    Unknown, // Just in case the backing enum type changes and we haven't caught it.
    Repeat,
    NoRepeat,
    SelfHarvest
}
