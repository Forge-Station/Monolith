using Content.Shared._WF.Xenonids.Construction.Tunnel;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Xenonids.Construction.Tunnel;

/// <summary>Picks a destination tunnel from a plain list.</summary>
// WOLFGATE: tactical maps are cut; this is RMC's earlier list-based tunnel window
[UsedImplicitly]
public sealed class SelectDestinationTunnelBui(EntityUid ent, Enum key) : BoundUserInterface(ent, key)
{
    private SelectDestinationTunnelWindow? _window;
    private NetEntity? _selectedTunnel;

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SelectDestinationTunnelInterfaceState newState || _window == null)
            return;

        _window.SelectableTunnels.Clear();
        foreach (var (name, netTunnel) in newState.HiveTunnels)
        {
            if (EntMan.GetEntity(netTunnel) == Owner)
                continue;

            _window.SelectableTunnels.Add(new ItemList.Item(_window.SelectableTunnels)
            {
                Text = name,
                Metadata = netTunnel,
            });
        }
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<SelectDestinationTunnelWindow>();
        _window.SelectButton.Disabled = true;

        _window.SelectableTunnels.OnItemSelected += args =>
        {
            _window.SelectButton.Disabled = false;
            _selectedTunnel = (NetEntity) args.ItemList[args.ItemIndex].Metadata!;
        };

        _window.SelectButton.OnPressed += _ =>
        {
            if (_selectedTunnel is not { } tunnel)
                return;

            SendMessage(new TraverseXenoTunnelMessage(tunnel));
            Close();
        };
    }
}
