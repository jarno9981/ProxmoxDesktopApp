using System.Collections.Generic;
using System.Linq;

namespace ProxmoxApiHelper.Helpers
{
    public class CpuTypesProxmox
    {
        public List<CpuCategory> Categories { get; }

        public CpuTypesProxmox()
        {
            Categories = new List<CpuCategory>
            {
                new CpuCategory("AMD", new List<string> { "Opteron_G1", "Opteron_G2", "Opteron_G3", "EPYC" }),
                new CpuCategory("Intel", new List<string> { "Nehalem", "Westmere", "SandyBridge", "IvyBridge", "Haswell", "Broadwell", "Skylake-Server" }),
                new CpuCategory("Other", new List<string> { "kvm64",  "host", "qemu32", "qemu64", "x86-64-v2-AES", "x86-64-v2" })
            };
        }

        public CpuTypesProxmox(List<string> cpuTypes)
        {
            var amdTypes = cpuTypes.Where(t => t.Contains("Opteron") || t.Contains("EPYC")).ToList();
            var intelTypes = cpuTypes.Where(t => t.Contains("Bridge") || t.Contains("well") || t.Contains("lake")).ToList();
            var otherTypes = cpuTypes.Except(amdTypes).Except(intelTypes).ToList();

            Categories = new List<CpuCategory>
            {
                new CpuCategory("AMD", amdTypes),
                new CpuCategory("Intel", intelTypes),
                new CpuCategory("Other", otherTypes)
            };
        }
    }

    public class CpuCategory
    {
        public string Name { get; }
        public List<string> Types { get; }

        public CpuCategory(string name, List<string> types)
        {
            Name = name;
            Types = types;
        }
    }
}

