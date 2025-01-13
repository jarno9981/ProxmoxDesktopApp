using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProxmoxApiHelper.Helpers
{
    public class UserConfig
    {
        public string Email { get; set; }
        public bool Enable { get; set; } = true;
        public string Firstname { get; set; }
        public string Lastname { get; set; }
        public string Comment { get; set; }
        public int? Expire { get; set; }
        public List<string> Groups { get; set; }
        public string Keys { get; set; }
        public string Password { get; set; }
        public bool? Append { get; set; }

        public void Validate()
        {
            if (Expire.HasValue && Expire.Value < 0)
                throw new ArgumentException("Expire must be greater than or equal to 0");

            if (!string.IsNullOrEmpty(Keys) && Keys.Length > 4096)
                throw new ArgumentException("Keys must not exceed 4096 characters");

            if (!string.IsNullOrEmpty(Keys) && !Keys.All(c => char.IsLetterOrDigit(c) || c == '!' || c == '='))
                throw new ArgumentException("Keys must contain only alphanumeric characters and != symbols");

            if (Groups != null && Groups.Any(g => string.IsNullOrWhiteSpace(g)))
                throw new ArgumentException("Group names cannot be empty or whitespace");
        }
    }

}
