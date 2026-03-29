using System.Collections.Generic; // Forge-Change
using System.Reflection; // Forge-Change
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.UnitTesting;
using Robust.UnitTesting.Pool; // Forge-Change

namespace Content.IntegrationTests.Tests;

public sealed class LogErrorTest
{
    /// <summary>
    ///     This test ensures that error logs cause tests to fail.
    /// </summary>
    /// <remarks>
    ///     Forge-Change: For older engine versions, the clean return would fail because the log call would throw.
    ///     Pooled integration tests record failing log levels and assert on clean return; they do not throw from the log call itself.
    ///     Intentional errors are cleared via reflection so clean return can succeed without new engine API on
    ///     <see cref="PoolTestLogHandler" />.
    /// </remarks>
    [Test]
    public async Task TestLogErrorCausesTestFailure()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;

        var cfg = server.ResolveDependency<IConfigurationManager>();
        var serverLog = server.ResolveDependency<ILogManager>().RootSawmill; // Forge-Change: Added
        var clientLog = client.ResolveDependency<ILogManager>().RootSawmill; // Forge-Change: Added

        Assert.That(cfg.GetCVar(RTCVars.FailureLogLevel), Is.EqualTo(LogLevel.Error));

        await server.WaitPost(() => serverLog.Warning("test"));
        Assert.That(pair.ServerLogHandler.FailingLogs.Count, Is.EqualTo(0)); // Forge-Change: Added

        await server.WaitPost(() => serverLog.Error("test")); // Forge-Change: Added
        Assert.That(pair.ServerLogHandler.FailingLogs.Count, Is.EqualTo(1)); // Forge-Change: Added
        ClearPoolHandlerFailingLogs(pair.ServerLogHandler); // Forge-Change: Added

        await client.WaitPost(() => clientLog.Error("test")); // Forge-Change: Added
        Assert.That(pair.ClientLogHandler.FailingLogs.Count, Is.EqualTo(1)); // Forge-Change: Added
        ClearPoolHandlerFailingLogs(pair.ClientLogHandler); // Forge-Change: Added

        await pair.CleanReturnAsync();
    }

    private static void ClearPoolHandlerFailingLogs(PoolTestLogHandler handler) // Forge-Change: Added
    {
        var field = typeof(PoolTestLogHandler).GetField("_failingLogs", BindingFlags.NonPublic | BindingFlags.Instance); // Forge-Change: Added
        ArgumentNullException.ThrowIfNull(field); // Forge-Change: Added
        ((List<string>)field.GetValue(handler)!).Clear(); // Forge-Change: Added
    }
}
