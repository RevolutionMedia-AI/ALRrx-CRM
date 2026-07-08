using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ALRrx.Infrastructure.Slice;

public sealed class SliceInternalClient : ISliceInternalClient
{
    private readonly HttpClient _http;
    private readonly ILogger<SliceInternalClient> _logger;
    private readonly string _baseUrl;
    private readonly string? _internalToken;

    public SliceInternalClient(HttpClient http, IConfiguration config, ILogger<SliceInternalClient> logger)
    {
        _http = http;
        _logger = logger;

        // Default to localhost:5001 (the slice port inside the combined container).
        // Override via Alrrx:Slice:BaseUrl for tests / split deployments.
        _baseUrl = (config["Alrrx:Slice:BaseUrl"] ?? "http://localhost:5001").TrimEnd('/');
        _internalToken = config["Alrrx:Slice:InternalToken"];

        if (!string.IsNullOrEmpty(_internalToken))
        {
            _http.DefaultRequestHeaders.Remove("X-Internal-Token");
            _http.DefaultRequestHeaders.Add("X-Internal-Token", _internalToken);
        }
        _http.Timeout = TimeSpan.FromSeconds(5);
    }

    public async Task AddAllowedEmailAsync(string email, CancellationToken ct = default)
    {
        await PostAsync("allowed-emails", new { email }, ct);
        _logger.LogInformation("Slice allow list: added email {Email}", email);
    }

    public async Task RemoveAllowedEmailAsync(string email, CancellationToken ct = default)
    {
        await DeleteAsync($"allowed-emails/{Uri.EscapeDataString(email)}", ct);
        _logger.LogInformation("Slice allow list: removed email {Email}", email);
    }

    private async Task PostAsync(string path, object body, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_internalToken))
        {
            _logger.LogWarning("Skipping slice internal call: Alrrx:Slice:InternalToken is not configured");
            return;
        }
        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{_baseUrl}/api/internal/{path}", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body_text = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Slice internal POST /{Path} returned {Status}: {Body}", path, response.StatusCode, body_text);
        }
    }

    private async Task DeleteAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_internalToken))
        {
            _logger.LogWarning("Skipping slice internal call: Alrrx:Slice:InternalToken is not configured");
            return;
        }
        using var response = await _http.DeleteAsync($"{_baseUrl}/api/internal/{path}", ct);
        if (!response.IsSuccessStatusCode)
        {
            var body_text = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Slice internal DELETE /{Path} returned {Status}: {Body}", path, response.StatusCode, body_text);
        }
    }
}
