using System;
using Content.Shared;
using Content.Shared.Module;

namespace Content.Client.IoC
{
public sealed class ClientModuleTestingCallbacks : SharedModuleTestingCallbacks
{
    public Action? ClientBeforeIoC { get; set; }

    /// <summary>
    /// Runs after texture preload, still on the client thread. The body lives in the test assembly.
    /// </summary>
    public Action? ClientPostInit { get; set; }
}
}
