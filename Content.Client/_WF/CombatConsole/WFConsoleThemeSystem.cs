using Content.Client._WF.Stylesheets;
using Content.Shared._WF.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client._WF.CombatConsole;

/// <summary>Caches the active instrument palette for drawing without per-frame configuration lookups.</summary>
public sealed class WFConsoleThemeSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configuration = default!;

    public WolfgateSkin Skin { get; private set; } = WolfgateSkins.Default;

    public override void Initialize()
    {
        base.Initialize();
        _configuration.OnValueChanged(WolfgateCVars.UiStyle, Changed, true);
    }

    public override void Shutdown()
    {
        _configuration.UnsubValueChanged(WolfgateCVars.UiStyle, Changed);
        base.Shutdown();
    }

    private void Changed(string id) => Skin = WolfgateSkins.Get(id);
}
