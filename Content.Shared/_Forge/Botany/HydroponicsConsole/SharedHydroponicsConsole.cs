using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Botany.HydroponicsConsole;

[Serializable, NetSerializable]
public enum HydroponicsConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class HydroponicsConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public List<HydroponicsConsoleTrayEntry> Trays = new();
}

[Serializable, NetSerializable]
public sealed class HydroponicsConsoleTrayEntry
{
    public NetEntity Entity;
    public string Address = string.Empty;
    public string TrayName = string.Empty;
    public string PlantName = string.Empty;
    public bool HasPlant;
    public bool Dead;
    public bool Harvest;
    public float Health;
    public float Endurance;
    public float Water;
    public float Nutrition;
    public float Toxins;
    public float Weeds;
    public float Pests;
    public int Age;
    public bool ImproperHeat;
    public bool ImproperPressure;
    public bool ImproperLight;
    public bool MissingGas;
    public bool Radioactive;
    public bool CarnivorousGrab;
    public string[] Chemicals = Array.Empty<string>();
    public string[] Mutations = Array.Empty<string>();
    public string ConsumeGases = string.Empty;
    public string ExudeGases = string.Empty;
    public float IdealHeat;
    public float HeatTolerance;
    public float LowPressure;
    public float HighPressure;
}
