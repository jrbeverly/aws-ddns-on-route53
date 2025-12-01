using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DdnsClient;

public static class Reporter
{
    // Public IP is determined by querying checkip.amazonaws.com, which returns a bare
    // IP address as plain text. Tradeoff: no JSON parsing, one round-trip, but requires
    // outbound HTTP and trusts the remote service; replace with a local interface query
    // if internet egress is unavailable.
    public const string IpEchoUrl = "https://checkip.amazonaws.com";

    public static async Task<string> GetPublicIpAsync(HttpClient http)
    {
        var text = await http.GetStringAsync(IpEchoUrl);
        return text.Trim();
    }

    public static async Task<string> ReportAsync(HttpClient http, string hostname, string ip, string endpointUrl)
    {
        var payload = JsonSerializer.Serialize(new { hostname, ip });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await http.PostAsync(endpointUrl, content);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Service returned {(int)response.StatusCode}: {body}");
        }
        var responseBody = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ReportResponse>(responseBody)
            ?? throw new InvalidOperationException("Empty response from service");
        return result.Status;
    }
}

file record ReportResponse([property: JsonPropertyName("status")] string Status);
