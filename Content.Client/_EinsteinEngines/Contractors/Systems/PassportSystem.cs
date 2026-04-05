using Content.Shared._EE.Contractors.Components;
using Content.Shared._EE.Contractors.Systems;
using Robust.Client.GameObjects;


namespace Content.Client._EE.Contractors.Systems;

public sealed class PassportSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PassportComponent, ComponentStartup>(OnPassportStartup); // Forge-Change: added passport startup event
        SubscribeLocalEvent<PassportComponent, SharedPassportSystem.PassportToggleEvent>(OnPassportToggled);
        // SubscribeLocalEvent<PassportComponent, SharedPassportSystem.PassportProfileUpdatedEvent>(OnPassportProfileUpdated);
    }

    // public void OnPassportProfileUpdated(Entity<PassportComponent> passport, ref SharedPassportSystem.PassportProfileUpdatedEvent evt)
    // {
    //     if(!_timing.IsFirstTimePredicted || evt.Handled || !_entityManager.TryGetComponent<SpriteComponent>(passport, out var sprite))
    //         return;

    //     var profile = evt.Profile;

    //     var currentState = sprite.LayerGetState(1);

    //     if (currentState.Name == null)
    //         return;

    //     sprite.LayerSetState(1, currentState.Name.Replace("human", profile.Species.ToString().ToLower(CultureInfo.CurrentCulture)));
    // }

    private void OnPassportToggled(Entity<PassportComponent> passport, ref SharedPassportSystem.PassportToggleEvent evt)
    {
        // Note: we intentionally don't gate on IsFirstTimePredicted here.
        // Fast open/close can involve prediction replays/corrections and skipping visual updates
        // would leave the sprite in the wrong open/closed state.
        if (evt.Handled || !_entityManager.TryGetComponent<SpriteComponent>(passport, out var sprite)) // Forge-Change: added Forge-Change prefix to the function name
            return;

        var currentState = sprite.LayerGetState(0);

        if (currentState.Name == null)
            return;

        evt.Handled = true;

        // Forge-Change-start: added Forge-Change prefix to the function name
        // Convert "..._open/closed" -> "..._closed/open" deterministically.
        // Using a direct suffix swap avoids issues with partial string matches during rapid toggles.
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

    private void OnPassportStartup(Entity<PassportComponent> passport, ref ComponentStartup args)
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

        // Keep the same safety net as in OnPassportToggled.
        if (prefix == currentName && desiredStateName.Contains("_open") && desiredStateName.Contains("_closed"))
        {
            var from = passport.Comp.IsClosed ? "_open" : "_closed";
            var to = passport.Comp.IsClosed ? "_closed" : "_open";
            desiredStateName = currentName.Replace(from, to, StringComparison.Ordinal);
        }

        if (desiredStateName != currentName)
            sprite.LayerSetState(0, desiredStateName);
        // Forge-Change-end: added Forge-Change prefix to the function name
    }
}
