using Content.Shared._WF.RMCClothing.Patterns;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Client._WF.RMCClothing.Patterns;

/// <summary>Redraws camouflage clothing when its pattern changes.</summary>
public sealed class WFCamoPatternVisualsSystem : EntitySystem
{
    [Dependency] private WFCamoPatternSystem _patterns = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCamoPatternComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WFCamoPatternComponent, WFCamoPatternChangedEvent>(OnChanged);
    }

    private void OnState(Entity<WFCamoPatternComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _patterns.ApplyPattern(ent);
    }

    private void OnChanged(Entity<WFCamoPatternComponent> ent, ref WFCamoPatternChangedEvent args)
    {
        // The look is a prototype, so its sprite component has no owner.
        if (TryComp<SpriteComponent>(ent, out var sprite) &&
            args.Look.TryGetComponent<SpriteComponent>(out var lookSprite, EntityManager.ComponentFactory))
            _sprite.CopySprite((EntityUid.Invalid, lookSprite), (ent, sprite));
    }
}
