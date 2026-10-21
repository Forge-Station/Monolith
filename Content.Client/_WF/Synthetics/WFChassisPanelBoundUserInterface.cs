using Content.Shared._WF.Synthetics;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Synthetics;

/// <summary>Client end of the chassis panel; the server registers it by this class name.</summary>
[UsedImplicitly]
public sealed class WFChassisPanelBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    /// <summary>The tab the player last had open, so the panel reopens on it.</summary>
    private static int _lastTab;

    [ViewVariables]
    private WFChassisPanelWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WFChassisPanelWindow>();
        _window.OnIsolate += (part, isolate) => SendMessage(new WFChassisIsolatePartMessage(part, isolate));
        _window.OnDetach += part => SendMessage(new WFChassisDetachPartMessage(part));
        _window.OnDisconnect += organ => SendMessage(new WFChassisDisconnectOrganMessage(organ));
        _window.OnToggleBay += () => SendMessage(new WFChassisToggleBayMessage());
        _window.OnReboot += () => SendMessage(new WFChassisRebootMessage());
        _window.OnOverclock += on => SendMessage(new WFChassisOverclockMessage(on));

        _window.CurrentTab = _lastTab;
        _window.OnTabChanged += tab => _lastTab = tab;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is WFChassisPanelState panel)
            _window?.Update(panel);
    }
}
