using System.Linq; // WOLFGATE(Stylesheets)
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration; // WOLFGATE(Stylesheets)
using Robust.Shared.IoC;
using Content.Client._WF.Stylesheets; // WOLFGATE(Stylesheets)
using Content.Shared._WF.CCVar; // WOLFGATE(Stylesheets)
using Content.Shared._WF.Prototypes; // WOLFGATE(Stylesheets)
using Robust.Shared; // WOLFGATE(Stylesheets)

namespace Content.Client.Stylesheets
{
    public sealed partial class StylesheetManager : IStylesheetManager
    {
        [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private IResourceCache _resourceCache = default!;
        [Dependency] private IConfigurationManager _cfg = default!; // WOLFGATE(Stylesheets)

        public Stylesheet SheetNano { get; private set; } = default!;
        public Stylesheet SheetSpace { get; private set; } = default!;

        public void Initialize()
        {
            // WOLFGATE(Stylesheets) START: the active skin comes from a CVar and is swapped live when it changes
            // SheetNano = new StyleNano(_resourceCache).Stylesheet;
            // SheetSpace = new StyleSpace(_resourceCache).Stylesheet;
            // A theme saved under a renamed id moves to the current one
            var savedTheme = _cfg.GetCVar(CVars.InterfaceTheme);
            var currentTheme = WFLegacyPrototypeIds.Resolve(WFLegacyPrototypeIds.HudThemes, savedTheme);
            if (currentTheme != savedTheme)
                _cfg.SetCVar(CVars.InterfaceTheme, currentTheme);
            // A player who never picked a HUD theme gets the one that pairs with their skin
            _cfg.OverrideDefault(CVars.InterfaceTheme, WolfgateSkins.Get(_cfg.GetCVar(WolfgateCVars.UiStyle)).HudTheme);
            _cfg.OnValueChanged(WolfgateCVars.UiStyle, _ => ApplySkin());
            ApplySkin();
            // WOLFGATE END
        }

        // WOLFGATE(Stylesheets) START: rebuilds both sheets over the stock ones with the chosen skin and pushes them to every root
        private void ApplySkin()
        {
            var skin = WolfgateSkins.Get(_cfg.GetCVar(WolfgateCVars.UiStyle));
            var wolfgate = new StyleWolfgate(_resourceCache, skin);
            SheetNano = wolfgate.Apply(new StyleNano(_resourceCache).Stylesheet);
            SheetSpace = wolfgate.Apply(new StyleSpace(_resourceCache).Stylesheet);
            _userInterfaceManager.Stylesheet = SheetNano;

            // Follow with the matching HUD theme unless the player picked a theme that is not one of ours
            var theme = _cfg.GetCVar(CVars.InterfaceTheme);
            if (theme != skin.HudTheme && (theme == string.Empty || WolfgateSkins.All.Any(s => s.HudTheme == theme)))
                _cfg.SetCVar(CVars.InterfaceTheme, skin.HudTheme);
        }
        // WOLFGATE END
    }
}
