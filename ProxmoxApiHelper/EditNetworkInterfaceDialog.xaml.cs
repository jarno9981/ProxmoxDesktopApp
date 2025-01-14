using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProxmoxApiHelper.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.Graphics;

namespace ProxmoxApiHelper
{
    public sealed partial class EditNetworkInterfaceDialog : Window, INotifyPropertyChanged
    {
        private readonly ProxmoxClient _proxmoxClient;
        private readonly string _nodeName;
        private readonly NetworkInterface _interface;
        private AppWindow appWindow;


        public EditNetworkInterfaceDialog(ProxmoxClient proxmoxClient, string nodeName, NetworkInterface networkInterface)
        {
            this.InitializeComponent();

            _proxmoxClient = proxmoxClient;
            _nodeName = nodeName;
            _interface = networkInterface;

            Title = $"Edit Interface {networkInterface.Iface}";
            SetWindowSize(650, 1075);
            TitleTop();

            SaveCommand = new RelayCommand(async () => await SaveChanges());
            CancelCommand = new RelayCommand(() => this.Close());

            LoadInterfaceData();
        }

        public void TitleTop()
        {
            nint hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon("logo.ico");

            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                throw new Exception("Unsupported OS version.");
            }
        }

        private void SetWindowSize(int width, int height)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new SizeInt32(width, height));
        }

        #region Properties

        private string _selectedType;
        public string SelectedType
        {
            get => _selectedType;
            set
            {
                if (SetProperty(ref _selectedType, value))
                {
                    OnPropertyChanged(nameof(IsBridgeVisible));
                }
            }
        }

        private string _selectedMethod;
        public string SelectedMethod
        {
            get => _selectedMethod;
            set => SetProperty(ref _selectedMethod, value);
        }

        private string _address;
        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        private string _netmask;
        public string Netmask
        {
            get => _netmask;
            set => SetProperty(ref _netmask, value);
        }

        private string _gateway;
        public string Gateway
        {
            get => _gateway;
            set => SetProperty(ref _gateway, value);
        }

        private string _bridgePorts;
        public string BridgePorts
        {
            get => _bridgePorts;
            set => SetProperty(ref _bridgePorts, value);
        }

        private bool _isBridgeVlanAware;
        public bool IsBridgeVlanAware
        {
            get => _isBridgeVlanAware;
            set => SetProperty(ref _isBridgeVlanAware, value);
        }

        private string _selectedBridgeStp;
        public string SelectedBridgeStp
        {
            get => _selectedBridgeStp;
            set => SetProperty(ref _selectedBridgeStp, value);
        }

        private string _bridgeFd;
        public string BridgeFd
        {
            get => _bridgeFd;
            set => SetProperty(ref _bridgeFd, value);
        }

        private bool _isAutostart;
        public bool IsAutostart
        {
            get => _isAutostart;
            set => SetProperty(ref _isAutostart, value);
        }

        private string _comments;
        public string Comments
        {
            get => _comments;
            set => SetProperty(ref _comments, value);
        }

        public bool IsBridgeVisible => SelectedType == "Bridge";

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        #endregion

        private async void LoadInterfaceData()
        {
            try
            {
                var details = await _proxmoxClient.GetNetworkInterfaceDetailsAsync(_nodeName, _interface.Iface);
                PopulateInterfaceData(details);
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Error", $"Failed to load interface details: {ex.Message}");
                this.Close();
            }
        }

        private void PopulateInterfaceData(NetworkInterface details)
        {
            SelectedType = details.Type;
            SelectedMethod = details.Method;
            Address = details.Address;
            Netmask = details.Netmask;
            Gateway = details.Gateway;
            BridgePorts = details.BridgePorts;
            IsBridgeVlanAware = details.BridgeVlanAware == 1;
            SelectedBridgeStp = details.BridgeStp;
            BridgeFd = details.BridgeFd;
            IsAutostart = details.Autostart == 1;
            Comments = details.Comments;

            OnPropertyChanged(nameof(IsBridgeVisible));
        }

        private async Task SaveChanges()
        {
            try
            {
                var updatedInterface = new NetworkInterface
                {
                    Type = SelectedType.ToLower(),
                    Method = SelectedMethod.ToLower(),
                    Address = Address,
                    Netmask = Netmask,
                    Gateway = Gateway,
                    BridgePorts = BridgePorts,
                    BridgeVlanAware = IsBridgeVlanAware ? 1 : 0,
                    BridgeStp = SelectedBridgeStp?.ToLower(),
                    BridgeFd = BridgeFd,
                    Autostart = IsAutostart ? 1 : 0,
                    Comments = Comments
                };

                bool success = await _proxmoxClient.UpdateNetworkInterfaceAsync(_nodeName, _interface.Iface, updatedInterface);
                if (success)
                {
                    this.Close();
                }
                else
                {
                    await ShowErrorDialog("Error", "Failed to update network interface.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Error", $"Failed to save changes: {ex.Message}");
            }
        }

        private async Task ShowErrorDialog(string title, string content)
        {
            ContentDialog errorDialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK"
            };

            errorDialog.XamlRoot = this.Content.XamlRoot;
            await errorDialog.ShowAsync();
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();

        public event EventHandler CanExecuteChanged;

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

