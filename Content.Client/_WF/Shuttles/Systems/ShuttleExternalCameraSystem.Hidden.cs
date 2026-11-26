using Content.Client.Effects;
using Content.Shared.Mobs.Components;
using Content.Shared.Polymorph.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Shuttles.Systems;

public sealed partial class ShuttleExternalCameraSystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    private static readonly EntProtoId JetpackTrail = "JetpackEffect";

    /// <summary>
    /// Sprites the view has stopped drawing, to be put back as it goes.
    /// </summary>
    private readonly HashSet<EntityUid> _hidden = new();

    /// <summary>
    /// Whether the external view is keeping an entity from being drawn.
    /// </summary>
    public bool IsHidden(EntityUid uid)
    {
        return _hidden.Contains(uid);
    }

    /// <summary>
    /// Stops every mob but the pilot being drawn, and the trails their jetpacks leave. Without FOV anyone
    /// the roofs don't cover would be in plain sight: out on EVA, on lattice, or half out of an airlock.
    /// </summary>
    private void HideMobs(EntityUid pilot)
    {
        var mobs = EntityQueryEnumerator<MobStateComponent, SpriteComponent>();

        while (mobs.MoveNext(out var uid, out _, out var sprite))
        {
            if (uid != pilot)
                Hide((uid, sprite));
        }

        var effects = EntityQueryEnumerator<EffectVisualsComponent, SpriteComponent, MetaDataComponent>();

        while (effects.MoveNext(out var uid, out _, out var sprite, out var meta))
        {
            if (meta.EntityPrototype?.ID == JetpackTrail.Id)
                Hide((uid, sprite));
        }
    }

    private void Hide(Entity<SpriteComponent> sprite)
    {
        // Whatever else has it hidden already isn't this view's to undo.
        if (!sprite.Comp.Visible)
            return;

        _sprite.SetVisible((sprite.Owner, sprite.Comp), false);
        _hidden.Add(sprite.Owner);
    }

    private void ShowHidden()
    {
        foreach (var uid in _hidden)
        {
            if (TryComp<SpriteComponent>(uid, out var sprite))
                _sprite.SetVisible((uid, sprite), true);
        }

        _hidden.Clear();
    }

    /// <summary>
    /// A chameleon projector notes whether its user was visible as the disguise goes on, and puts that
    /// back later. It's shown them as they really were, not as this view left them.
    /// </summary>
    private void OnDisguiseInit(Entity<ChameleonDisguisedComponent> ent, ref ComponentInit args)
    {
        if (_hidden.Remove(ent.Owner) && TryComp<SpriteComponent>(ent, out var sprite))
            _sprite.SetVisible((ent.Owner, sprite), true);
    }
}
