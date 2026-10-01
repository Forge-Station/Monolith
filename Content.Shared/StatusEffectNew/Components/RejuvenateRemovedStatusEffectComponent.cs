// WOLFGATE(Wolfmed): StatusEffectNew framework vendored from Space Onyx (2f5bab9), Onyx's copy of upstream SS14's, for the wound port.
using Content.Shared.Damage.Components;
using Content.Shared.Rejuvenate;
using Robust.Shared.GameStates;

namespace Content.Shared.StatusEffectNew.Components;

/// <summary>
/// Marker component for a status effect that should be removed on rejuvenation
/// and should not be applied on targets with <see cref="GodmodeComponent" />.
/// Only applies to effects using the new <see cref="StatusEffectsSystem" />.
/// </summary>
/// <seealso cref="RejuvenateEvent"/>
[RegisterComponent, NetworkedComponent]
public sealed partial class RejuvenateRemovedStatusEffectComponent : Component;
