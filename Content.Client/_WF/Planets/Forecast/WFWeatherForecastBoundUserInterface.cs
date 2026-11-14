using Content.Shared._WF.Planets.Forecast;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Planets.Forecast;

/// <summary>Opens the forecast window and passes a world pick back to the console.</summary>
[UsedImplicitly]
public sealed class WFWeatherForecastBoundUserInterface : BoundUserInterface
{
    private WFWeatherForecastWindow? _window;

    public WFWeatherForecastBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFWeatherForecastWindow>();
        _window.OnWorldSelected += world => SendMessage(new WFWeatherForecastSelectMessage(world));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WFWeatherForecastState forecast)
            _window?.UpdateState(forecast);
    }
}
