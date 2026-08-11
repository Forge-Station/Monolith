using Content.Shared._Mono.Company;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Company;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CompanyDoorAccessConsoleComponent : Component
{
    /// <summary>
    /// Cached owning company of the console's grid. Empty / None means inactive.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<CompanyPrototype> BoundCompany = "None";
}

[Serializable, NetSerializable]
public enum CompanyDoorAccessConsoleUiKey : byte
{
    Key,
}

public enum CompanyDoorAccessMode : byte
{
    Open = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

[Serializable, NetSerializable]
public sealed class CompanyDoorAccessEntry
{
    public NetEntity Door;
    public string Name = string.Empty;
    public CompanyDoorAccessMode Mode;
    public NetCoordinates Coordinates;
}

[Serializable, NetSerializable]
public sealed class CompanyDoorAccessConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public bool Active;
    public string BoundCompanyId = string.Empty;
    public string BoundCompanyName = string.Empty;
    public string StatusMessage = string.Empty;
    public List<CompanyDoorAccessEntry> Doors = new();
    public bool CanConfigure;
    /// <summary>
    /// Highest door access tier this user may set or modify (owners = High).
    /// </summary>
    public CompanyAccessTier MaxAccessTier;
    public NetEntity? GridEntity;
    public NetCoordinates? ConsoleCoordinates;
}

[Serializable, NetSerializable]
public sealed class CompanyDoorAccessConsoleSetMessage : BoundUserInterfaceMessage
{
    public List<NetEntity> Doors = new();
    public CompanyDoorAccessMode Mode;

    public CompanyDoorAccessConsoleSetMessage(List<NetEntity> doors, CompanyDoorAccessMode mode)
    {
        Doors = doors;
        Mode = mode;
    }
}
