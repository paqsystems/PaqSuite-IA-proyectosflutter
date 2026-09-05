using PaqAgent.Diagnostics;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class DiagnosticsRunnerTests
{
    [Fact]
    public async Task RunAsync_withoutSqlConfig_returnsDegraded()
    {
        var runner = new DiagnosticsRunner(new FakePinger(ok: true));
        var options = new AgentOptions
        {
            AgentId = "lab-agent-01",
            ClientId = "lab",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub"
        };

        var outcome = await runner.RunAsync(options, "1.0.0", "TEST-PC", CancellationToken.None);

        Assert.Equal(JobStatuses.Degraded, outcome.Status);
        Assert.False(outcome.Data.SqlConnectionOk);
        Assert.Equal("gateway_authenticated", outcome.Data.Readiness);
        Assert.Equal("SQL_NOT_CONFIGURED", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_sqlUnreachable_returnsDegraded()
    {
        var runner = new DiagnosticsRunner(new FakePinger(ok: false, error: "timeout"));
        var options = new AgentOptions
        {
            AgentId = "lab-agent-01",
            ClientId = "lab",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub",
            Sql = new SqlOptions { Server = "bad", Database = "db", User = "u", Password = "p" }
        };

        var outcome = await runner.RunAsync(options, "1.0.0", "TEST-PC", CancellationToken.None);

        Assert.Equal(JobStatuses.Degraded, outcome.Status);
        Assert.False(outcome.Data.SqlConnectionOk);
        Assert.Equal("SQL_UNREACHABLE", outcome.ErrorCode);
        Assert.Equal("lab-agent-01", outcome.Data.AgentId);
    }

    [Fact]
    public async Task RunAsync_sqlOk_returnsSuccessOperational()
    {
        var runner = new DiagnosticsRunner(new FakePinger(ok: true));
        var options = new AgentOptions
        {
            AgentId = "lab-agent-01",
            ClientId = "lab",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub",
            Sql = new SqlOptions { Server = "localhost", Database = "lab", User = "u", Password = "p" }
        };

        var outcome = await runner.RunAsync(options, "1.2.3", "TEST-PC", CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        Assert.True(outcome.Data.SqlConnectionOk);
        Assert.Equal("operational", outcome.Data.Readiness);
        Assert.Equal("1.2.3", outcome.Data.AgentVersion);
        Assert.Equal("localhost", outcome.Data.SqlServerName);
    }

    private sealed class FakePinger : ISqlConnectionPinger
    {
        private readonly bool ok;
        private readonly string? error;

        public FakePinger(bool ok, string? error = null)
        {
            this.ok = ok;
            this.error = error;
        }

        public Task<SqlPingResult> PingAsync(SqlOptions sql, CancellationToken cancellationToken) =>
            Task.FromResult(new SqlPingResult { Ok = ok, ErrorMessage = error });
    }
}
