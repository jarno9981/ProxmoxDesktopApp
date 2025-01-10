using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProxmoxApiHelper.Helpers
{
    public class UserConfig
    {
        public string Comment { get; set; }
        public string Email { get; set; }
        public bool? Enable { get; set; }
        public int? Expire { get; set; }
        public string Firstname { get; set; }
        public List<string> Groups { get; set; }
        public string Keys { get; set; }
        public string Lastname { get; set; }
        public string Password { get; set; }
    }
}
