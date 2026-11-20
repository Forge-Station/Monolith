using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Pheromones;

[Serializable, NetSerializable]
public enum XenoPheromonesUI
{
    Key
}

[Serializable, NetSerializable]
public sealed class XenoPheromonesChosenBuiMsg(XenoPheromones pheromones) : BoundUserInterfaceMessage
{
    public readonly XenoPheromones Pheromones = pheromones;
}

[Serializable, NetSerializable]
public sealed class XenoPheromonesHelpButtonBuiMsg() : BoundUserInterfaceMessage
{

}