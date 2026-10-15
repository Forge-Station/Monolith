using Content.Shared._Forge.KIAS;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.KIAS;

[UsedImplicitly]
public sealed class KiasLocalBoundUserInterface : BoundUserInterface
{
    private KiasLocalWindow? _window;
    public KiasLocalBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _window = UiKey switch
        {
            KiasUiKey.Service => this.CreateWindow<KiasServiceWindow>(),
            KiasUiKey.Recorder => this.CreateWindow<KiasRecorderWindow>(),
            KiasUiKey.Scanner => this.CreateWindow<KiasScannerWindow>(),
            KiasUiKey.Sensor => this.CreateWindow<KiasSensorWindow>(),
            KiasUiKey.Crew => this.CreateWindow<KiasCrewWindow>(),
            KiasUiKey.Speaker => this.CreateWindow<KiasSpeakerWindow>(),
            _ => this.CreateWindow<KiasWallWindow>(),
        };
        _window.RefreshRequested += () => SendMessage(new KiasRefreshMessage());
        _window.SettingsChanged += message => SendMessage(message);
        _window.ModeChanged += mode => SendMessage(new KiasModeMessage { Mode = mode });
        _window.MessageChanged += message => SendMessage(new KiasSetMessage(message));
        _window.OnClose += () => EntMan.System<KiasCoverageSystem>().Clear(Owner);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        _window?.UpdateState(state);
        var geometry = state switch
        {
            KiasServiceState service => service.Geometry,
            KiasSensorState sensor => sensor.Geometry,
            KiasScannerState scanner => scanner.Geometry,
            _ => null,
        };
        EntMan.System<KiasCoverageSystem>().Show(Owner, geometry, UiKey is KiasUiKey.Service);
    }

    protected override void Dispose(bool disposing)
    {
        EntMan.System<KiasCoverageSystem>().Clear(Owner);
        base.Dispose(disposing);
    }
}
