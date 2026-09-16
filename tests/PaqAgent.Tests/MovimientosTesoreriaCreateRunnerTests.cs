using PaqAgent.MovimientosTesoreria;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class MovimientosTesoreriaCreateRunnerTests
{
    [Fact]
    public void Catalog_containsGetAndCreate()
    {
        Assert.Equal(3, MovimientosTesoreriaCatalog.Operations.Count);
        Assert.True(MovimientosTesoreriaCatalog.TryGet("MovimientosTesoreria.Create", out var def));
        Assert.Equal(MovimientosTesoreriaResponseShape.Create, def.Shape);
        Assert.Equal("(orchestrated)", def.StoredProcedure);
    }

    [Fact]
    public async Task Create_missingParams_returnsInvalidParameters()
    {
        var runner = new MovimientosTesoreriaCreateRunner();
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["cod_comp"] = "REC" },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task Create_invalidRenglonesJson_returnsInvalidParameters()
    {
        var runner = new MovimientosTesoreriaCreateRunner();
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["cod_comp"] = "REC",
                ["renglones_json"] = "{not-json"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
        Assert.Contains("renglones_json", outcome.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseRenglones_mapsCamelAndSnake()
    {
        var list = MovimientosTesoreriaCreateRunner.ParseRenglones(
            """[{"renglon":0,"codCta":10,"dH":"H","monto":100},{"renglon":1,"cod_cta":20,"d_h":"D","monto":100,"leyenda":"x"}]""");

        Assert.Equal(2, list.Count);
        Assert.Equal(0, list[0].Renglon);
        Assert.Equal(10, list[0].CodCta);
        Assert.Equal("H", list[0].DH);
        Assert.Equal(100m, list[0].Monto);
        Assert.Equal(20, list[1].CodCta);
        Assert.Equal("D", list[1].DH);
        Assert.Equal("x", list[1].Leyenda);
    }

    [Fact]
    public void ValidateRenglones_partidaDesbalanceada()
    {
        var list = MovimientosTesoreriaCreateRunner.ParseRenglones(
            """[{"renglon":0,"codCta":1,"dH":"H","monto":100},{"renglon":1,"codCta":2,"dH":"D","monto":50}]""");
        var err = MovimientosTesoreriaCreateRunner.ValidateRenglonesEstructura(list);
        Assert.NotNull(err);
        Assert.Contains("igualar", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateRenglones_okEstructuraMinima()
    {
        var list = MovimientosTesoreriaCreateRunner.ParseRenglones(
            """[{"renglon":0,"codCta":1,"dH":"H","monto":100},{"renglon":1,"codCta":2,"dH":"D","monto":100}]""");
        Assert.Null(MovimientosTesoreriaCreateRunner.ValidateRenglonesEstructura(list));
    }

    [Fact]
    public async Task Create_partidaDesbalanceada_returnsValidation()
    {
        var runner = new MovimientosTesoreriaCreateRunner();
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["cod_comp"] = "REC",
                ["renglones_json"] =
                    """[{"renglon":0,"codCta":1,"dH":"H","monto":100},{"renglon":1,"codCta":2,"dH":"D","monto":50}]"""
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("VALIDATION", outcome.ErrorCode);
    }

    [Fact]
    public void Matriz_clase1_principalYFondos()
    {
        Assert.True(MovimientosTesoreriaMatrizClases.ClaseSoportada(1));
        Assert.True(MovimientosTesoreriaMatrizClases.Admite(
            1, MovimientosTesoreriaMatrizClases.RolPrincipal, "H", "O", null));
        Assert.False(MovimientosTesoreriaMatrizClases.Admite(
            1, MovimientosTesoreriaMatrizClases.RolPrincipal, "D", "O", null));
        Assert.True(MovimientosTesoreriaMatrizClases.Admite(
            1, MovimientosTesoreriaMatrizClases.RolFondos, "D", "B", "C"));
        Assert.False(MovimientosTesoreriaMatrizClases.Admite(
            1, MovimientosTesoreriaMatrizClases.RolFondos, "D", "B", "D"));
    }

    [Fact]
    public void FormatNComp_espacioMas13Digitos()
    {
        var n = MovimientosTesoreriaCreateRunner.FormatNComp(42);
        Assert.Equal(14, n.Length);
        Assert.StartsWith(" ", n);
        Assert.EndsWith("0000000000042", n);
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
