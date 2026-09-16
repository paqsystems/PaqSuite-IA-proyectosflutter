namespace PaqAgent.AsientosContables;

public enum AsientosContablesResponseShape
{
    Get,
    Create,
    Update,
    Delete
}

public sealed record AsientosContablesOperationDefinition(
    string Operation,
    string StoredProcedure,
    IReadOnlyList<string> Parameters,
    AsientosContablesResponseShape Shape);

/// <summary>Catálogo D6.7 — Asientos contables (Get/Create/Update/Delete).</summary>
public static class AsientosContablesCatalog
{
    private static readonly Dictionary<string, AsientosContablesOperationDefinition> ByOperation =
        new(StringComparer.Ordinal)
        {
            ["AsientosContables.Get"] = new(
                "AsientosContables.Get",
                "dbo.PAQ_AsientosContables_Get",
                new[] { "nro_interno_analitico" },
                AsientosContablesResponseShape.Get),
            ["AsientosContables.Create"] = new(
                "AsientosContables.Create",
                "(orchestrated)",
                new[]
                {
                    "nro_interno_analitico", "cod_tipo_asiento", "fecha", "cod_moneda",
                    "nro_asiento", "leyenda", "observaciones", "usuario", "renglones_json"
                },
                AsientosContablesResponseShape.Create),
            ["AsientosContables.Update"] = new(
                "AsientosContables.Update",
                "(orchestrated)",
                new[]
                {
                    "nro_interno_analitico", "row_version", "fecha", "leyenda", "observaciones",
                    "cod_moneda", "estado_asiento_analitico", "nro_asiento", "usuario", "renglones_json"
                },
                AsientosContablesResponseShape.Update),
            ["AsientosContables.Delete"] = new(
                "AsientosContables.Delete",
                "(orchestrated)",
                new[] { "nro_interno_analitico", "row_version", "usuario" },
                AsientosContablesResponseShape.Delete),
        };

    public static IReadOnlyCollection<string> Operations => ByOperation.Keys;

    public static bool TryGet(string operation, out AsientosContablesOperationDefinition definition) =>
        ByOperation.TryGetValue(operation, out definition!);
}
