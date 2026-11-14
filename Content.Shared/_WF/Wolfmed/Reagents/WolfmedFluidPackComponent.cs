using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Reagents;

/// <summary>
/// Playtest 3 IPC 2: a pack whose contents go straight into a body's own fluid, the way a chassis is refilled. The pack
/// only gives what the body runs on: anything else in it refuses the whole transfer, so an oil pack never reaches an IPC.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedFluidPackComponent : Component
{
    /// <summary>The pack's own solution, which is also what the autodoc drains when it is loaded.</summary>
    [DataField]
    public string Solution = "pack";

    /// <summary>Units moved in one use. The use repeats until the body is full or the pack is empty.</summary>
    [DataField]
    public FixedPoint2 TransferAmount = 25;

    /// <summary>Seconds a use takes.</summary>
    [DataField]
    public float Delay = 2f;

    [DataField]
    public SoundSpecifier? UseSound = new SoundPathSpecifier("/Audio/Items/Medical/brutepack_end.ogg");
}

/// <summary>Raised when one use of a fluid pack finishes.</summary>
[Serializable, NetSerializable]
public sealed partial class WolfmedFluidPackDoAfterEvent : SimpleDoAfterEvent;
