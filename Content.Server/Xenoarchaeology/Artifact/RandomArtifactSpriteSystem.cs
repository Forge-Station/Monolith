// WOLFGATE(Xenoarch) START: wizden's xenoarch rework
using Content.Shared.Item;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.XenoArtifacts;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Xenoarchaeology.Artifact;

public sealed class RandomArtifactSpriteSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _time = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;

    /// <summary> Artifacts still showing their activation effect. </summary>
    private readonly List<Entity<RandomArtifactSpriteComponent>> _activated = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RandomArtifactSpriteComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RandomArtifactSpriteComponent, ArtifactUnlockingStartedEvent>(UnlockingStageStarted);
        SubscribeLocalEvent<RandomArtifactSpriteComponent, ArtifactUnlockingFinishedEvent>(UnlockingStageFinished);
        SubscribeLocalEvent<RandomArtifactSpriteComponent, XenoArtifactActivatedEvent>(ArtifactActivated);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        for (var i = _activated.Count - 1; i >= 0; i--)
        {
            var (uid, component) = _activated[i];
            if (component.Deleted || component.ActivationStart == null)
            {
                _activated.RemoveAt(i);
                continue;
            }

            var timeDif = _time.CurTime - component.ActivationStart.Value;
            if (timeDif.TotalSeconds < component.ActivationTime)
                continue;

            _appearance.SetData(uid, SharedArtifactsVisuals.IsActivated, false);
            component.ActivationStart = null;
            _activated.RemoveAt(i);
        }
    }

    private void OnMapInit(EntityUid uid, RandomArtifactSpriteComponent component, MapInitEvent args)
    {
        var randomSprite = _random.Next(component.MinSprite, component.MaxSprite + 1);
        _appearance.SetData(uid, SharedArtifactsVisuals.SpriteIndex, randomSprite);
        _item.SetHeldPrefix(uid, "ano" + randomSprite.ToString("D2")); //set item artifact inhands
    }

    private void UnlockingStageStarted(Entity<RandomArtifactSpriteComponent> ent, ref ArtifactUnlockingStartedEvent args)
    {
        _appearance.SetData(ent, SharedArtifactsVisuals.IsUnlocking, true);
    }

    private void UnlockingStageFinished(Entity<RandomArtifactSpriteComponent> ent, ref ArtifactUnlockingFinishedEvent args)
    {
        _appearance.SetData(ent, SharedArtifactsVisuals.IsUnlocking, false);
    }

    private void ArtifactActivated(Entity<RandomArtifactSpriteComponent> ent, ref XenoArtifactActivatedEvent args)
    {
        _appearance.SetData(ent, SharedArtifactsVisuals.IsActivated, true);
        if (ent.Comp.ActivationStart == null)
            _activated.Add(ent);
        ent.Comp.ActivationStart = _time.CurTime;
    }
}
// WOLFGATE END
