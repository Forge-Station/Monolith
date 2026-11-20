using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Shared._WF.CCVar;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;

namespace Content.Client._WF.CombatConsole;

/// <summary>Restyles an open console in place and releases its configuration subscription when closed.</summary>
public sealed class WFConsoleThemeBinding : Control
{
    private readonly Control _console;
    private readonly IConfigurationManager _configuration;
    private WolfgateSkin _skin;

    /// <summary>Attaches theme changes to the lifetime of the existing window.</summary>
    public WFConsoleThemeBinding(Control console)
    {
        _console = console;
        _configuration = IoCManager.Resolve<IConfigurationManager>();
        _skin = WFInstrumentTheme.Skin;
        SetSize = Vector2.Zero;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _configuration.OnValueChanged(WolfgateCVars.UiStyle, Changed, true);
    }

    protected override void ExitedTree()
    {
        _configuration.UnsubValueChanged(WolfgateCVars.UiStyle, Changed);
        base.ExitedTree();
    }

    private void Changed(string value)
    {
        var next = WolfgateSkins.Get(value);
        if (next == _skin)
            return;
        WFInstrumentTheme.Apply(_console, _skin);
        _skin = next;
    }
}
