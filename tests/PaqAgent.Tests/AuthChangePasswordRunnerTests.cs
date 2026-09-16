using PaqAgent.Auth;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class AuthChangePasswordRunnerTests
{
    [Fact]
    public async Task RunAsync_withoutSqlConfig_returnsDegraded()
    {
        var runner = new AuthChangePasswordRunner(new FakeSpExecutor((_, _, _) => throw new InvalidOperationException("no call")));
        var options = new AgentOptions
        {
            AgentId = "lab-agent-01",
            ClientId = "lab",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub"
        };

        var outcome = await runner.RunAsync(
            options,
            ValidParameters(),
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Degraded, outcome.Status);
        Assert.Equal("SQL_NOT_CONFIGURED", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_missingParameters_returnsInvalidParameters()
    {
        var runner = new AuthChangePasswordRunner(new FakeSpExecutor((_, _, _) => throw new InvalidOperationException("no call")));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>(),
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_okResultSet_returnsSuccess()
    {
        var header = new Dictionary<string, object?> { ["status"] = "OK" };
        var runner = new AuthChangePasswordRunner(new FakeSpExecutor((_, _, _) =>
            new List<IReadOnlyList<Dictionary<string, object?>>> { new[] { header } }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            ValidParameters(),
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal("OK", data["status"]);
    }

    [Fact]
    public async Task RunAsync_notFound_returnsFailedNotFound()
    {
        var header = new Dictionary<string, object?> { ["status"] = "NOT_FOUND" };
        var runner = new AuthChangePasswordRunner(new FakeSpExecutor((_, _, _) =>
            new List<IReadOnlyList<Dictionary<string, object?>>> { new[] { header } }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            ValidParameters(),
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("NOT_FOUND", outcome.ErrorCode);
    }

    private static Dictionary<string, object?> ValidParameters() =>
        new()
        {
            ["user_id"] = 77,
            ["codigo"] = "PQ",
            ["password_hash"] = "$2y$12$abcdefghijklmnopqrstuv"
        };

    private static AgentOptions LabOptionsWithSql() =>
        new()
        {
            AgentId = "lab-agent-01",
            ClientId = "lab",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub",
            Sql = new SqlOptions { Server = "localhost", Database = "lab", User = "u", Password = "p" }
        };

    private sealed class FakeSpExecutor : IAuthChangePasswordSpExecutor
    {
        private readonly Func<int, string, string, IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> factory;

        public FakeSpExecutor(Func<int, string, string, IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> factory)
        {
            this.factory = factory;
        }

        public Task<IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> ExecuteAsync(
            string connectionString,
            int userId,
            string codigo,
            string passwordHash,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(factory(userId, codigo, passwordHash));
    }
}
