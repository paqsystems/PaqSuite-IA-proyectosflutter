namespace PaqAgent.Acopios;

public enum AcopiosResponseShape
{
    Parametros,
    FacturasList,
    PedidosList,
    SaldosList,
    ListaPrecios,
    FacturaGet,
    FacturaDetalleGet,
    PedidoDetalleGet,
    PedidoBatchGet,
    ResultCodeWithId,
    ResultCodeOnly
}

public sealed record AcopiosOperationDefinition(
    string Operation,
    string StoredProcedure,
    IReadOnlyList<string> Parameters,
    AcopiosResponseShape Shape,
    bool UseCompanyDatabaseOverride = true,
    bool PassDatabaseAsSpParam = false);

public static class AcopiosCatalog
{
    private static readonly Dictionary<string, AcopiosOperationDefinition> ByOperation =
        new(StringComparer.Ordinal)
        {
            ["Acopios.Parametros.List"] = Def(
                "Acopios.Parametros.List", "dbo.PAQ_Acopios_ParametrosList",
                AcopiosResponseShape.Parametros),
            ["Acopios.FacturasElegibles.List"] = Def(
                "Acopios.FacturasElegibles.List", "dbo.PAQ_Acopios_FacturasElegiblesList",
                AcopiosResponseShape.FacturasList,
                "prefijo_articulo", "cliente", "fecha_desde", "fecha_hasta", "dictionary_db", "grupo_id"),
            ["Acopios.PedidosDisponibles.List"] = Def(
                "Acopios.PedidosDisponibles.List", "dbo.PAQ_Acopios_PedidosDisponiblesList",
                AcopiosResponseShape.PedidosList,
                "cliente", "fecha_desde", "fecha_hasta", "dictionary_db", "grupo_id"),
            ["Acopios.FacturaAcopio.Get"] = Def(
                "Acopios.FacturaAcopio.Get", "dbo.PAQ_Acopios_FacturaAcopioGet",
                AcopiosResponseShape.FacturaGet, "id"),
            ["Acopios.FacturaAcopio.Create"] = Def(
                "Acopios.FacturaAcopio.Create", "dbo.PAQ_Acopios_FacturaAcopioCreate",
                AcopiosResponseShape.ResultCodeWithId,
                "t_comp", "n_comp", "cod_client", "fecha_vigencia", "lista_precios_id", "descuento",
                "importe_neto", "importe_impuestos", "importe_total", "fecha_umo_acopio"),
            ["Acopios.FacturaAcopio.Update"] = Def(
                "Acopios.FacturaAcopio.Update", "dbo.PAQ_Acopios_FacturaAcopioUpdate",
                AcopiosResponseShape.ResultCodeWithId,
                "id", "lista_precios_id", "fecha_vigencia", "descuento", "fecha_umo_acopio"),
            ["Acopios.Saldos.List"] = Def(
                "Acopios.Saldos.List", "dbo.PAQ_Acopios_SaldosList",
                AcopiosResponseShape.SaldosList,
                "cliente", "fecha_desde", "fecha_hasta", "comprobante"),
            ["Acopios.FacturaAcopio.Close"] = Def(
                "Acopios.FacturaAcopio.Close", "dbo.PAQ_Acopios_FacturaAcopioClose",
                AcopiosResponseShape.ResultCodeOnly, "id", "fecha_umo_acopio"),
            ["Acopios.Asociacion.Create"] = Def(
                "Acopios.Asociacion.Create", "dbo.PAQ_Acopios_AsociacionCreate",
                AcopiosResponseShape.ResultCodeWithId,
                "t_comp", "n_comp", "talon_ped", "nro_pedido", "cod_client_ped", "dictionary_db",
                "grupo_id", "renglones_json", "saldo_disponible"),
            ["Acopios.Asociacion.Delete"] = Def(
                "Acopios.Asociacion.Delete", "dbo.PAQ_Acopios_AsociacionDelete",
                AcopiosResponseShape.ResultCodeOnly, "id"),
            ["Acopios.FacturaDetalle.Get"] = Def(
                "Acopios.FacturaDetalle.Get", "dbo.PAQ_Acopios_FacturaDetalleGet",
                AcopiosResponseShape.FacturaDetalleGet,
                "t_comp", "n_comp", "prefijo_articulo", "dictionary_db", "grupo_id", "empresa_bd"),
            ["Acopios.PedidoDetalle.Get"] = Def(
                "Acopios.PedidoDetalle.Get", "dbo.PAQ_Acopios_PedidoDetalleGet",
                AcopiosResponseShape.PedidoDetalleGet,
                "talon_ped", "nro_pedido", "dictionary_db", "grupo_id", "empresa_id", "empresa_bd"),
            ["Acopios.PedidoDetallesBatch.Get"] = new(
                "Acopios.PedidoDetallesBatch.Get",
                "dbo.PAQ_Acopios_PedidoDetallesBatchGet",
                new[] { "pedidos_xml", "dictionary_db", "grupo_id" },
                AcopiosResponseShape.PedidoBatchGet,
                UseCompanyDatabaseOverride: false),
            ["Acopios.ListaPrecios.Opciones"] = new(
                "Acopios.ListaPrecios.Opciones",
                "dbo.PAQ_Acopios_ListaPreciosOpciones",
                Array.Empty<string>(),
                AcopiosResponseShape.ListaPrecios,
                UseCompanyDatabaseOverride: true,
                PassDatabaseAsSpParam: true),
        };

    public static IReadOnlyCollection<string> Operations => ByOperation.Keys;

    public static bool TryGet(string operation, out AcopiosOperationDefinition definition) =>
        ByOperation.TryGetValue(operation, out definition!);

    private static AcopiosOperationDefinition Def(
        string operation,
        string sp,
        AcopiosResponseShape shape,
        params string[] parameters) =>
        new(operation, sp, parameters, shape);
}
