using Content.Shared._Forge.Botany;
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
    public List<HydroponicsCultivarRecord> Journal = new();
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
    public bool CarnivorousPestEater;
    public bool GeneLocked;
    public string[] Chemicals = Array.Empty<string>();
    public string[] Mutations = Array.Empty<string>();
    public string ConsumeGases = string.Empty;
    public string ExudeGases = string.Empty;
    public float IdealHeat;
    public float HeatTolerance;
    public float LowPressure;
    public float HighPressure;
    public float IdealLight;
    public float LightTolerance;
    public HydroponicsLightMode LightMode;
}

[Serializable, NetSerializable]
public sealed class HydroponicsCultivarRecord
{
    public int Index;
    public string LineName = string.Empty;
    public string SpeciesName = string.Empty;
    public float Potency;
    public int Yield;
    public float IdealHeat;
    public float IdealLight;
    public bool Radioactive;
    public bool CarnivorousGrab;
    public bool CarnivorousPestEater;
    public bool GeneLocked;
    public bool Ligneous;
    public string[] Chemicals = Array.Empty<string>();
    public string[] Mutations = Array.Empty<string>();
    public string ConsumeGases = string.Empty;
    public string ExudeGases = string.Empty;
}

[Serializable, NetSerializable]
public sealed class HydroponicsConsoleSaveCultivarMessage : BoundUserInterfaceMessage
{
    public NetEntity Tray;
    public string LineName = string.Empty;

    public HydroponicsConsoleSaveCultivarMessage(NetEntity tray, string lineName)
    {
        Tray = tray;
        LineName = lineName;
    }
}

[Serializable, NetSerializable]
public sealed class HydroponicsConsoleRenameCultivarMessage : BoundUserInterfaceMessage
{
    public int Index;
    public string LineName = string.Empty;

    public HydroponicsConsoleRenameCultivarMessage(int index, string lineName)
    {
        Index = index;
        LineName = lineName;
    }
}

[Serializable, NetSerializable]
public sealed class HydroponicsConsoleDeleteCultivarMessage : BoundUserInterfaceMessage
{
    public int Index;

    public HydroponicsConsoleDeleteCultivarMessage(int index)
    {
        Index = index;
    }
}

[Serializable, NetSerializable]
public sealed class HydroponicsConsolePrintPacketMessage : BoundUserInterfaceMessage
{
    public int Index;

    public HydroponicsConsolePrintPacketMessage(int index)
    {
        Index = index;
    }
}

[Serializable, NetSerializable]
public sealed class HydroponicsConsoleEjectDiskMessage : BoundUserInterfaceMessage
{
    public int Index;

    public HydroponicsConsoleEjectDiskMessage(int index)
    {
        Index = index;
    }
}

[Serializable, NetSerializable]
public sealed class HydroponicsConsoleCycleLightMessage : BoundUserInterfaceMessage
{
    public NetEntity Tray;

    public HydroponicsConsoleCycleLightMessage(NetEntity tray)
    {
        Tray = tray;
    }
}

[Serializable, NetSerializable]
public sealed class HydroponicsConsoleRenameTrayMessage : BoundUserInterfaceMessage
{
    public NetEntity Tray;
    public string TrayName = string.Empty;

    public HydroponicsConsoleRenameTrayMessage(NetEntity tray, string trayName)
    {
        Tray = tray;
        TrayName = trayName;
    }
}
