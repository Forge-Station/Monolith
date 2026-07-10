// Author: @lenta313. Все права не защищены / No rights reserved.
using Content.Shared.Actions;

namespace Content.Shared._Forge.EmergencyLimb;

/// <summary>
/// Fired by the regrow action. Regrows the missing limb selected on the user's targeting doll,
/// spending one stored charge.
/// </summary>
public sealed partial class RegrowEmergencyLimbEvent : InstantActionEvent
{
}
