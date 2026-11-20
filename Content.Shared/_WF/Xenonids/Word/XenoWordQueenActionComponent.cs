using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Word;

[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoWordQueenSystem))]
public sealed partial class XenoWordQueenActionComponent : Component;
