using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

public class ESXiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _username;
    private readonly string _password;
    private string _sessionId;
    private bool _disposed;
    private readonly object _lock = new object();
    private const int MaxRetries = 3;
    private const int RetryDelay = 1000; // milliseconds

    private readonly Dictionary<string, string[]> _validValues = new Dictionary<string, string[]>
    {
        ["guestOs"] = new[] { "windows9_64Guest", "windows8_64Guest", "rhel8_64Guest", "ubuntu64Guest", "other" },
        ["powerState"] = new[] { "powered_on", "powered_off", "suspended" }
    };

    public ESXiClient(string baseUrl, string username, string password)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _username = username;
        _password = password;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task InitializeAsync()
    {
        try
        {
            await AuthenticateAsync();
            await ValidateConnectionAsync();
        }
        catch (Exception ex)
        {
            throw new Exception($"ESXi initialization failed: {ex.Message}", ex);
        }
    }

    private async Task AuthenticateAsync()
    {
        var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authString);

        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.PostAsync("/rest/com/vmware/cis/session", new StringContent("")), false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"ESXi authentication failed: {response.StatusCode}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);

        if (jsonResponse.TryGetProperty("value", out var sessionId))
        {
            _sessionId = sessionId.GetString();
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("vmware-api-session-id", _sessionId);
        }
        else
        {
            throw new Exception("Failed to retrieve ESXi session ID");
        }
    }

    private async Task ValidateConnectionAsync()
    {
        try
        {
            await GetSystemInfoAsync();
        }
        catch (Exception ex)
        {
            throw new Exception("Failed to validate ESXi connection", ex);
        }
    }

    private async Task<HttpResponseMessage> SendRequestWithRetryAsync(
        Func<Task<HttpResponseMessage>> request,
        bool shouldReauthenticate = true,
        int retryCount = 0)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ESXiClient));

        try
        {
            var response = await request();

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && shouldReauthenticate && retryCount < MaxRetries)
            {
                await AuthenticateAsync();
                return await SendRequestWithRetryAsync(request, true, retryCount + 1);
            }

            var content = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"ESXi request failed with status {response.StatusCode}: {content}");
        }
        catch (Exception ex) when (ex is not ObjectDisposedException && retryCount < MaxRetries)
        {
            await Task.Delay(RetryDelay * (retryCount + 1));
            return await SendRequestWithRetryAsync(request, shouldReauthenticate, retryCount + 1);
        }
    }

    private async Task<T> DeserializeResponseAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"ESXi request failed: {response.StatusCode}\nContent: {content}");
        }

        try
        {
            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
            if (result.TryGetValue("value", out var value))
            {
                return JsonSerializer.Deserialize<T>(value.ToString());
            }
            return default;
        }
        catch (JsonException ex)
        {
            throw new Exception($"Failed to parse ESXi response: {content}", ex);
        }
    }

    public async Task<Dictionary<string, object>> GetSystemInfoAsync()
    {
        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.GetAsync("/rest/appliance/system/version"));
        return await DeserializeResponseAsync<Dictionary<string, object>>(response);
    }

    public async Task<List<Dictionary<string, object>>> GetVirtualMachinesAsync()
    {
        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.GetAsync("/rest/vcenter/vm"));
        return await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);
    }

    public async Task<Dictionary<string, object>> GetVirtualMachineAsync(string vmId)
    {
        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.GetAsync($"/rest/vcenter/vm/{vmId}"));
        return await DeserializeResponseAsync<Dictionary<string, object>>(response);
    }

    public async Task<bool> PowerOperationAsync(string vmId, string operation)
    {
        if (!_validValues["powerState"].Contains(operation))
        {
            throw new ArgumentException($"Invalid power operation: {operation}");
        }

        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.PostAsync($"/rest/vcenter/vm/{vmId}/power/{operation}", null));
        return response.IsSuccessStatusCode;
    }

    public async Task<Dictionary<string, object>> GetVmHardwareAsync(string vmId)
    {
        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.GetAsync($"/rest/vcenter/vm/{vmId}/hardware"));
        return await DeserializeResponseAsync<Dictionary<string, object>>(response);
    }

    public async Task<bool> UpdateVmHardwareAsync(string vmId, Dictionary<string, object> spec)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { spec }),
            Encoding.UTF8,
            "application/json"
        );

        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.PatchAsync($"/rest/vcenter/vm/{vmId}/hardware", content));
        return response.IsSuccessStatusCode;
    }

    public async Task<List<Dictionary<string, object>>> GetDatastoresAsync()
    {
        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.GetAsync("/rest/vcenter/datastore"));
        return await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);
    }

    public async Task<List<Dictionary<string, object>>> GetNetworksAsync()
    {
        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.GetAsync("/rest/vcenter/network"));
        return await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);
    }

    public async Task<string> CreateVmAsync(Dictionary<string, object> createSpec)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { spec = createSpec }),
            Encoding.UTF8,
            "application/json"
        );

        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.PostAsync("/rest/vcenter/vm", content));

        var result = await DeserializeResponseAsync<Dictionary<string, object>>(response);
        return result["id"].ToString();
    }

    public async Task<bool> DeleteVmAsync(string vmId)
    {
        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.DeleteAsync($"/rest/vcenter/vm/{vmId}"));
        return response.IsSuccessStatusCode;
    }

    public async Task<List<Dictionary<string, object>>> GetHostsAsync()
    {
        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.GetAsync("/rest/vcenter/host"));
        return await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);
    }

    public async Task<Dictionary<string, object>> GetHostAsync(string hostId)
    {
        var response = await SendRequestWithRetryAsync(() =>
            _httpClient.GetAsync($"/rest/vcenter/host/{hostId}"));
        return await DeserializeResponseAsync<Dictionary<string, object>>(response);
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_sessionId != null)
                {
                    _httpClient.DeleteAsync("/rest/com/vmware/cis/session").GetAwaiter().GetResult();
                }
            }
            catch
            {
                // Best effort cleanup
            }
            finally
            {
                _httpClient?.Dispose();
            }
        }
    }
}