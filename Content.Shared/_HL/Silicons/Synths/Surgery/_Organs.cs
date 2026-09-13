using Content.Shared._Shitmed.Medical.Surgery;
using Robust.Shared.GameStates;

namespace Content.Shared._HL.Silicons.Synths.Surgery;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class SynthOrganComponent : Component;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class OrganBrainSynthComponent : Component;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class OrganEyesSynthComponent : Component;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class OrganHeartSynthComponent : Component;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class OrganLungsSynthComponent : Component;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedSurgerySystem))]
public sealed partial class OrganLiverSynthComponent : Component;
