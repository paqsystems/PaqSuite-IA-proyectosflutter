using PaqAgentInstaller.Models;

namespace PaqAgentInstaller.Services;

public static class GatewayReachabilityTester
{
    public static async Task<ConnectionTestResult> TestAsync(string gatewayUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate((gatewayUrl ?? "").Trim(), UriKind.Absolute, out var hubUri))
        {
            return new ConnectionTestResult { Ok = false, Message = "Gateway URL inválida." };
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // negotiate: respuesta 4xx/200 cuenta como hub alcanzable (TLS/DNS/443 OK).
            var negotiateUri = new Uri(hubUri, hubUri.AbsolutePath.TrimEnd('/') + "/negotiate?negotiateVersion=1");
            using var request = new HttpRequestMessage(HttpMethod.Post, negotiateUri);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if ((int)response.StatusCode is >= 200 and < 500)
            {
                return new ConnectionTestResult
                {
                    Ok = true,
                    Message = $"Gateway alcanzable (HTTP {(int)response.StatusCode})."
                };
            }

            return new ConnectionTestResult
            {
                Ok = false,
                Message = $"Gateway respondió HTTP {(int)response.StatusCode}. Verifique DNS/TLS/443 saliente."
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult
            {
                Ok = false,
                Message = "No se pudo alcanzar el Gateway (DNS/TLS/443): " + ex.Message
            };
        }
    }
}
