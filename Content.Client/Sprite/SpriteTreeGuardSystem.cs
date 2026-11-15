using Robust.Client.ComponentTrees;
using Robust.Shared.GameObjects;

namespace Content.Client.Sprite;

/// <summary>
/// Runs immediately before the sprite tree reads bounds.
/// Tests assign <see cref="Repair"/> so a debug client does not throw when a drawn layer is still dirty.
/// </summary>
public sealed class SpriteTreeGuardSystem : EntitySystem
{
    /// <summary>
    /// Set by the integration-test assembly. The game leaves this empty.
    /// </summary>
    public static Action<IEntityManager>? Repair;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(SpriteTreeSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        Repair?.Invoke(EntityManager);
    }
}
