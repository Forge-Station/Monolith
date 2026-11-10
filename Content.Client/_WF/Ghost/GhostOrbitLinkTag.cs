using System.Diagnostics.CodeAnalysis;
using Content.Client._WF.Stylesheets;
using Content.Shared._WF.CCVar;
using Content.Shared.Ghost;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client._WF.Ghost;

/// <summary>
/// <c>[orbit=NETENTITY/]</c>: a clickable link that orbits the entity while the local player is a ghost.
/// Added in front of chat messages by <see cref="GhostOrbitSystem.AddChatLink"/>.
/// </summary>
public sealed partial class GhostOrbitLinkTag : IMarkupTagHandler
{
    public const string TagName = "orbit";

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPlayerManager _player = default!;

    public string Name => TagName;

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        if (!TryGetTarget(node, out var target))
            return false;

        var skin = WolfgateSkins.Get(_cfg.GetCVar(WolfgateCVars.UiStyle));
        var label = new Label
        {
            Text = Loc.GetString("wf-ghost-orbit-chat-link"),
            ToolTip = Loc.GetString("wf-ghost-orbit-chat-link-tooltip"),
            FontColorOverride = skin.AccentDim,
            MouseFilter = Control.MouseFilterMode.Stop,
            DefaultCursorShape = Control.CursorShape.Hand,
        };
        label.OnMouseEntered += _ => label.FontColorOverride = skin.Accent;
        label.OnMouseExited += _ => label.FontColorOverride = skin.AccentDim;
        label.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            args.Handle();
            // History keeps the link after respawning; only a ghost can orbit.
            if (_player.LocalEntity is { } local
                && _entMan.HasComponent<GhostComponent>(local)
                && _entMan.EntitySysManager.TryGetEntitySystem<GhostOrbitSystem>(out var orbit))
                orbit.Orbit(target);
        };

        control = label;
        return true;
    }

    private static bool TryGetTarget(MarkupNode node, out NetEntity target)
    {
        target = NetEntity.Invalid;
        if (node.Value.TryGetLong(out var number))
        {
            target = new NetEntity((int) number.Value);
            return target.Valid;
        }

        if (node.Value.TryGetString(out var text) && int.TryParse(text, out var id))
        {
            target = new NetEntity(id);
            return target.Valid;
        }

        return false;
    }
}
