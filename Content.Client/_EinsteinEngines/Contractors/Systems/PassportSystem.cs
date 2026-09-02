using Content.Shared._EE.Contractors.Components;
using Content.Shared._EE.Contractors.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Client._EE.Contractors.Systems;

// Forge-change-start
public sealed class PassportSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PassportComponent, ComponentStartup>(OnPassportStartup);
        SubscribeLocalEvent<PassportComponent, AfterAutoHandleStateEvent>(OnPassportState);
        SubscribeLocalEvent<PassportComponent, SharedPassportSystem.PassportToggleEvent>(OnPassportToggled);
    }

    private void OnPassportToggled(Entity<PassportComponent> passport, ref SharedPassportSystem.PassportToggleEvent evt)
    {
        if (evt.Handled)
            return;

        evt.Handled = true;
        UpdateOpenClosedSprite(passport);
    }

    private void OnPassportStartup(Entity<PassportComponent> passport, ref ComponentStartup args)
    {
        UpdateOpenClosedSprite(passport);
    }

    private void OnPassportState(Entity<PassportComponent> passport, ref AfterAutoHandleStateEvent args)
    {
        UpdateOpenClosedSprite(passport);
    }

    private void UpdateOpenClosedSprite(Entity<PassportComponent> passport)
    {
        if (!_entityManager.TryGetComponent<SpriteComponent>(passport, out var sprite))
            return;

        var currentState = sprite.LayerGetState(0);
        if (currentState.Name == null)
            return;

        var currentName = currentState.Name;
        var prefix = currentName;

        if (currentName.EndsWith("_open", StringComparison.Ordinal))
            prefix = currentName[..^"_open".Length];
        else if (currentName.EndsWith("_closed", StringComparison.Ordinal))
            prefix = currentName[..^"_closed".Length];

        var desiredStateName = prefix + (passport.Comp.IsClosed ? "_closed" : "_open");

        if (desiredStateName == currentName)
            return;

        // Fallback: if the current state didn't match the expected suffix pattern,
        // try a simple open/closed swap instead of producing "..._open_closed".
        if (prefix == currentName && desiredStateName.Contains("_open") && desiredStateName.Contains("_closed"))
        {
            var from = passport.Comp.IsClosed ? "_open" : "_closed";
            var to = passport.Comp.IsClosed ? "_closed" : "_open";
            desiredStateName = currentName.Replace(from, to, StringComparison.Ordinal);
        }

        if (desiredStateName != currentName)
            sprite.LayerSetState(0, desiredStateName);
    }
}
// Forge-change-end
