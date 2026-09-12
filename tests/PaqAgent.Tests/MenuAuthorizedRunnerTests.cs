using System.Text.Json;
using PaqAgent.Menu;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class MenuAuthorizedRunnerTests
{
    [Fact]
    public async Task RunAsync_withoutSqlConfig_returnsDegraded()
    {
        var runner = new MenuAuthorizedRunner(new FakeSpExecutor((_, _) => throw new InvalidOperationException("no call")));
        var options = new AgentOptions
        {
            AgentId = "lab-agent-01",
            ClientId = "lab",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub"
        };

        var outcome = await runner.RunAsync(
            options,
            new Dictionary<string, object?> { ["user_id"] = 1, ["empresa_id"] = 8 },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Degraded, outcome.Status);
        Assert.Equal("SQL_NOT_CONFIGURED", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_missingParams_returnsInvalidParameters()
    {
        var runner = new MenuAuthorizedRunner(new FakeSpExecutor((_, _) => throw new InvalidOperationException("no call")));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["user_id"] = 0 },
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
            ["empresa_id"] = 8,
            ["acceso_total"] = true,
            ["error_message"] = null
        };
        var items = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["id"] = 10,
                ["text"] = "Seguridad",
                ["parentId"] = null,
                ["orden"] = 1,
                ["routeName"] = null,
                ["procedimiento"] = null,
                ["icon_name"] = "shield",
                ["tipo_proceso"] = null
            },
            new()
            {
                ["id"] = 11,
                ["text"] = "Usuarios",
                ["parentId"] = 10,
                ["orden"] = 1,
                ["routeName"] = "seguridad.usuarios",
                ["procedimiento"] = "seguridad_usuarios",
                ["icon_name"] = null,
                ["tipo_proceso"] = "A"
            }
        };

        var runner = new MenuAuthorizedRunner(new FakeSpExecutor((_, _) =>
            new List<IReadOnlyList<Dictionary<string, object?>>> { new[] { header }, items }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["user_id"] = JsonDocument.Parse("2377").RootElement,
                ["empresa_id"] = "8"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal(8, data["empresaId"]);
        Assert.True((bool)data["accesoTotal"]!);
        var itemsOut = Assert.IsType<List<Dictionary<string, object?>>>(data["items"]);
        Assert.Equal(2, itemsOut.Count);
        var procs = Assert.IsType<List<string>>(data["procedimientos"]);
        Assert.Contains("seguridad_usuarios", procs);
    }

    [Fact]
    public async Task RunAsync_sqlError_returnsFailed()
    {
        var header = new Dictionary<string, object?>
        {
            ["status"] = "SQL_ERROR",
            ["empresa_id"] = 8,
            ["acceso_total"] = false,
            ["error_message"] = "boom"
        };
        var runner = new MenuAuthorizedRunner(new FakeSpExecutor((_, _) =>
            new List<IReadOnlyList<Dictionary<string, object?>>> { new[] { header } }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["userId"] = 1, ["empresaId"] = 8 },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("SQL_ERROR", outcome.ErrorCode);
        Assert.Equal("boom", outcome.ErrorMessage);
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

    private sealed class FakeSpExecutor : IMenuAuthorizedSpExecutor
    {
        private readonly Func<int, int, IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> factory;

        public FakeSpExecutor(Func<int, int, IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> factory)
        {
            this.factory = factory;
        }

        public Task<IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> ExecuteAsync(
            string connectionString,
            int userId,
            int empresaId,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(factory(userId, empresaId));
    }
}
