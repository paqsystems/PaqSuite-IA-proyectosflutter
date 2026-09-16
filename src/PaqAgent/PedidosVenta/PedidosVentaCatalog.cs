namespace PaqAgent.PedidosVenta;

public enum PedidosVentaResponseShape
{
    Get,
    Create,
    Update,
    Delete
}

public sealed record PedidosVentaOperationDefinition(
    string Operation,
    string StoredProcedure,
    IReadOnlyList<string> Parameters,
    PedidosVentaResponseShape Shape);

/// <summary>Catálogo D6 — Pedidos venta (Get + Create + Update + Delete MVP).</summary>
public static class PedidosVentaCatalog
{
    private static readonly Dictionary<string, PedidosVentaOperationDefinition> ByOperation =
        new(StringComparer.Ordinal)
        {
            ["PedidosVenta.Get"] = new(
                "PedidosVenta.Get",
                "dbo.PAQ_PedidosVenta_Get",
                new[] { "talon_ped", "nro_pedido" },
                PedidosVentaResponseShape.Get),
            ["PedidosVenta.Create"] = new(
                "PedidosVenta.Create",
                "(orchestrated)",
                new[]
                {
                    "talon_ped", "nro_pedido", "id_externo", "cod_client", "fecha_pedi", "fecha_entr",
                    "mon_cte", "cotiz", "n_lista", "cond_vta", "cod_vended", "cod_transp", "cod_sucurs",
                    "observaciones", "usuario_ingreso", "renglones_json"
                },
                PedidosVentaResponseShape.Create),
            ["PedidosVenta.Update"] = new(
                "PedidosVenta.Update",
                "(orchestrated)",
                new[]
                {
                    "talon_ped", "nro_pedido", "row_version", "observaciones", "fecha_entr", "estado",
                    "renglones_json"
                },
                PedidosVentaResponseShape.Update),
            ["PedidosVenta.Delete"] = new(
                "PedidosVenta.Delete",
                "(orchestrated)",
                new[] { "talon_ped", "nro_pedido", "row_version" },
                PedidosVentaResponseShape.Delete),
        };

    public static IReadOnlyCollection<string> Operations => ByOperation.Keys;

    public static bool TryGet(string operation, out PedidosVentaOperationDefinition definition) =>
        ByOperation.TryGetValue(operation, out definition!);
}
