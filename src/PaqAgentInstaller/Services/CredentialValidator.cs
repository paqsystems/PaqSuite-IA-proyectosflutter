using PaqAgentInstaller.Models;

namespace PaqAgentInstaller.Services;

public static class CredentialValidator
{
    public static IReadOnlyList<string> ValidateRequired(InstallerSession session)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(session.AgentId))
        {
            errors.Add("AgentId es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(session.ClientId))
        {
            errors.Add("ClientId es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(session.AgentToken))
        {
            errors.Add("AgentToken es obligatorio (sin valor por defecto).");
        }

        if (string.IsNullOrWhiteSpace(session.GatewayUrl))
        {
            errors.Add("Gateway URL es obligatoria.");
        }
        else if (!Uri.TryCreate(session.GatewayUrl.Trim(), UriKind.Absolute, out var uri)
                 || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("Gateway URL debe ser http(s) absoluta.");
        }

        if (string.IsNullOrWhiteSpace(session.SqlServer))
        {
            errors.Add("Servidor SQL es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(session.SqlDatabase))
        {
            errors.Add("Base diccionario es obligatoria.");
        }

        if (string.IsNullOrWhiteSpace(session.SqlUser))
        {
            errors.Add("Usuario SQL es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(session.SqlPassword))
        {
            errors.Add("Contraseña SQL es obligatoria.");
        }

        if (session.SqlPort is <= 0)
        {
            errors.Add("Puerto SQL, si se informa, debe ser mayor que 0.");
        }

        return errors;
    }

    public static bool CanProceedPastRuntime(InstallerSession session) =>
        session.RuntimePresent || session.RuntimeAckContinue;
}
