using Content.Shared._WF.Wolfmed.Autodoc;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Maths;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WF.Wolfmed.Autodoc;

/// <summary>Draws the autodoc pod: open bed or closed lid, the unpowered tint, and the occupant.</summary>
// A GenericVisualizer can't: a layer colour it sets is sticky, so the unpowered dim stayed for the round (every pod
// is unpowered for the tick before the power net's first update); the bed and lid are both whole pod sprites, so
// exactly one may be drawn; and the occupant shows on the open bed and is gone the moment the lid comes down.
public sealed class AutodocVisualizerSystem : VisualizerSystem<AutodocComponent>
{
    [Dependency] private SharedContainerSystem _containers = default!;

    /// <summary>Colour of the base layer with no power. Every other state draws the art as it was authored.</summary>
    private static readonly Color Unpowered = Color.FromHex("#555555");

    /// <summary>Playtest 3: the scorched tint of a breached hull, over whatever the power state would draw.</summary>
    private static readonly Color Breached = Color.FromHex("#b0605a");

    protected override void OnAppearanceChange(EntityUid uid, AutodocComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null ||
            !AppearanceSystem.TryGetData<AutodocVisualState>(uid, AutodocVisuals.State, out var state, args.Component))
            return;

        var sprite = (uid, args.Sprite);
        var lidClosed = state is AutodocVisualState.Closed or AutodocVisualState.Operating;

        // One layer, never both: the open art is the whole pod with its bed showing, and the closed and
        // operating art are the whole pod with the lid down.
        SpriteSystem.LayerSetVisible(sprite, AutodocVisualLayers.Base, !lidClosed);
        SpriteSystem.LayerSetVisible(sprite, AutodocVisualLayers.Lid, lidClosed);

        if (lidClosed)
            SpriteSystem.LayerSetRsiState(sprite, AutodocVisualLayers.Lid,
                state == AutodocVisualState.Operating ? "operate" : "closed");
        else
            SpriteSystem.LayerSetColor(sprite, AutodocVisualLayers.Base,
                state == AutodocVisualState.Unpowered ? Unpowered : Color.White);

        // Playtest 3: a breached hull is drawn scorched on whichever layer shows, until it is welded.
        var breached = AppearanceSystem.TryGetData<bool>(uid, WolfmedAutodocAtmosphereVisuals.Breached, out var hull,
            args.Component) && hull;
        SpriteSystem.LayerSetColor(sprite, AutodocVisualLayers.Lid, breached ? Breached : Color.White);
        if (breached && !lidClosed)
            SpriteSystem.LayerSetColor(sprite, AutodocVisualLayers.Base, Breached);

        // Open: the bed draws under the occupant lying in it. Closed: the lid is over them and they are not
        // drawn at all, so nothing taller than the pod can poke out of it.
        SpriteSystem.SetDrawDepth(sprite, (int) (lidClosed ? DrawDepth.OverMobs : DrawDepth.BelowMobs));
        SetOccupantVisible(uid, !lidClosed);
    }

    /// <summary>
    /// The container's own showEnts is what decides this, and the server flips it with the lid. The engine
    /// only recomputes container occlusion when something is parented or unparented, though, so a lid
    /// closing over somebody already inside has to say so here.
    /// </summary>
    private void SetOccupantVisible(EntityUid uid, bool visible)
    {
        if (!_containers.TryGetContainer(uid, AutodocComponent.BodyContainerId, out var container))
            return;

        foreach (var occupant in container.ContainedEntities)
        {
            if (TryComp(occupant, out SpriteComponent? sprite))
                SpriteSystem.SetContainerOccluded((occupant, sprite), !visible);
        }
    }
}

/// <summary>The pod's sprite layers: the open bed and the closed lid.</summary>
public enum AutodocVisualLayers : byte
{
    Base,
    Lid,
}
