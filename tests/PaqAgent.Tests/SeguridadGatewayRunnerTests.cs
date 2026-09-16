using PaqAgent.Informes;
using PaqAgent.Options;
using PaqAgent.Seguridad;
using PaqContracts;

namespace PaqAgent.Tests;

public class SeguridadGatewayRunnerTests
{
    [Fact]
    public void Catalog_containsFiveOps()
    {
        Assert.Equal(5, SeguridadCatalog.Operations.Count);
        Assert.True(SeguridadCatalog.TryGet("seguridad.roles.list", out _));
        Assert.True(SeguridadCatalog.TryGet("seguridad.users.list", out _));
        Assert.True(SeguridadCatalog.TryGet("seguridad.empresas.list", out _));
        Assert.True(SeguridadCatalog.TryGet("seguridad.permisos.list", out _));
        Assert.True(SeguridadCatalog.TryGet("seguridad.grupos-empresarios.list", out _));
    }

    [Fact]
    public async Task RunAsync_withoutSqlConfig_returnsDegraded()
    {
        var runner = new SeguridadGatewayRunner(new FakeExecutor((_, _, _) => throw new InvalidOperationException("no call")));
        Assert.True(SeguridadCatalog.TryGet("seguridad.roles.list", out var def));

        var outcome = await runner.RunAsync(
            def,
            new AgentOptions
            {
                AgentId = "a",
                ClientId = "c",
                AgentToken = "t",
                GatewayUrl = "http://127.0.0.1:5100/agent-hub"
            },
            new Dictionary<string, object?>(),
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Degraded, outcome.Status);
        Assert.Equal("SQL_NOT_CONFIGURED", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_rolesList_mapsItemsPayload()
    {
        var runner = new SeguridadGatewayRunner(new FakeExecutor((_, sp, _) =>
        {
            Assert.Equal("dbo.PAQ_Seguridad_Roles_List", sp);
            return new List<IReadOnlyList<Dictionary<string, object?>>>
            {
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = 1,
                        ["nombreRol"] = "Admin",
                        ["descripcionRol"] = "Total",
                        ["accesoTotal"] = true,
                        ["enUso"] = false
                    }
                }
            };
        }));

        Assert.True(SeguridadCatalog.TryGet("seguridad.roles.list", out var def));
        var outcome = await runner.RunAsync(def, LabOptionsWithSql(), new Dictionary<string, object?>(), 30, CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        var items = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(data["items"]);
        Assert.Single(items);
    }

    [Fact]
    public async Task RunAsync_usersList_mapsFiltersToSpParameters()
    {
        IReadOnlyDictionary<string, object?>? capturedParams = null;
        var runner = new SeguridadGatewayRunner(new FakeExecutor((_, sp, spParams) =>
        {
            Assert.Equal("dbo.PAQ_Seguridad_Users_List", sp);
            capturedParams = spParams;
            return new List<IReadOnlyList<Dictionary<string, object?>>> { Array.Empty<Dictionary<string, object?>>() };
        }));

        Assert.True(SeguridadCatalog.TryGet("seguridad.users.list", out var def));
        await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["codigo"] = "PQ",
                ["nombre"] = "Pablo",
                ["activo"] = true
            },
            30,
            CancellationToken.None);

        Assert.NotNull(capturedParams);
        Assert.Equal("PQ", capturedParams!["@Codigo"]?.ToString());
        Assert.Equal("Pablo", capturedParams["@Nombre"]?.ToString());
        Assert.Equal(true, capturedParams["@Activo"]);
    }

    [Fact]
    public async Task RunAsync_permisosList_mapsIdFilters()
    {
        IReadOnlyDictionary<string, object?>? capturedParams = null;
        var runner = new SeguridadGatewayRunner(new FakeExecutor((_, _, spParams) =>
        {
            capturedParams = spParams;
            return new List<IReadOnlyList<Dictionary<string, object?>>> { Array.Empty<Dictionary<string, object?>>() };
        }));

        Assert.True(SeguridadCatalog.TryGet("seguridad.permisos.list", out var def));
        await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["id_usuario"] = 7, ["id_empresa"] = 3 },
            30,
            CancellationToken.None);

        Assert.NotNull(capturedParams);
        Assert.Equal(7, capturedParams!["@IdUsuario"]);
        Assert.Equal(3, capturedParams["@IdEmpresa"]);
    }

    [Fact]
    public async Task RunAsync_usesDictionaryConnectionWithoutDatabaseOverride()
    {
        string? capturedCs = null;
        var runner = new SeguridadGatewayRunner(new FakeExecutor((cs, _, _) =>
        {
            capturedCs = cs;
            return new List<IReadOnlyList<Dictionary<string, object?>>> { Array.Empty<Dictionary<string, object?>>() };
        }));

        Assert.True(SeguridadCatalog.TryGet("seguridad.empresas.list", out var def));
        await runner.RunAsync(def, LabOptionsWithSql(), new Dictionary<string, object?>(), 30, CancellationToken.None);

        Assert.Contains("diccionario_lab", capturedCs, StringComparison.OrdinalIgnoreCase);
    }

    private static AgentOptions LabOptionsWithSql() =>
        new()
        {
            AgentId = "a",
            ClientId = "c",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub",
            Sql = new SqlOptions { Server = "localhost", Database = "diccionario_lab", User = "sa", Password = "x" }
        };

    private sealed class FakeExecutor : IInformesSpExecutor
    {
        private readonly Func<string, string, IReadOnlyDictionary<string, object?>, IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> factory;

        public FakeExecutor(
            Func<string, string, IReadOnlyDictionary<string, object?>, IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> factory)
        {
            this.factory = factory;
        }

        public Task<IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> ExecuteAsync(
            string connectionString,
            string storedProcedure,
            IReadOnlyDictionary<string, object?> spParameters,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(factory(connectionString, storedProcedure, spParameters));
    }
}
