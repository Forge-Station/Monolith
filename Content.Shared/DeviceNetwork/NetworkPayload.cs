using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Utility;

namespace Content.Shared.DeviceNetwork;

/// <summary>
/// A data class for information passing through a Device Network.
/// </summary>
[ImplicitDataRecord]
public partial interface INetworkPayload;

public sealed class NetworkPayload : Dictionary<string, object?>
{
    public bool TryGetValue<T>(string key, [NotNullWhen(true)] out T? value)
    {
        if (this.TryCastValue(key, out T? result))
        {
            value = result;
            return true;
        }

        value = default;
        return false;
    }
}
