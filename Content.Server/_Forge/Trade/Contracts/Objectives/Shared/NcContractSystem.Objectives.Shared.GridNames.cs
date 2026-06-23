using Content.Shared._Forge.Trade;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private string ResolveRuntimeGridName(ContractServerData contract, string fallbackPrefix)
    {
        return ResolveRuntimeGridName(contract, fallbackPrefix, null);
    }

    private string ResolveRuntimeGridName(
        ContractServerData contract,
        string fallbackPrefix,
        IReadOnlyList<string>? localNames
    )
    {
        if (TryPickRuntimeGridName(localNames, out var localName))
            return localName;

        if (TryPickRuntimeGridName(contract.Config.GridNames, out var contractName))
            return contractName;

        if (!string.IsNullOrWhiteSpace(contract.Config.GridName))
            return contract.Config.GridName.Trim();

        return string.IsNullOrWhiteSpace(contract.Name)
            ? fallbackPrefix
            : $"{fallbackPrefix}: {contract.Name}";
    }

    private bool TryConfigureContractRadarContact(
        EntityUid grid,
        ContractServerData contract,
        string fallbackPrefix,
        Color color,
        bool alwaysShowColor = false,
        IReadOnlyList<string>? localNames = null
    )
    {
        var name = ResolveRuntimeGridName(contract, fallbackPrefix, localNames).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _shuttle.AddIFFFlag(grid, IFFFlags.HideLabel);
            return false;
        }

        _contractMeta.SetEntityName(grid, name);
        _shuttle.SetIFFColor(grid, color);
        if (alwaysShowColor)
            _shuttle.AddIFFFlag(grid, IFFFlags.AlwaysShowColor);

        _shuttle.RemoveIFFFlag(grid, IFFFlags.Hide | IFFFlags.HideLabel | IFFFlags.HideLabelAlways);
        return true;
    }

    private bool TryPickRuntimeGridName(IReadOnlyList<string>? names, out string name)
    {
        name = string.Empty;
        if (names is not { Count: > 0 })
            return false;

        var candidates = new List<string>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            var candidate = names[i];
            if (!string.IsNullOrWhiteSpace(candidate))
                candidates.Add(candidate.Trim());
        }

        if (candidates.Count == 0)
            return false;

        name = _random.Pick(candidates);
        return true;
    }
}
