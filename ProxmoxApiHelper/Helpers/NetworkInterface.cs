using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ProxmoxApiHelper.Helpers
{
    public class NetworkInterface
    {
        [JsonPropertyName("families")]
        public List<string> Families { get; set; } = new List<string>();

        [JsonPropertyName("method")]
        public string Method { get; set; }

        [JsonPropertyName("active")]
        public int? Active { get; set; }

        [JsonPropertyName("exists")]
        public int? Exists { get; set; }

        [JsonPropertyName("method6")]
        public string Method6 { get; set; }

        [JsonPropertyName("priority")]
        public int? Priority { get; set; }

        [JsonPropertyName("iface")]
        public string Iface { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("address")]
        public string Address { get; set; }

        [JsonPropertyName("autostart")]
        public int? Autostart { get; set; }

        [JsonPropertyName("gateway")]
        public string Gateway { get; set; }

        [JsonPropertyName("bridge_vids")]
        public string BridgeVids { get; set; }

        [JsonPropertyName("bridge_fd")]
        public string BridgeFd { get; set; }

        [JsonPropertyName("cidr")]
        public string Cidr { get; set; }

        [JsonPropertyName("bridge_ports")]
        public string BridgePorts { get; set; }

        [JsonPropertyName("netmask")]
        public string Netmask { get; set; }

        [JsonPropertyName("bridge_vlan_aware")]
        public int? BridgeVlanAware { get; set; }

        [JsonPropertyName("bridge_stp")]
        public string BridgeStp { get; set; }

        [JsonPropertyName("comments")]
        public string Comments { get; set; }
    }

}
