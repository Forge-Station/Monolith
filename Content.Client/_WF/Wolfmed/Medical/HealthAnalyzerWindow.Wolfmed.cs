// HOOK 26 body. Kept out of the upstream window so that file carries only the marked lines PLAN4 authorises.
// The partial lives on the window class because the panel and the overview pane are private generated fields a
// standalone control could not reach.
// UI3 adds the doll's targeting behaviour here for the same reason: the doll buttons are generated fields.

using Content.Client._Shitmed.UserInterface.Systems.Targeting;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.MedicalScanner;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.HealthAnalyzer.UI;

public sealed partial class HealthAnalyzerWindow
{
    private static readonly ResPath DollTextures = new("/Textures/_Shitmed/Interface/Targeting/Doll");

    private readonly IPlayerManager _wolfmedPlayers = IoCManager.Resolve<IPlayerManager>();

    /// <summary>One selection overlay per doll button, shown for the part the local player is targeting.</summary>
    private readonly Dictionary<TargetBodyPart, TextureRect> _wolfmedTargetOverlays = new();

    /// <summary>What the doll is currently showing as targeted, so the per-frame poll only redraws on a change.</summary>
    private TargetBodyPart? _wolfmedTargetedPart;

    private TargetingUIController? _wolfmedTargeting;

    /// <summary>Feeds the Wolfmed detail pane, or collapses it outright for anything that is not a wound host (D2).</summary>
    private void PopulateWolfmed(HealthAnalyzerScannedUserMessage msg)
    {
        if (msg.WoundDiagnostics == null && msg.Organs == null)
        {
            HideWolfmed();
            return;
        }

        WolfmedPanel.Visible = true;
        // The overview pane keeps its natural width while the tabs take the rest of the window.
        WolfmedOverviewPane.HorizontalExpand = false;
        WolfmedPanel.Populate(msg);
    }

    /// <summary>Drops the detail pane and lets the overview fill the window, so no early return leaves stale findings on screen.</summary>
    private void HideWolfmed()
    {
        WolfmedPanel.Clear();
        WolfmedPanel.Visible = false;
        WolfmedOverviewPane.HorizontalExpand = true;
    }

    /// <summary>
    /// UI3: gives every doll button a selection overlay and joins the wounds tab to the same selection. The
    /// doll no longer rescans one part; it moves the player's own body-part target.
    /// </summary>
    private void InitWolfmedTargeting()
    {
        foreach (var (part, button) in _bodyPartControls)
        {
            // The limb's own frame from the status doll art, the same 32 px canvas SetupIcon draws at 3x, laid over
            // the whole 96x96 view. It is the doll's exact silhouette in the doll's exact place by construction,
            // so there is no per-button geometry to get wrong.
            var name = part.ToString().ToLowerInvariant();
            var state = new SpriteSpecifier.Rsi(new ResPath($"/Textures/_Shitmed/Interface/Targeting/Status/{name}.rsi"), $"{name}_0");
            var overlay = new TextureRect
            {
                Texture = _spriteSystem.Frame0(state),
                Stretch = TextureRect.StretchMode.Scale,
                SetSize = new System.Numerics.Vector2(96, 96),
                Visible = false,
                MouseFilter = MouseFilterMode.Ignore,
                Modulate = Color.FromHex("#ffcf6b"),
            };
            PartView.AddChild(overlay);
            _wolfmedTargetOverlays[part] = overlay;
        }

        WolfmedPanel.OnPartSelected += SelectWolfmedTargetPart;
        ApplyWolfmedTarget(ReadWolfmedTarget(), false);
    }

    /// <summary>A click on the doll, or on a wounds-tab card header, moves the player's target.</summary>
    private void SelectWolfmedTargetPart(TargetBodyPart part)
    {
        // Bit of the ole shitcode until we have Groins in the prototypes.
        var target = part == TargetBodyPart.Groin ? TargetBodyPart.Torso : part;

        // An admin ghost has no target to move. The doll still picks the card, for this window only.
        if (!CanTarget())
        {
            ApplyWolfmedTarget(target, true);
            return;
        }

        _wolfmedTargeting ??= UserInterfaceManager.GetUIController<TargetingUIController>();
        _wolfmedTargeting.CycleTarget(target);

        // Shown at once rather than waiting for the component to come back from the server. The poll below
        // reverts it if the change did not take.
        ApplyWolfmedTarget(target, true);
    }

    /// <summary>
    /// Polled rather than subscribed: the hotkeys, the HUD doll and the server all move the target by
    /// different routes, and the component is the one place all three agree.
    /// </summary>
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // Nothing to follow for a viewer that cannot target; the doll's own pick stands.
        if (!CanTarget())
            return;

        var target = ReadWolfmedTarget();
        if (target != _wolfmedTargetedPart)
            ApplyWolfmedTarget(target, false);
    }

    private bool CanTarget() =>
        _wolfmedPlayers.LocalEntity is { } player && _entityManager.HasComponent<TargetingComponent>(player);

    private TargetBodyPart? ReadWolfmedTarget() =>
        _wolfmedPlayers.LocalEntity is { } player &&
        _entityManager.TryGetComponent(player, out TargetingComponent? targeting)
            ? targeting.Target
            : null;

    private void ApplyWolfmedTarget(TargetBodyPart? part, bool scrollIntoView)
    {
        _wolfmedTargetedPart = part;

        foreach (var (key, overlay) in _wolfmedTargetOverlays)
            overlay.Visible = key == part;

        WolfmedPanel.SetTargetedPart(part, scrollIntoView);
    }
}
