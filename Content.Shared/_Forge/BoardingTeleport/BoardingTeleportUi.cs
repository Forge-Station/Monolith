using Content.Shared.Shuttles.BUIStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.BoardingTeleport;

[Serializable, NetSerializable]
public sealed class BoardingTeleportBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly BoardingTeleportPage Page;
    public readonly NavInterfaceState NavState;
    public readonly ShuttleMapInterfaceState MapState;
    public readonly NetEntity? TargetGrid;
    public readonly NetCoordinates? LandingCoordinates;
    public readonly BoardingTeleportStatus Status;
    public readonly BoardingTeleportInsertionMode Mode;
    public readonly float ModeDelaySeconds;
    public readonly float ModeScatter;
    public readonly float ModeRiskPercent;
    public readonly float ApcRiskBonusPercent;
    public readonly float? PlatformCooldownSeconds;
    public readonly float ReturnWindowSeconds;
    public readonly float? EngineRange;
    public readonly float? EngineMaxTargetVelocity;
    public readonly float LockAgeSeconds;
    public readonly float LockScatterPenalty;
    public readonly float LockRiskPenalty;
    public readonly int SelectedPlatformSlot;
    public readonly bool UseSharedLandingZone;
    public readonly NetCoordinates? SelectedLandingCoordinates;
    public readonly List<BoardingTeleportPlatformUiEntry> Platforms;

    public BoardingTeleportBoundUserInterfaceState(
        BoardingTeleportPage page,
        NavInterfaceState navState,
        ShuttleMapInterfaceState mapState,
        NetEntity? targetGrid,
        NetCoordinates? landingCoordinates,
        BoardingTeleportStatus status,
        BoardingTeleportInsertionMode mode,
        float modeDelaySeconds,
        float modeScatter,
        float modeRiskPercent,
        float apcRiskBonusPercent,
        float? platformCooldownSeconds,
        float returnWindowSeconds,
        float? engineRange,
        float? engineMaxTargetVelocity,
        float lockAgeSeconds,
        float lockScatterPenalty,
        float lockRiskPenalty,
        int selectedPlatformSlot,
        bool useSharedLandingZone,
        NetCoordinates? selectedLandingCoordinates,
        List<BoardingTeleportPlatformUiEntry> platforms)
    {
        Page = page;
        NavState = navState;
        MapState = mapState;
        TargetGrid = targetGrid;
        LandingCoordinates = landingCoordinates;
        Status = status;
        Mode = mode;
        ModeDelaySeconds = modeDelaySeconds;
        ModeScatter = modeScatter;
        ModeRiskPercent = modeRiskPercent;
        ApcRiskBonusPercent = apcRiskBonusPercent;
        PlatformCooldownSeconds = platformCooldownSeconds;
        ReturnWindowSeconds = returnWindowSeconds;
        EngineRange = engineRange;
        EngineMaxTargetVelocity = engineMaxTargetVelocity;
        LockAgeSeconds = lockAgeSeconds;
        LockScatterPenalty = lockScatterPenalty;
        LockRiskPenalty = lockRiskPenalty;
        SelectedPlatformSlot = selectedPlatformSlot;
        UseSharedLandingZone = useSharedLandingZone;
        SelectedLandingCoordinates = selectedLandingCoordinates;
        Platforms = platforms;
    }
}

[Serializable, NetSerializable]
public sealed class BoardingTeleportClearTargetMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class BoardingTeleportSelectGridMessage : BoundUserInterfaceMessage
{
    public readonly MapCoordinates Coordinates;

    public BoardingTeleportSelectGridMessage(MapCoordinates coordinates)
    {
        Coordinates = coordinates;
    }
}

[Serializable, NetSerializable]
public sealed class BoardingTeleportSelectGridEntityMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Grid;

    public BoardingTeleportSelectGridEntityMessage(NetEntity grid)
    {
        Grid = grid;
    }
}

[Serializable, NetSerializable]
public sealed class BoardingTeleportSelectTileMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Grid;
    public readonly Vector2i Tile;

    public BoardingTeleportSelectTileMessage(NetEntity grid, Vector2i tile)
    {
        Grid = grid;
        Tile = tile;
    }
}

[Serializable, NetSerializable]
public sealed class BoardingTeleportBackMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class BoardingTeleportSelectModeMessage : BoundUserInterfaceMessage
{
    public readonly BoardingTeleportInsertionMode Mode;

    public BoardingTeleportSelectModeMessage(BoardingTeleportInsertionMode mode)
    {
        Mode = mode;
    }
}

[Serializable, NetSerializable]
public sealed class BoardingTeleportSelectPlatformSlotMessage : BoundUserInterfaceMessage
{
    public readonly int SlotIndex;

    public BoardingTeleportSelectPlatformSlotMessage(int slotIndex)
    {
        SlotIndex = slotIndex;
    }
}

[Serializable, NetSerializable]
public sealed class BoardingTeleportSyncVolleyMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class BoardingTeleportToggleSharedLandingMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public enum BoardingTeleportReturnConfirmKind : byte
{
    Emergency,
    Early,
}

[Serializable, NetSerializable]
public enum BoardingTeleportUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum BoardingTeleportPage : byte
{
    Sector,
    Grid,
}

[Serializable, NetSerializable]
public enum BoardingTeleportStatus : byte
{
    None,
    TargetSelected,
    LandingSelected,
    InvalidTarget,
    TargetTooFar,
    TargetMoving,
    InvalidLanding,
    NoGrid,
    NoEngine,
    NoEnginePower,
    EngineRecharging,
    TargetShielded,
    TargetShieldTooStrong,
    SourceShieldBlocksTeleport,
    TargetInFtl,
    TargetScrambled,
    TargetFriendly,
    TargetGridProtected,
    LockExpired,
}

[Serializable, NetSerializable]
public enum BoardingTeleportInsertionMode : byte
{
    Stealth,
    Precise,
    Rapid,
}
