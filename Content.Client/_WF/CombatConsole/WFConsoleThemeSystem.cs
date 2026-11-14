using Content.Client._WF.Stylesheets;
using Content.Shared._Forge.CCVars; // Forge-Change
using Content.Shared._WF.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client._WF.CombatConsole;

/// <summary>Caches the active instrument palette for drawing without per-frame configuration lookups.</summary>
public sealed partial class WFConsoleThemeSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configuration = default!;

    public WolfgateSkin Skin { get; private set; } = WolfgateSkins.Default;

    /// <summary>Whether the player asked for the simplified interface, which drops decorative instrument drawing.</summary>
    public bool Simplified { get; private set; } // Forge-Change

    public override void Initialize()
    {
        base.Initialize();
        _configuration.OnValueChanged(WolfgateCVars.UiStyle, Changed, true);
        _configuration.OnValueChanged(ForgeCCVars.UiSimplified, SimplifiedChanged, true); // Forge-Change
    }

    public override void Shutdown()
    {
        _configuration.UnsubValueChanged(WolfgateCVars.UiStyle, Changed);
        _configuration.UnsubValueChanged(ForgeCCVars.UiSimplified, SimplifiedChanged); // Forge-Change
        base.Shutdown();
    }

    private void Changed(string id) => Skin = WolfgateSkins.Get(id);

    private void SimplifiedChanged(bool value) => Simplified = value; // Forge-Change
}
