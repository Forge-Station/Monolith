using Robust.Shared.Serialization;

namespace Content.Shared._NF.PlantAnalyzer;

[Serializable, NetSerializable]
public enum PlantAnalyzerUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class PlantAnalyzerPrintLabelMessage : BoundUserInterfaceMessage;

