using Content.Shared.Roles;

namespace Content.Server._WF.Lattice;

/// <summary>On a Choir player's mind role, so admin tools list them as an antagonist.</summary>
[RegisterComponent]
public sealed partial class WFChoirRoleComponent : BaseMindRoleComponent;

/// <summary>On a Choir member's PDA: red glyphs scratched into it, for anyone who looks.</summary>
[RegisterComponent]
public sealed partial class WFChoirMarkComponent : Component;
