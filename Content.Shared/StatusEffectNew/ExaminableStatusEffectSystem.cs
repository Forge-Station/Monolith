// WOLFGATE(Wolfmed): StatusEffectNew framework vendored from Space Onyx (2f5bab9), Onyx's copy of upstream SS14's, for the wound port.
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.StatusEffectNew.Components;

namespace Content.Shared.StatusEffectNew;

/// <summary>
/// Handler for <see cref="ExaminableStatusEffectComponent"/>.
/// </summary>
public sealed partial class ExaminableStatusEffectSystem : EntitySystem
{
    // WOLFGATE(Wolfmed): RT 277 has no [SubscribeLocalEvent] source generator; subscribe explicitly.
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExaminableStatusEffectComponent, StatusEffectRelayedEvent<ExaminedEvent>>(OnExaminedEvent);
    }

    private void OnExaminedEvent(Entity<ExaminableStatusEffectComponent> ent, ref StatusEffectRelayedEvent<ExaminedEvent> args)
    {
        using (args.Args.PushGroup(nameof(ExaminableStatusEffectSystem)))
        {
            args.Args.PushMarkup(Loc.GetString(ent.Comp.MessageId, ("target", Identity.Entity(args.AppliedTo, EntityManager))));
        }
    }
}
