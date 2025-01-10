using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using ProxmoxApiHelper.Helpers;

namespace ProxmoxApiHelper
{
    /// <summary>
    /// Provides methods to interact with the Proxmox API.
    /// </summary>
    public class ProxmoxClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        public string ApiUrl { get; }
        private readonly string _username;
        private readonly string _password;
        private readonly string _realm;
        private string _authToken;
        private string _csrfToken;
        private bool _disposed;
        private readonly object _lock = new object();
        private const int MaxRetries = 3;
        private const int RetryDelay = 1000; // milliseconds

        private readonly Dictionary<string, string[]> _validValues = new Dictionary<string, string[]>
        {
            ["ostype"] = new[] { "other", "wxp", "w2k", "w2k3", "w2k8", "wvista", "win7", "win8", "win10", "win11", "l24", "l26", "solaris" },
            ["bios"] = new[] { "seabios", "ovmf" }
        };

        /// <summary>
        /// Initializes a new instance of the ProxmoxClient class.
        /// </summary>
        public ProxmoxClient(string apiUrl, string username, string password, string realm = "pam")
        {
            ApiUrl = apiUrl.TrimEnd('/');
            _username = username;
            _password = password;
            _realm = realm;

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };

            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(ApiUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        /// <summary>
        /// Initializes the client by authenticating and fetching initial data.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                await AuthenticateAsync();
                await FetchAndSaveAllDataAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Initialization failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Authenticates with the Proxmox API and retrieves authentication tokens.
        /// </summary>
        /// 

        private async Task AuthenticateAsync()
        {
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", $"{_username}@{_realm}"),
                new KeyValuePair<string, string>("password", _password)
            });

            using var response = await SendRequestWithRetryAsync(() => _httpClient.PostAsync("/api2/json/access/ticket", content), false);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Authentication failed: {responseBody}");
            }

            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);

            if (!jsonResponse.TryGetProperty("data", out var data))
            {
                throw new Exception($"Invalid response format: {responseBody}");
            }

            _authToken = data.GetProperty("ticket").GetString();
            _csrfToken = data.GetProperty("CSRFPreventionToken").GetString();

            UpdateHttpClientHeaders();
        }

        private void UpdateHttpClientHeaders()
        {
            lock (_lock)
            {
                if (_disposed) return;

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                _httpClient.DefaultRequestHeaders.Add("Cookie", $"PVEAuthCookie={_authToken}");
                _httpClient.DefaultRequestHeaders.Add("CSRFPreventionToken", _csrfToken);
            }
        }

        private async Task FetchAndSaveAllDataAsync()
        {
            var allData = new Dictionary<string, object>
            {
                ["nodes"] = await GetNodesAsync(),
                ["clusters"] = await GetClustersAsync(),
                ["vms"] = await GetAllVmsAsync(),
                ["storage"] = await GetStorageAsync(),
                ["networks"] = await GetNetworksAsync(),
                ["users"] = await GetUsersAsync(),
                ["groups"] = await GetGroupsAsync()
            };

            // In a real implementation, you might want to save this data to a file or database
            // For this example, we'll just log it
            Console.WriteLine($"Fetched and saved data: {JsonSerializer.Serialize(allData)}");
        }

        public async Task<Result> CreateVMAsync(
     string node,
     int vmid,
     string storage,
          string disktype = null,
                           // Required storage name
     int diskSize = 0,    // Size in GB
     bool? acpi = null,
     string affinity = null,
     string agent = null,
     string amd_sev = null,
     string arch = null,
     string archive = null,
     string args = null,
     string audio0 = null,
     bool? autostart = null,
     string? balloon = null,
     string bios = null,
     string boot = null,
     string bootdisk = null,
     int? bwlimit = null,
     string cicustom = null,
     string cipassword = null,
     string citype = null,
     bool? ciupgrade = null,
     string ciuser = null,
     int? cores = null,
     string cpu = null,
     float? cpulimit = null,
     int? cpuunits = null,
     string description = null,
     string efidisk0 = null,
     bool? force = null,
     bool? freeze = null,
     string hookscript = null,
     IDictionary<int, string> hostpciN = null,
     string hotplug = null,
     string hugepages = null,
     IDictionary<int, string> ideN = null,
     string import_working_storage = null,
     IDictionary<int, string> ipconfigN = null,
     string ivshmem = null,
     bool? keephugepages = null,
     string keyboard = null,
     bool? kvm = null,
     bool? live_restore = null,
     bool? localtime = null,
     string lock_ = null,
     string machine = null,
     string memory = null,
     float? migrate_downtime = null,
     int? migrate_speed = null,
     string name = null,
     string nameserver = null,
     IDictionary<int, string> netN = null,
     bool? numa = null,
     IDictionary<int, string> numaN = null,
     bool? onboot = null,
     string ostype = null,
     IDictionary<int, string> parallelN = null,
     string pool = null,
     bool? protection = null,
     bool? reboot = null,
     string rng0 = null,
     IDictionary<int, string> sataN = null,
     IDictionary<int, string> scsiN = null,
     string scsihw = null,
     string searchdomain = null,
     IDictionary<int, string> serialN = null,
     int? shares = null,
     string smbios1 = null,
     int? smp = null,
     int? sockets = null,
     string spice_enhancements = null,
     string sshkeys = null,
     bool? start = null,
     string startdate = null,
     string storage_ = null, // Added to avoid naming conflict
     bool? tablet = null,
     string tags = null,
     bool? tdf = null,
     bool? template = null,
     string tpmstate0 = null,
     bool? unique = null,
     IDictionary<int, string> unusedN = null,
     IDictionary<int, string> usbN = null,
     int? vcpus = null,
     string vga = null,
     IDictionary<int, string> virtioN = null,
     string vmgenid = null,
     string vmstatestorage = null,
     string watchdog = null,
     string iso = null,
     string net0 = null,
     string ipconfig0 = null
     )
        {
            var parameters = new Dictionary<string, object>
            {
                ["vmid"] = vmid,
                ["acpi"] = acpi,
                ["affinity"] = affinity,
                ["agent"] = agent,
                ["amd-sev"] = amd_sev,
                ["arch"] = arch,
                ["archive"] = archive,
                ["args"] = args,
                ["audio0"] = audio0,
                ["autostart"] = autostart,
                ["balloon"] = balloon,
                ["bios"] = bios,
                ["boot"] = boot,
                ["bootdisk"] = bootdisk,
                ["bwlimit"] = bwlimit,
                ["cicustom"] = cicustom,
                ["cipassword"] = cipassword,
                ["citype"] = citype,
                ["ciupgrade"] = ciupgrade,
                ["ciuser"] = ciuser,
                ["cores"] = cores,
                ["cpu"] = cpu,
                ["cpulimit"] = cpulimit,
                ["cpuunits"] = cpuunits,
                ["description"] = description,
                ["efidisk0"] = efidisk0,
                ["force"] = force,
                ["freeze"] = freeze,
                ["hookscript"] = hookscript,
                ["hotplug"] = hotplug,
                ["hugepages"] = hugepages,
                ["import-working-storage"] = import_working_storage,
                ["ivshmem"] = ivshmem,
                ["keephugepages"] = keephugepages,
                ["keyboard"] = keyboard,
                ["kvm"] = kvm,
                ["live-restore"] = live_restore,
                ["localtime"] = localtime,
                ["lock"] = lock_,
                ["machine"] = machine,
                ["memory"] = memory,
                ["migrate_downtime"] = migrate_downtime,
                ["migrate_speed"] = migrate_speed,
                ["name"] = name,
                ["nameserver"] = nameserver,
                ["numa"] = numa,
                ["onboot"] = onboot,
                ["ostype"] = ostype,
                ["pool"] = pool,
                ["protection"] = protection,
                ["reboot"] = reboot,
                ["rng0"] = rng0,
                ["scsihw"] = scsihw,
                ["searchdomain"] = searchdomain,
                ["shares"] = shares,
                ["smbios1"] = smbios1,
                ["smp"] = smp,
                ["sockets"] = sockets,
                ["spice_enhancements"] = spice_enhancements,
                ["sshkeys"] = sshkeys,
                ["start"] = start,
                ["startdate"] = startdate,
                ["storage"] = storage,
                ["tablet"] = tablet,
                ["tags"] = tags,
                ["tdf"] = tdf,
                ["template"] = template,
                ["tpmstate0"] = tpmstate0,
                ["unique"] = unique,
                ["vcpus"] = vcpus,
                ["vga"] = vga,
                ["vmgenid"] = vmgenid,
                ["vmstatestorage"] = vmstatestorage,
                ["watchdog"] = watchdog,
                ["net0"] = net0,
                ["ipconfig0"] = ipconfig0,
                [disktype] = $"{storage}:{diskSize}" // Use the selected drive type
            };

            // Handle ISO mounting as CDROM
            if (!string.IsNullOrEmpty(iso))
            {
                parameters["ide2"] = $"{iso},media=cdrom";
            }

          

            AddIndexedParameters(parameters, "hostpci", hostpciN);
            AddIndexedParameters(parameters, "ide", ideN);
            AddIndexedParameters(parameters, "ipconfig", ipconfigN);
            AddIndexedParameters(parameters, "net", netN);
            AddIndexedParameters(parameters, "numa", numaN);
            AddIndexedParameters(parameters, "parallel", parallelN);
            AddIndexedParameters(parameters, "sata", sataN);
            AddIndexedParameters(parameters, "scsi", scsiN);
            AddIndexedParameters(parameters, "serial", serialN);
            AddIndexedParameters(parameters, "unused", unusedN);
            AddIndexedParameters(parameters, "usb", usbN);
            AddIndexedParameters(parameters, "virtio", virtioN);

            // Remove null values
            parameters = parameters.Where(kvp => kvp.Value != null).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            var jsonContent = JsonSerializer.Serialize(parameters, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                var response = await SendRequestWithRetryAsync(() => _httpClient.PostAsync($"/api2/json/nodes/{node}/qemu", content));
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    return new Result { Success = true, Data = jsonResponse };
                }
                else
                {
                    return new Result { Success = false, ErrorMessage = $"Failed to create VM. Status code: {response.StatusCode}, Response: {responseContent}" };
                }
            }
            catch (Exception ex)
            {
                return new Result { Success = false, ErrorMessage = $"Exception occurred while creating VM: {ex.Message}" };
            }
        }


      

        private void AddIndexedParameters(Dictionary<string, object> parameters, string prefix, IDictionary<int, string> indexedParams)
        {
            if (indexedParams != null)
            {
                foreach (var kvp in indexedParams)
                {
                    parameters[$"{prefix}{kvp.Key}"] = kvp.Value;
                }
            }
        }

        public class Result
        {
            public bool Success { get; set; }
            public JsonElement Data { get; set; }
            public string ErrorMessage { get; set; }
        }

        private async Task<HttpResponseMessage> SendRequestWithRetryAsync(Func<Task<HttpResponseMessage>> request, bool shouldReauthenticate = true)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ProxmoxClient));

            for (int i = 0; i < MaxRetries; i++)
            {
                try
                {
                    var response = await request();

                    if (response.IsSuccessStatusCode)
                    {
                        return response;
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && shouldReauthenticate)
                    {
                        await AuthenticateAsync();
                        continue;
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"Request failed with status {response.StatusCode}: {content}");
                }
                catch (Exception ex) when (ex is not ObjectDisposedException)
                {
                    if (i == MaxRetries - 1) throw;
                    await Task.Delay(RetryDelay * (i + 1));
                }
            }

            throw new Exception($"Request failed after {MaxRetries} attempts.");
        }

        private async Task<T> DeserializeResponseAsync<T>(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Request failed: {response.StatusCode}\nContent: {content}");
            }

            try
            {
                var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content);
                if (result.TryGetValue("data", out var data))
                {
                    return JsonSerializer.Deserialize<T>(data.GetRawText());
                }
                throw new InvalidOperationException($"Unexpected response format: {content}");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to parse response: {content}", ex);
            }
        }

        public async Task<List<Dictionary<string, object>>> GetNodesAsync()
        {
            var response = await SendRequestWithRetryAsync(() => _httpClient.GetAsync("/api2/json/nodes"));
            return await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);
        }

        public async Task<List<Dictionary<string, object>>> GetClustersAsync()
        {
            var response = await SendRequestWithRetryAsync(() => _httpClient.GetAsync("/api2/json/cluster/resources"));
            return await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);
        }

        public async Task<List<Dictionary<string, object>>> GetAllVmsAsync()
        {
            var nodes = await GetNodesAsync();
            var allVms = new List<Dictionary<string, object>>();

            foreach (var node in nodes)
            {
                var nodeName = node["node"].ToString();
                var vms = await GetVmsForNodeAsync(nodeName);
                foreach (var vm in vms)
                {
                    vm["node"] = nodeName;
                }
                allVms.AddRange(vms);
            }

            return allVms;
        }

        public async Task<List<Dictionary<string, object>>> GetVmsForNodeAsync(string nodeName)
        {
            var response = await SendRequestWithRetryAsync(() => _httpClient.GetAsync($"/api2/json/nodes/{nodeName}/qemu"));
            return await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);
        }

        public async Task<List<Dictionary<string, object>>> GetStorageAsync()
        {
            var response = await SendRequestWithRetryAsync(() => _httpClient.GetAsync("/api2/json/storage"));
            return await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);
        }

        public async Task<List<Dictionary<string, object>>> GetNetworksAsync()
        {
            var nodes = await GetNodesAsync();
            var allNetworks = new List<Dictionary<string, object>>();

            foreach (var node in nodes)
            {
                var nodeName = node["node"].ToString();
                var response = await SendRequestWithRetryAsync(() => _httpClient.GetAsync($"/api2/json/nodes/{nodeName}/network"));
                var networks = await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);
                allNetworks.AddRange(networks);
            }

            return allNetworks;
        }

        

        public async Task<List<string>> GetUsersAsync()
        {
            try
            {
                var response = await SendRequestWithRetryAsync(() => _httpClient.GetAsync("/api2/json/access/users"));
                var responseBody = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(responseBody);
                var data = jsonDocument.RootElement.GetProperty("data");

                var users = new List<string>();

                foreach (var user in data.EnumerateArray())
                {
                    if (user.TryGetProperty("userid", out var userId))
                    {
                        users.Add(userId.GetString());
                    }
                }

                return users;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to retrieve users", ex);
            }
        }

        public async Task<bool> UpdateVmConfigAsync(string node, string vmid, Dictionary<string, string> changes)
        {
            var validatedChanges = new Dictionary<string, string>();

            foreach (var change in changes)
            {
                string key = change.Key;
                string value = change.Value;

                switch (key)
                {
                    case string s when s.StartsWith("net"):
                        var netParts = value.Split(',');
                        if (netParts.Length < 2)
                        {
                            throw new ArgumentException($"Invalid network configuration: {value}");
                        }

                        var firstPart = netParts[0].Split('=');
                        if (!new[] { "virtio", "e1000", "rtl8139" }.Contains(firstPart[0]))
                        {
                            throw new ArgumentException($"Invalid network model: {firstPart[0]}");
                        }

                        if (!netParts.Any(p => p.StartsWith("bridge=")))
                        {
                            throw new ArgumentException("Network configuration must include bridge");
                        }
                        validatedChanges[key] = value;
                        break;

                    case "ostype":
                    case "bios":
                        if (_validValues.ContainsKey(key) && !_validValues[key].Contains(value.ToLower()))
                        {
                            throw new ArgumentException($"Invalid value for {key}: {value}");
                        }
                        validatedChanges[key] = value.ToLower();
                        break;

                    case "memory":
                    case "balloon":
                    case "cores":
                    case "sockets":
                        if (int.TryParse(value, out int numericValue) && numericValue > 0)
                        {
                            validatedChanges[key] = numericValue.ToString();
                        }
                        else
                        {
                            throw new ArgumentException($"Invalid numeric value for {key}: {value}");
                        }
                        break;

                    case "onboot":
                        validatedChanges[key] = (value == "1" || value.ToLower() == "true") ? "1" : "0";
                        break;

                    default:
                        validatedChanges[key] = value;
                        break;
                }
            }

            var content = new FormUrlEncodedContent(validatedChanges);
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PostAsync($"/api2/json/nodes/{node}/qemu/{vmid}/config", content));

            return response.IsSuccessStatusCode;
        }

        public async Task<Dictionary<string, object>> GetVmConfigAsync(string node, string vmid)
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.GetAsync($"/api2/json/nodes/{node}/qemu/{vmid}/config"));
            return await DeserializeResponseAsync<Dictionary<string, object>>(response);
        }

        public async Task<Dictionary<string, object>> GetVmStatusAsync(string node, string vmid)
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.GetAsync($"/api2/json/nodes/{node}/qemu/{vmid}/status/current"));
            return await DeserializeResponseAsync<Dictionary<string, object>>(response);
        }

        public async Task<bool> StartVmAsync(string node, string vmid)
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PostAsync($"/api2/json/nodes/{node}/qemu/{vmid}/status/start", null));
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> StopVmAsync(string node, string vmid)
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PostAsync($"/api2/json/nodes/{node}/qemu/{vmid}/status/stop", null));
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ShutdownVmAsync(string node, string vmid)
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PostAsync($"/api2/json/nodes/{node}/qemu/{vmid}/status/shutdown", null));
            return response.IsSuccessStatusCode;
        }

        public async Task<Dictionary<string, object>> GetUserConfigAsync(string userId)
        {
            try
            {
                var response = await SendRequestWithRetryAsync(() => _httpClient.GetAsync($"/api2/json/access/users/{userId}"));
                var responseBody = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(responseBody);
                var data = jsonDocument.RootElement.GetProperty("data");

                var userDict = new Dictionary<string, object>();
                foreach (var property in data.EnumerateObject())
                {
                    // Special handling for boolean values
                    if (property.Name == "enable")
                    {
                        userDict[property.Name] = property.Value.ValueKind == JsonValueKind.True;
                    }
                    else
                    {
                        userDict[property.Name] = property.Value.GetString() ?? "";
                    }
                }

                return userDict;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to retrieve user {userId}", ex);
            }
        }

        public async Task<bool> CreateUserAsync(string userid, UserConfig config)
        {
            if (string.IsNullOrEmpty(userid))
                throw new ArgumentException("User ID cannot be empty", nameof(userid));

            var parameters = new Dictionary<string, string>
            {
                ["userid"] = userid
            };

            AddUserConfigParameters(parameters, config);

            var content = new FormUrlEncodedContent(parameters);
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PostAsync("/api2/json/access/users", content));

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> UpdateUserAsync(string userid, UserConfig config)
        {
            if (string.IsNullOrEmpty(userid))
                throw new ArgumentException("User ID cannot be empty", nameof(userid));

            var parameters = new Dictionary<string, string>();
            AddUserConfigParameters(parameters, config);

            var content = new FormUrlEncodedContent(parameters);
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PutAsync($"/api2/json/access/users/{userid}", content));

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> DeleteUserAsync(string userid)
        {
            if (string.IsNullOrEmpty(userid))
                throw new ArgumentException("User ID cannot be empty", nameof(userid));

            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.DeleteAsync($"/api2/json/access/users/{userid}"));

            return response.IsSuccessStatusCode;
        }

      
        private void AddUserConfigParameters(Dictionary<string, string> parameters, UserConfig config)
        {
            if (config == null) return;

            if (!string.IsNullOrEmpty(config.Comment))
                parameters["comment"] = config.Comment;

            if (!string.IsNullOrEmpty(config.Email))
                parameters["email"] = config.Email;

            if (config.Enable.HasValue)
                parameters["enable"] = config.Enable.Value ? "1" : "0";

            if (config.Expire.HasValue)
                parameters["expire"] = config.Expire.Value.ToString();

            if (!string.IsNullOrEmpty(config.Firstname))
                parameters["firstname"] = config.Firstname;

            if (config.Groups != null && config.Groups.Any())
                parameters["groups"] = string.Join(",", config.Groups);

            if (!string.IsNullOrEmpty(config.Keys))
                parameters["keys"] = config.Keys;

            if (!string.IsNullOrEmpty(config.Lastname))
                parameters["lastname"] = config.Lastname;

            if (!string.IsNullOrEmpty(config.Password))
                parameters["password"] = config.Password;
        }

        public async Task<List<Dictionary<string, object>>> GetGroupsAsync()
        {
            try
            {
                var response = await SendRequestWithRetryAsync(() => _httpClient.GetAsync("/api2/json/access/groups"));
                var responseBody = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(responseBody);
                var data = jsonDocument.RootElement.GetProperty("data");

                var groups = new List<Dictionary<string, object>>();

                foreach (var group in data.EnumerateArray())
                {
                    var groupDict = new Dictionary<string, object>();
                    foreach (var property in group.EnumerateObject())
                    {
                        groupDict[property.Name] = property.Value.GetString();
                    }
                    groups.Add(groupDict);
                }

                return groups;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to retrieve groups", ex);
            }
        }

        public async Task<Dictionary<string, object>> GetGroupAsync(string groupId)
        {
            if (string.IsNullOrEmpty(groupId))
            {
                throw new ArgumentNullException(nameof(groupId), "Group ID cannot be null or empty.");
            }

            try
            {
                var response = await SendRequestWithRetryAsync(() => _httpClient.GetAsync($"/api2/json/access/groups/{groupId}"));
                var responseBody = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(responseBody);
                var data = jsonDocument.RootElement.GetProperty("data");

                var groupDict = new Dictionary<string, object>();
                foreach (var property in data.EnumerateObject())
                {
                    groupDict[property.Name] = property.Value.GetString();
                }

                return groupDict;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to retrieve group {groupId}", ex);
            }
        }

        public async Task<bool> CreateGroupAsync(string groupId, string comment = null)
        {
            if (string.IsNullOrEmpty(groupId))
            {
                throw new ArgumentNullException(nameof(groupId), "Group ID cannot be null or empty.");
            }

            try
            {
                var parameters = new Dictionary<string, string>
                {
                    ["groupid"] = groupId
                };

                if (!string.IsNullOrEmpty(comment))
                {
                    parameters["comment"] = comment;
                }

                var content = new FormUrlEncodedContent(parameters);
                var response = await SendRequestWithRetryAsync(() => _httpClient.PostAsync("/api2/json/access/groups", content));
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create group {groupId}", ex);
            }
        }

        public async Task<bool> UpdateGroupAsync(string groupId, Dictionary<string, object> updatedGroupData)
        {
            if (string.IsNullOrEmpty(groupId))
            {
                throw new ArgumentNullException(nameof(groupId), "Group ID cannot be null or empty.");
            }

            try
            {
                var parameters = new Dictionary<string, string>();

                if (updatedGroupData.TryGetValue("comment", out var comment))
                {
                    parameters["comment"] = comment.ToString();
                }

                if (updatedGroupData.TryGetValue("members", out var members))
                {
                    parameters["users"] = members.ToString();
                }

                var content = new FormUrlEncodedContent(parameters);
                var response = await SendRequestWithRetryAsync(() => _httpClient.PutAsync($"/api2/json/access/groups/{groupId}", content));
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to update group {groupId}", ex);
            }
        }

        public async Task<bool> DeleteGroupAsync(string groupId)
        {
            if (string.IsNullOrEmpty(groupId))
            {
                throw new ArgumentNullException(nameof(groupId), "Group ID cannot be null or empty.");
            }

            try
            {
                var response = await SendRequestWithRetryAsync(() => _httpClient.DeleteAsync($"/api2/json/access/groups/{groupId}"));
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to delete group {groupId}", ex);
            }
        }

        public async Task<bool> ResetVmAsync(string node, string vmid)
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PostAsync($"/api2/json/nodes/{node}/qemu/{vmid}/status/reset", null));
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ResizeVmDiskAsync(string node, string vmid, string disk, string size)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("disk", disk),
                new KeyValuePair<string, string>("size", size)
            });
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PutAsync($"/api2/json/nodes/{node}/qemu/{vmid}/resize", content));
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> MoveVmDiskAsync(string node, string vmid, string disk, string storage,
            string format = null, bool delete = false)
        {
            var parameters = new Dictionary<string, string>
            {
                ["disk"] = disk,
                ["storage"] = storage,
                ["delete"] = delete.ToString().ToLower()
            };

            if (!string.IsNullOrEmpty(format))
            {
                parameters["format"] = format;
            }

            var content = new FormUrlEncodedContent(parameters);
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PostAsync($"/api2/json/nodes/{node}/qemu/{vmid}/move_disk", content));
            return response.IsSuccessStatusCode;
        }

        public async Task<List<Dictionary<string, object>>> GetStorageContentAsync(string node, string storage)
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.GetAsync($"/api2/json/nodes/{node}/storage/{storage}/content"));
            return await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);
        }

        public async Task<bool> UpdateVmMemoryAsync(string node, string vmid, int memory, int balloon = 0)
        {
            var parameters = new Dictionary<string, string>
            {
                ["memory"] = memory.ToString(),
                ["balloon"] = balloon.ToString()
            };

            var content = new FormUrlEncodedContent(parameters);
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PutAsync($"/api2/json/nodes/{node}/qemu/{vmid}/config", content));
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> UpdateVmNetworkAsync(string node, string vmid, string netId, string model,
            string macAddress, string bridge = "vmbr0", bool firewall = false)
        {
            var networkConfig = $"{model}={macAddress},bridge={bridge}" + (firewall ? ",firewall=1" : "");
            var parameters = new Dictionary<string, string>
            {
                [$"net{netId}"] = networkConfig
            };

            var content = new FormUrlEncodedContent(parameters);
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PutAsync($"/api2/json/nodes/{node}/qemu/{vmid}/config", content));
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> UpdateVmCpuAsync(string node, string vmid, string type,
            int cores, int sockets = 1, int vcpus = 0)
        {
            var parameters = new Dictionary<string, string>
            {
                ["cpu"] = type,
                ["cores"] = cores.ToString(),
                ["sockets"] = sockets.ToString()
            };

            if (vcpus > 0)
            {
                parameters["vcpus"] = vcpus.ToString();
            }

            var content = new FormUrlEncodedContent(parameters);
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PutAsync($"/api2/json/nodes/{node}/qemu/{vmid}/config", content));
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> UpdateVmBootOrderAsync(string node, string vmid,
            string bootOrder, string bootDisk = null)
        {
            var parameters = new Dictionary<string, string>
            {
                ["boot"] = bootOrder
            };

            if (!string.IsNullOrEmpty(bootDisk))
            {
                parameters["bootdisk"] = bootDisk;
            }

            var content = new FormUrlEncodedContent(parameters);
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PutAsync($"/api2/json/nodes/{node}/qemu/{vmid}/config", content));
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> UpdateVmDisplayAsync(string node, string vmid,
            string type, int port = 0, string listen = "0.0.0.0")
        {
            var parameters = new Dictionary<string, string>
            {
                ["vga"] = type
            };

            if (port > 0)
            {
                parameters["port"] = port.ToString();
            }

            if (!string.IsNullOrEmpty(listen))
            {
                parameters["listen"] = listen;
            }

            var content = new FormUrlEncodedContent(parameters);
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PutAsync($"/api2/json/nodes/{node}/qemu/{vmid}/config", content));
            return response.IsSuccessStatusCode;
        }

        public async Task<Dictionary<string, object>> GetFirewallOptionsAsync(string node, string vmid)
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.GetAsync($"/api2/json/nodes/{node}/qemu/{vmid}/firewall/options"));
            return await DeserializeResponseAsync<Dictionary<string, object>>(response);
        }

        public async Task<List<Dictionary<string, object>>> GetFirewallRulesAsync(string node, string vmid)
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.GetAsync($"/api2/json/nodes/{node}/qemu/{vmid}/firewall/rules"));
            return await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);
        }

        public async Task<int> GetNextVmIdAsync()
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.GetAsync("/api2/json/cluster/nextid"));

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Failed to get next VM ID. Status code: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonDocument = JsonDocument.Parse(content);

            if (jsonDocument.RootElement.TryGetProperty("data", out var dataElement))
            {
                if (int.TryParse(dataElement.GetString(), out int result) && result > 0)
                {
                    return result;
                }
            }

            throw new InvalidOperationException("Failed to parse next VM ID from cluster response");
        }

        public async Task<List<Dictionary<string, object>>> GetStorageAsync(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
            {
                throw new ArgumentException("Node name cannot be null or empty.", nameof(nodeName));
            }

            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.GetAsync($"/api2/json/nodes/{nodeName}/storage"));

            return await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);
        }

        public async Task<bool> ConfigureNetworkAsync(Dictionary<string, string> networkConfig)
        {
            var content = new FormUrlEncodedContent(networkConfig);
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PostAsync($"/api2/json/nodes/localhost/network", content));
            return response.IsSuccessStatusCode;
        }

        public async Task<List<string>> GetCpuTypesAsync()
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.GetAsync("/api2/json/nodes/localhost/capabilities/qemu/cpu"));

            var result = await DeserializeResponseAsync<List<Dictionary<string, object>>>(response);

            if (result != null)
            {
                return result
                    .Where(cpu => cpu.ContainsKey("name") && cpu["name"] != null)
                    .Select(cpu => cpu["name"].ToString())
                    .ToList();
            }

            return new List<string>
            {
                "kvm64",
                "host",
                "Opteron_G1",
                "Opteron_G2",
                "Opteron_G3",
                "EPYC",
                "Nehalem",
                "Westmere",
                "SandyBridge",
                "IvyBridge",
                "Haswell",
                "Broadwell",
                "Skylake-Server"
            };
        }

        public async Task<List<string>> GetOsTypesAsync()
        {
            return new List<string>
            {
                "Linux",
                "Windows",
                "Solaris",
                "Other"
            };
        }

        public async Task<List<string>> GetOsVersionsAsync(string osType)
        {
            switch (osType.ToLower())
            {
                case "linux":
                    return new List<string>
                    {
                        "6.x - 2.6 Kernel",
                        "2.6 Kernel"
                    };
                case "windows":
                    return new List<string>
                    {
                        "11/2022/2025",
                        "10/2016/2019",
                        "8.x/2012/2012r2",
                        "7/2008r2",
                        "Vista/2008",
                        "Xp/2003",
                        "2000"
                    };
                case "solaris":
                    return new List<string>
                    {
                        "Solaris Kernel"
                    };
                default:
                    return new List<string>
                    {
                        "Other"
                    };
            }
        }

        public async Task<Dictionary<string, dynamic>> GetNodeStatusAsync(string nodeName)
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.GetAsync($"/api2/json/nodes/{nodeName}/status"));
            var result = await DeserializeResponseAsync<Dictionary<string, object>>(response);
            return ConvertToDynamicDictionary(result);
        }

        private Dictionary<string, dynamic> ConvertToDynamicDictionary(Dictionary<string, object> input)
        {
            return input.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value is JsonElement element ? ConvertJsonElement(element) : kvp.Value
            );
        }

        public async Task<string> GetVncUrlAsync(string node, string vmid)
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.GetAsync($"/api2/json/nodes/{node}/qemu/{vmid}/status/current"));

            if (response.IsSuccessStatusCode)
            {
                var result = await DeserializeResponseAsync<Dictionary<string, object>>(response);
                if (result.TryGetValue("name", out var vmName))
                {
                    return $"{ApiUrl}/?console=kvm&novnc=1&vmid={vmid}&vmname={vmName}&node={node}&resize=off&cmd=";
                }
            }

            throw new Exception($"Failed to get VNC URL for VM {vmid} on node {node}");
        }

        public async Task<byte[]> GetVncScreenshotAsync(string node, string vmid)
        {
            try
            {
                var response = await SendRequestWithRetryAsync(() =>
                    _httpClient.GetAsync($"/api2/json/nodes/{node}/qemu/{vmid}/screenshot"));

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    throw new HttpRequestException($"Failed to get screenshot. Status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetVncScreenshotAsync: {ex.Message}");
                return null;
            }
        }

        public async Task SendKeyEventAsync(string node, string vmid, string key)
        {
            var content = new StringContent($"{{\"key\":\"{key}\"}}", Encoding.UTF8, "application/json");
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PostAsync($"/api2/json/nodes/{node}/qemu/{vmid}/sendkey", content));

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Failed to send key event. Status code: {response.StatusCode}");
            }
        }

        public async Task<VncTicket> GetVncTicketAsync(string node, string vmid)
        {
            var response = await SendRequestWithRetryAsync(() =>
                _httpClient.PostAsync($"/api2/json/nodes/{node}/qemu/{vmid}/vncproxy", null));
            var result = await DeserializeResponseAsync<Dictionary<string, object>>(response);
            return new VncTicket
            {
                Ticket = result["ticket"].ToString(),
                Port = int.Parse(result["port"].ToString())
            };
        }

        public class VncTicket
        {
            public string Ticket { get; set; }
            public int Port { get; set; }
        }

        public class ApiResponse<T>
        {
            public T Data { get; set; }
        }

        private dynamic ConvertJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    return element.EnumerateObject()
                        .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value));
                case JsonValueKind.Array:
                    return element.EnumerateArray()
                        .Select(ConvertJsonElement).ToList();
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                default:
                    return element.ToString();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_lock)
            {
                if (_disposed) return;
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }
}

