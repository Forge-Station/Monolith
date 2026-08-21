using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Features;

[Serializable, NetSerializable]
public enum RemnantConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class RemnantConsoleUiState : BoundUserInterfaceState
{
    public List<string> ButtonLabels;

    public RemnantConsoleUiState(List<string> buttonLabels)
    {
        ButtonLabels = buttonLabels;
    }
}

[Serializable, NetSerializable]
public sealed class RemnantConsolePressButtonMessage : BoundUserInterfaceMessage
{
    public int Index;

    public RemnantConsolePressButtonMessage(int index)
    {
        Index = index;
    }
}
