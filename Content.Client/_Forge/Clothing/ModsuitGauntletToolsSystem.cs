using Content.Shared._Forge.Clothing;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.GameStates;

namespace Content.Client._Forge.Clothing;

public sealed partial class ModsuitGauntletToolsSystem : SharedModsuitGauntletToolsSystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    private ModsuitGauntletToolsRadialMenu? _menu;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModsuitGauntletToolsComponent, OpenModsuitGauntletToolsMenuActionEvent>(OnOpenMenu);
        SubscribeLocalEvent<ModsuitGauntletToolComponent, ComponentStartup>(OnToolStartup);
        SubscribeLocalEvent<ModsuitGauntletToolComponent, AfterAutoHandleStateEvent>(OnToolAfterHandleState);
    }

    private void OnToolStartup(Entity<ModsuitGauntletToolComponent> ent, ref ComponentStartup args)
    {
        ApplyToolSpriteVisible(ent, !ent.Comp.StoredHidden);
    }

    private void OnToolAfterHandleState(Entity<ModsuitGauntletToolComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ApplyToolSpriteVisible(ent, !ent.Comp.StoredHidden);
    }

    private void ApplyToolSpriteVisible(EntityUid tool, bool visible)
    {
        if (!TryComp<SpriteComponent>(tool, out var sprite) || sprite.Visible == visible)
            return;

        sprite.Visible = visible;
    }

    private void OnOpenMenu(Entity<ModsuitGauntletToolsComponent> ent, ref OpenModsuitGauntletToolsMenuActionEvent args)
    {
        args.Handled = true;
        OpenRadialMenu(ent);
    }

    private void OpenRadialMenu(Entity<ModsuitGauntletToolsComponent> gauntlets)
    {
        CloseRadialMenu();

        _menu = _ui.CreateWindow<ModsuitGauntletToolsRadialMenu>();
        _menu.OnClose += CloseRadialMenu;
        _menu.OnToolSelected += slot =>
        {
            RaisePredictiveEvent(new ModsuitGauntletToggleToolMessage(GetNetEntity(gauntlets), slot));
        };
        _menu.SetGauntlets(gauntlets);
        _menu.OpenCentered();
    }

    private void CloseRadialMenu()
    {
        if (_menu == null)
            return;

        _menu.OnClose -= CloseRadialMenu;
        _menu.Dispose();
        _menu = null;
    }
}
