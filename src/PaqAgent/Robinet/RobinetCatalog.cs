using PaqAgent.Informes;

namespace PaqAgent.Robinet;

/// <summary>Catálogo D4 — tableros Robinet (mismo shape dual-RS que Informes).</summary>
public static class RobinetCatalog
{
    private static readonly Dictionary<string, InformesOperationDefinition> ByOperation =
        new(StringComparer.Ordinal)
        {
            ["robinet.deudas"] = Def(
                "robinet.deudas",
                "dbo.PAQ_Robinet_Deudas",
                "cod_client", "prefijo_acopio", "empresa", "page", "page_size"),
            ["robinet.pedidos"] = Def(
                "robinet.pedidos",
                "dbo.PAQ_Robinet_Pedidos",
                "fecha_desde", "fecha_hasta", "cod_client", "talon_ped", "cod_articu",
                "empresa", "page", "page_size"),
            ["robinet.cobranzas"] = Def(
                "robinet.cobranzas",
                "dbo.PAQ_Robinet_Cobranzas",
                "fecha_desde", "fecha_hasta", "prefijo_acopio", "cod_client", "vendedor", "zona",
                "rubro", "transporte", "provincia", "condicion_venta", "empresa", "page", "page_size"),
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
