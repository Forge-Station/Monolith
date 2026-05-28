using Content.Shared._Forge.BoardingTeleport;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Client._Forge.BoardingTeleport;

[UsedImplicitly]
public sealed class BoardingTeleportBoundUserInterface : BoundUserInterface
{
    private BoardingTeleportWindow? _window;

    public BoardingTeleportBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new BoardingTeleportWindow(this);
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _window?.Dispose();
        _window = null;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not BoardingTeleportBoundUserInterfaceState current)
            return;

        _window?.UpdateState(current);
    }

    public void SelectGrid(MapCoordinates coordinates)
    {
        SendMessage(new BoardingTeleportSelectGridMessage(coordinates));
    }

    public void SelectGrid(NetEntity grid)
    {
        SendMessage(new BoardingTeleportSelectGridEntityMessage(grid));
    }

    public void SelectTile(NetEntity grid, Vector2i tile)
    {
        SendMessage(new BoardingTeleportSelectTileMessage(grid, tile));
    }

    public void Back()
    {
        SendMessage(new BoardingTeleportBackMessage());
    }

    public void ClearTarget()
    {
        SendMessage(new BoardingTeleportClearTargetMessage());
    }

    public void SelectMode(BoardingTeleportInsertionMode mode)
    {
        SendMessage(new BoardingTeleportSelectModeMessage(mode));
    }

    public void SelectPlatformSlot(int slotIndex)
    {
        SendMessage(new BoardingTeleportSelectPlatformSlotMessage(slotIndex));
    }

    public void SyncVolley()
    {
        SendMessage(new BoardingTeleportSyncVolleyMessage());
    }

    public void ToggleSharedLanding()
    {
        SendMessage(new BoardingTeleportToggleSharedLandingMessage());
    }
}
