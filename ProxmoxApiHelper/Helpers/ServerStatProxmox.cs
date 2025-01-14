using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ProxmoxApiHelper.Helpers
{
    public class ServerStatProxmox : INotifyPropertyChanged
    {
        private string _hostName;
        private int _vmCount;
        private double _cpuUsage;
        private string _cpuUsageText;
        private double _memoryUsage;
        private string _memoryUsageText;
        private double _storageUsage;
        private string _storageUsageText;

        public string HostName
        {
            get => _hostName;
            set
            {
                if (_hostName != value)
                {
                    _hostName = value;
                    OnPropertyChanged();
                }
            }
        }

        public int VmCount
        {
            get => _vmCount;
            set
            {
                if (_vmCount != value)
                {
                    _vmCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public double CpuUsage
        {
            get => _cpuUsage;
            set
            {
                if (_cpuUsage != value)
                {
                    _cpuUsage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CpuUsageText
        {
            get => _cpuUsageText;
            set
            {
                if (_cpuUsageText != value)
                {
                    _cpuUsageText = value;
                    OnPropertyChanged();
                }
            }
        }

        public double MemoryUsage
        {
            get => _memoryUsage;
            set
            {
                if (_memoryUsage != value)
                {
                    _memoryUsage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string MemoryUsageText
        {
            get => _memoryUsageText;
            set
            {
                if (_memoryUsageText != value)
                {
                    _memoryUsageText = value;
                    OnPropertyChanged();
                }
            }
        }

        public double StorageUsage
        {
            get => _storageUsage;
            set
            {
                if (_storageUsage != value)
                {
                    _storageUsage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StorageUsageText
        {
            get => _storageUsageText;
            set
            {
                if (_storageUsageText != value)
                {
                    _storageUsageText = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


}
