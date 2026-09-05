using System.Text.Json;
using PaqAgent.Auth;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class AuthLoginRunnerTests
{
    [Fact]
    public async Task RunAsync_withoutSqlConfig_returnsDegraded()
    {
        var runner = new AuthLoginRunner(new FakeSpExecutor(_ => throw new InvalidOperationException("no call")));
        var options = new AgentOptions
        {
            AgentId = "lab-agent-01",
            ClientId = "lab",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub"
        };

        var outcome = await runner.RunAsync(
            options,
            new Dictionary<string, object?> { ["codigo"] = "01" },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Degraded, outcome.Status);
        Assert.Equal("SQL_NOT_CONFIGURED", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_missingCodigo_returnsInvalidParameters()
    {
        var runner = new AuthLoginRunner(new FakeSpExecutor(_ => throw new InvalidOperationException("no call")));
        var options = LabOptionsWithSql();

        var outcome = await runner.RunAsync(
            options,
            new Dictionary<string, object?>(),
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_okResultSets_returnsSuccessPayload()
    {
        var header = new Dictionary<string, object?>
        {
            ["status"] = "OK",
            ["user_id"] = 7,
            ["codigo"] = "01",
            ["name_user"] = "Admin",
            ["email"] = "a@b.c",
            ["password_hash"] = "$2y$10$hash",
            ["locale"] = "es",
            ["menu_abrir_nueva_pestana"] = false,
            ["sidebar_collapsed"] = true,
            ["es_admin"] = true,
            ["redirectTo"] = "layout"
        };
        var empresas = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["id"] = 1,
                ["nombreEmpresa"] = "Demo",
                ["nombreBd"] = "DEMO",
                ["theme"] = "default",
                ["imagen"] = null
            }
        };

        var runner = new AuthLoginRunner(new FakeSpExecutor(_ =>
            new List<IReadOnlyList<Dictionary<string, object?>>> { new[] { header }, empresas }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["codigo"] = "01" },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal("OK", data["status"]);
        Assert.True((bool)data["es_admin"]!);
        var user = Assert.IsType<Dictionary<string, object?>>(data["user"]);
        Assert.Equal("$2y$10$hash", user["password_hash"]);
        var empresasOut = Assert.IsType<List<Dictionary<string, object?>>>(data["empresas"]);
        Assert.Single(empresasOut);
    }

    [Fact]
    public async Task RunAsync_notFound_returnsFailedNotFound()
    {
        var header = new Dictionary<string, object?> { ["status"] = "NOT_FOUND" };
        var runner = new AuthLoginRunner(new FakeSpExecutor(_ =>
            new List<IReadOnlyList<Dictionary<string, object?>>> { new[] { header } }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["codigo"] = JsonDocument.Parse("\"X\"").RootElement },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("NOT_FOUND", outcome.ErrorCode);
    }

    private static AgentOptions LabOptionsWithSql() =>
        new()
        {
            AgentId = "lab-agent-01",
            ClientId = "lab",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub",
            Sql = new SqlOptions { Server = "localhost", Database = "lab", User = "u", Password = "p" }
        };

    private sealed class FakeSpExecutor : IAuthLoginSpExecutor
    {
        private readonly Func<string, IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> factory;

        public FakeSpExecutor(Func<string, IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> factory)
        {
            this.factory = factory;
        }

        public Task<IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> ExecuteAsync(
            string connectionString,
            string codigo,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(factory(codigo));
    }
}
