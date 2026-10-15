using Content.Shared._Forge.KIAS.Controllers;

namespace Content.Server._Forge.KIAS.Controllers;

public readonly struct KiasCommandInputs(string[] names, KiasGraphValue[] values)
{
    public KiasGraphValue Read(string name)
    {
        for (var i = 0; i < names.Length; i++)
            if (names[i] == name) return values[i];
        return default;
    }
}
