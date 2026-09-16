namespace PaqAgent.MovimientosTesoreria;

public enum MovimientosTesoreriaResponseShape
{
    Get,
    Create,
    Reversion
}

public sealed record MovimientosTesoreriaOperationDefinition(
    string Operation,
    string StoredProcedure,
    IReadOnlyList<string> Parameters,
    MovimientosTesoreriaResponseShape Shape);

/// <summary>Catálogo D6.6 — Movimientos de tesorería (Get + Create + Reversion).</summary>
public static class MovimientosTesoreriaCatalog
{
    private static readonly Dictionary<string, MovimientosTesoreriaOperationDefinition> ByOperation =
        new(StringComparer.Ordinal)
        {
            ["MovimientosTesoreria.Get"] = new(
                "MovimientosTesoreria.Get",
                "dbo.PAQ_MovimientosTesoreria_Get",
                new[] { "cod_comp", "n_comp", "barra" },
                MovimientosTesoreriaResponseShape.Get),
            ["MovimientosTesoreria.Create"] = new(
                "MovimientosTesoreria.Create",
                "(orchestrated)",
                new[]
                {
                    "cod_comp", "barra", "n_comp", "fecha", "fecha_emis", "concepto", "observaciones",
                    "cod_client", "cod_provee", "externo", "force_externo", "usuario",
                    "cotizacion_json", "renglones_json", "clase", "asiento_json"
                },
                MovimientosTesoreriaResponseShape.Create),
            ["MovimientosTesoreria.Reversion"] = new(
                "MovimientosTesoreria.Reversion",
                "(orchestrated)",
                new[]
                {
                    "cod_comp", "n_comp", "barra", "row_version", "concepto", "observaciones", "usuario"
                },
                MovimientosTesoreriaResponseShape.Reversion),
        };

    public static IReadOnlyCollection<string> Operations => ByOperation.Keys;

    public static bool TryGet(string operation, out MovimientosTesoreriaOperationDefinition definition) =>
        ByOperation.TryGetValue(operation, out definition!);
}
