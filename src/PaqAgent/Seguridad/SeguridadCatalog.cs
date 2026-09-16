namespace PaqAgent.Seguridad;

public sealed record SeguridadOperationDefinition(
    string Operation,
    string StoredProcedure,
    IReadOnlyList<string> Parameters);

public static class SeguridadCatalog
{
    private static readonly Dictionary<string, SeguridadOperationDefinition> ByOperation =
        new(StringComparer.Ordinal)
        {
            ["seguridad.roles.list"] = Def(
                "seguridad.roles.list",
                "dbo.PAQ_Seguridad_Roles_List"),
            ["seguridad.users.list"] = Def(
                "seguridad.users.list",
                "dbo.PAQ_Seguridad_Users_List",
                "codigo", "nombre", "email", "activo", "inhabilitado"),
            ["seguridad.empresas.list"] = Def(
                "seguridad.empresas.list",
                "dbo.PAQ_Seguridad_Empresas_List"),
            ["seguridad.permisos.list"] = Def(
                "seguridad.permisos.list",
                "dbo.PAQ_Seguridad_Permisos_List",
                "id_usuario", "id_empresa", "id_rol"),
            ["seguridad.grupos-empresarios.list"] = Def(
                "seguridad.grupos-empresarios.list",
                "dbo.PAQ_Seguridad_GruposEmpresarios_List"),
        };

    public static IReadOnlyCollection<string> Operations => ByOperation.Keys;

    public static bool TryGet(string operation, out SeguridadOperationDefinition definition) =>
        ByOperation.TryGetValue(operation, out definition!);

    private static SeguridadOperationDefinition Def(
        string operation,
        string storedProcedure,
        params string[] parameters) =>
        new(operation, storedProcedure, parameters);
}
