using PaqAgent.Options;
using PaqAgent.PedidosVenta;
using PaqContracts;

namespace PaqAgent.Tests;

public class PedidosVentaCreateRunnerTests
{
    [Fact]
    public void Catalog_containsGetAndCreate()
    {
        Assert.Equal(4, PedidosVentaCatalog.Operations.Count);
        Assert.True(PedidosVentaCatalog.TryGet("PedidosVenta.Create", out var def));
        Assert.Equal(PedidosVentaResponseShape.Create, def.Shape);
    }

    [Fact]
    public void Encriptacion_roundTrip_preservesValue()
    {
        for (var n = 0; n < 50; n++)
        {
            var enc = EncriptacionTalonarios.CrypNro(n * 17 + 3);
            Assert.Equal(16, enc.Length);
            Assert.Equal(n * 17 + 3, EncriptacionTalonarios.UnCrypNro(enc));
        }
    }

    [Fact]
    public void Encriptacion_crearNumero_formato14()
    {
        var nro = EncriptacionTalonarios.CrearNumero("A", "1", 28029);
        Assert.Equal(14, nro.Length);
        Assert.StartsWith("A", nro);
        Assert.EndsWith("00028029", nro);
    }

    [Fact]
    public async Task Create_missingParams_returnsInvalidParameters()
    {
        var runner = new PedidosVentaCreateRunner();
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["talon_ped"] = 1 },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task Create_ocasional_rejected()
    {
        var runner = new PedidosVentaCreateRunner();
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["talon_ped"] = 1,
                ["cod_client"] = "000000",
                ["n_lista"] = 1,
                ["renglones_json"] = """[{"codArticu":"A","cantPedid":1,"precio":10}]"""
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("OCASIONAL_NO_SOPORTADO", outcome.ErrorCode);
    }

    private static AgentOptions LabOptionsWithSql() =>
        new()
        {
            AgentId = "a",
            ClientId = "c",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub",
            Sql = new SqlOptions { Server = "localhost", Database = "dic", User = "sa", Password = "x" }
        };
}
