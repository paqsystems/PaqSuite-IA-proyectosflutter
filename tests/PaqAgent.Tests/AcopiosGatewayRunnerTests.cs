using PaqAgent.Acopios;
using PaqAgent.Informes;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class AcopiosGatewayRunnerTests
{
    [Fact]
    public void Catalog_containsFourteenOps()
    {
        Assert.Equal(14, AcopiosCatalog.Operations.Count);
        Assert.True(AcopiosCatalog.TryGet("Acopios.Parametros.List", out _));
        Assert.True(AcopiosCatalog.TryGet("Acopios.ListaPrecios.Opciones", out _));
    }

    [Fact]
    public async Task RunAsync_missingDatabase_returnsInvalidParameters()
    {
        var runner = new AcopiosGatewayRunner(new FakeExecutor((_, _, _) => throw new InvalidOperationException("no call")));
        Assert.True(AcopiosCatalog.TryGet("Acopios.Parametros.List", out var def));

        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>(),
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_parametros_mapsPayload()
    {
        var runner = new AcopiosGatewayRunner(new FakeExecutor((_, _, _) =>
            new List<IReadOnlyList<Dictionary<string, object?>>>
            {
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase) { ["total_filas"] = 1 }
                },
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Clave"] = "Prefijo",
                        ["Tipo_Valor"] = "S",
                        ["Valor_String"] = "ACO"
                    }
                }
            }));

        Assert.True(AcopiosCatalog.TryGet("Acopios.Parametros.List", out var def));
        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["_database"] = "EMP" },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        var parametros = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(data["parametros"]);
        Assert.Single(parametros);
    }

    [Fact]
    public async Task RunAsync_create_resultCodeNotOk_fails()
    {
        var runner = new AcopiosGatewayRunner(new FakeExecutor((_, _, _) =>
            new List<IReadOnlyList<Dictionary<string, object?>>>
            {
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase) { ["resultCode"] = "yaExiste" }
                }
            }));

        Assert.True(AcopiosCatalog.TryGet("Acopios.FacturaAcopio.Create", out var def));
        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["t_comp"] = "FAC",
                ["n_comp"] = "1"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("yaExiste", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_batch_usesDictionaryConnection()
    {
        string? capturedCs = null;
        var runner = new AcopiosGatewayRunner(new FakeExecutor((cs, _, _) =>
        {
            capturedCs = cs;
            return new List<IReadOnlyList<Dictionary<string, object?>>>
            {
                Array.Empty<Dictionary<string, object?>>()
            };
        }));

        Assert.True(AcopiosCatalog.TryGet("Acopios.PedidoDetallesBatch.Get", out var def));
        Assert.False(def.UseCompanyDatabaseOverride);

        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["pedidos_xml"] = "<x/>",
                ["dictionary_db"] = "dic",
                ["grupo_id"] = 1
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
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
