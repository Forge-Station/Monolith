using Content.Shared.Ghost;
using Robust.Client.GameObjects;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Client.Ghost;

public sealed partial class GhostToggleSelfVisibility : LocalizedEntityCommands
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override string Command => "toggleselfghost";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var attachedEntity = shell.Player?.AttachedEntity;
        if (!attachedEntity.HasValue)
            return;

        if (!EntityManager.HasComponent<GhostComponent>(attachedEntity))
        {
            shell.WriteError(Loc.GetString($"cmd-toggleselfghost-must-be-ghost"));
            return;
        }

        if (!EntityManager.TryGetComponent(attachedEntity, out SpriteComponent? spriteComponent))
            return;

        var entityManager = IoCManager.Resolve<IEntityManager>();
        var spriteSys = entityManager.System<SpriteSystem>();
        spriteSys.SetVisible((attachedEntity.Value, spriteComponent), !spriteComponent.Visible);
    }
}
