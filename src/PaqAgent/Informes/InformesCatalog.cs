namespace PaqAgent.Informes;

public sealed record InformesOperationDefinition(
    string Operation,
    string StoredProcedure,
    IReadOnlyList<string> Parameters);

/// <summary>Catálogo D2 — ops InformesGestion pedidas por Tango (TR-043 / appsettings legado).</summary>
public static class InformesCatalog
{
    private static readonly Dictionary<string, InformesOperationDefinition> ByOperation =
        new(StringComparer.Ordinal)
        {
            ["informes.ventas-listado-saldos"] = Def(
                "informes.ventas-listado-saldos",
                "dbo.PAQ_Ventas_ListadoSaldos",
                "fecha_referencia", "ignorar_saldo_cero", "cod_client", "vendedor", "zona", "rubro",
                "provincia", "empresa", "page", "page_size"),
            ["informes.ventas-resumen-cuenta"] = Def(
                "informes.ventas-resumen-cuenta",
                "dbo.PAQ_Ventas_ResumenCuenta",
                "fecha_desde", "fecha_hasta", "cod_client", "empresa", "page", "page_size"),
            ["informes.compras-resumen-cuenta"] = Def(
                "informes.compras-resumen-cuenta",
                "dbo.PAQ_Compras_ResumenCuenta",
                "fecha_desde", "fecha_hasta", "cod_provee", "empresa", "page", "page_size"),
            ["informes.ventas-iva-ventas"] = Def(
                "informes.ventas-iva-ventas",
                "dbo.PAQ_Ventas_IvaVentas",
                "fecha_desde", "fecha_hasta", "empresa", "page", "page_size"),
            ["informes.compras-iva-compras"] = Def(
                "informes.compras-iva-compras",
                "dbo.PAQ_Compras_IvaCompras",
                "fecha_desde", "fecha_hasta", "criterio_fecha", "empresa", "page", "page_size"),
            ["informes.ventas-composicion-saldos"] = Def(
                "informes.ventas-composicion-saldos",
                "dbo.PAQ_Ventas_ComposicionSaldos",
                "fecha_referencia", "cod_client", "empresa", "sort", "sort_dir", "page", "page_size"),
            ["informes.compras-listado-saldos"] = Def(
                "informes.compras-listado-saldos",
                "dbo.PAQ_Compras_ListadoSaldos",
                "fecha_referencia", "criterio_fecha", "ignorar_saldo_cero", "cod_provee", "comprador",
                "provincia", "empresa", "page", "page_size"),
            ["informes.compras-composicion-saldos"] = Def(
                "informes.compras-composicion-saldos",
                "dbo.PAQ_Compras_ComposicionSaldos",
                "fecha_referencia", "cod_provee", "empresa", "sort", "sort_dir", "page", "page_size"),
            ["informes.tesoreria-listado-saldos"] = Def(
                "informes.tesoreria-listado-saldos",
                "dbo.PAQ_Tesoreria_ListadoSaldos",
                "fecha_referencia", "ignorar_saldo_cero", "cod_cuen", "tipo_cuenta", "empresa", "page",
                "page_size"),
            ["informes.tesoreria-mayor-cuenta"] = Def(
                "informes.tesoreria-mayor-cuenta",
                "dbo.PAQ_Tesoreria_MayorCuenta",
                "fecha_desde", "fecha_hasta", "cod_cuen", "empresa", "page", "page_size"),
            ["informes.stock-listado-saldos"] = Def(
                "informes.stock-listado-saldos",
                "dbo.PAQ_Stock_ListadoSaldos",
                "fecha_referencia", "ignorar_saldo_cero", "cod_articu", "cod_deposi", "empresa", "page",
                "page_size"),
            ["informes.stock-partida-listado-saldos"] = Def(
                "informes.stock-partida-listado-saldos",
                "dbo.PAQ_Stock_PartidaListadoSaldos",
                "fecha_referencia", "ignorar_saldo_cero", "cod_articu", "cod_deposi", "nro_parti", "empresa",
                "page", "page_size"),
            ["informes.stock-movimiento"] = Def(
                "informes.stock-movimiento",
                "dbo.PAQ_Stock_Movimiento",
                "fecha_desde", "fecha_hasta", "cod_articu", "cod_deposi", "tipo_mov", "empresa", "page",
                "page_size"),
            ["informes.stock-partida-movimiento"] = Def(
                "informes.stock-partida-movimiento",
                "dbo.PAQ_Stock_PartidaMovimiento",
                "fecha_desde", "fecha_hasta", "cod_articu", "cod_deposi", "tipo_mov", "nro_parti", "empresa",
                "page", "page_size"),
            ["informes.stock-inventario-valorizado"] = Def(
                "informes.stock-inventario-valorizado",
                "dbo.PAQ_Stock_InventarioValorizado",
                "fecha_referencia", "ignorar_saldo_cero", "cod_articu", "cod_deposi", "empresa", "page",
                "page_size"),
            ["informes.stock-partida-inventario-valorizado"] = Def(
                "informes.stock-partida-inventario-valorizado",
                "dbo.PAQ_Stock_PartidaInventarioValorizado",
                "fecha_referencia", "ignorar_saldo_cero", "cod_articu", "cod_deposi", "nro_parti", "empresa",
                "page", "page_size"),
        };

    public static IReadOnlyCollection<string> Operations => ByOperation.Keys;

    public static bool TryGet(string operation, out InformesOperationDefinition definition) =>
        ByOperation.TryGetValue(operation, out definition!);

    private static InformesOperationDefinition Def(
        string operation,
        string storedProcedure,
        params string[] parameters) =>
        new(operation, storedProcedure, parameters);
}
