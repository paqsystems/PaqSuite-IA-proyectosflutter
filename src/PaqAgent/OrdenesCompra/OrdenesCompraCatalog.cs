namespace PaqAgent.OrdenesCompra;

public enum OrdenesCompraResponseShape
{
    Get,
    Create,
    Update,
    Delete
}

public sealed record OrdenesCompraOperationDefinition(
    string Operation,
    string StoredProcedure,
    IReadOnlyList<string> Parameters,
    OrdenesCompraResponseShape Shape);

/// <summary>Catálogo D6.4 — Órdenes de compra (Get + Create + Update + Delete MVP).</summary>
public static class OrdenesCompraCatalog
{
    private static readonly Dictionary<string, OrdenesCompraOperationDefinition> ByOperation =
        new(StringComparer.Ordinal)
        {
            ["OrdenesCompra.Get"] = new(
                "OrdenesCompra.Get",
                "dbo.PAQ_OrdenesCompra_Get",
                new[] { "talon_oc", "n_orden_co" },
                OrdenesCompraResponseShape.Get),
            ["OrdenesCompra.Create"] = new(
                "OrdenesCompra.Create",
                "(orchestrated)",
                new[]
                {
                    "talon_oc", "n_orden_co", "cod_provee", "cod_lista", "mon_cte", "cotiz",
                    "cond_compr", "cod_compra", "nro_sucurs", "porc_bonif", "observaciones",
                    "fecha_emisio", "fecha_vigenc", "fecha_gener", "usuario_ingreso", "cod_sucurs",
                    "leyendas_json", "renglones_json"
                },
                OrdenesCompraResponseShape.Create),
            ["OrdenesCompra.Update"] = new(
                "OrdenesCompra.Update",
                "(orchestrated)",
                new[]
                {
                    "talon_oc", "n_orden_co", "row_version", "observaciones", "fecha_vigenc",
                    "porc_bonif", "renglones_json"
                },
                OrdenesCompraResponseShape.Update),
            ["OrdenesCompra.Delete"] = new(
                "OrdenesCompra.Delete",
                "(orchestrated)",
                new[] { "talon_oc", "n_orden_co", "row_version", "usuario_anulacion" },
                OrdenesCompraResponseShape.Delete),
        };

    public static IReadOnlyCollection<string> Operations => ByOperation.Keys;

    public static bool TryGet(string operation, out OrdenesCompraOperationDefinition definition) =>
        ByOperation.TryGetValue(operation, out definition!);
}
