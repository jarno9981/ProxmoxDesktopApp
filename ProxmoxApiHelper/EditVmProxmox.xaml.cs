using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProxmoxApiHelper.Helpers;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ProxmoxApiHelper
{
    public sealed partial class EditVmProxmox : Window
    {
        private readonly ProxmoxClient _proxmoxClient;
        private readonly string _node;
        private readonly string _vmid;
        private Dictionary<string, object> _currentVmConfig;
        private CpuTypesProxmox _cpuTypes;
        private AppWindow appWindow;

        public event EventHandler VmUpdated;

        public EditVmProxmox(ProxmoxClient proxmoxClient, string node, string vmid)
        {
            this.InitializeComponent();
            SetWindowSize(625, 975);
            _proxmoxClient = proxmoxClient;
            _node = node;
            _vmid = vmid;
            _cpuTypes = new CpuTypesProxmox();

            InitializeCpuTypes();
            InitializeBiosTypes();
            InitializeNetworkModels();
            InitializeDisplayTypes();
            InitializeOsTypes();

            InitializeUIAsync();
            TitleTop();
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

        private async Task InitializeUIAsync()
        {
            LoadingProgressRing.IsActive = true;
            try
            {
                await FetchVmConfigAsync();
                await InitializeCpuTypes();
                InitializeBiosTypes();
                InitializeNetworkModels();
                InitializeDisplayTypes();
                InitializeOsTypes();
                await InitializeStorageLocations();
                PopulateUIWithVmConfig();
                SetupEventHandlers();
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"An error occurred while initializing the UI: {ex.Message}");
            }
            finally
            {
                LoadingProgressRing.IsActive = false;
            }
        }

        private async Task FetchVmConfigAsync()
        {
            _currentVmConfig = await _proxmoxClient.GetVmConfigAsync(_node, _vmid);
            if (_currentVmConfig == null)
            {
                throw new Exception("Failed to fetch VM configuration.");
            }
        }

        private async Task InitializeCpuTypes()
        {
            var cpuTypes = await _proxmoxClient.GetCpuTypesAsync();
            _cpuTypes = new CpuTypesProxmox(cpuTypes);
            foreach (var category in _cpuTypes.Categories)
            {
                CpuCategoryComboBox.Items.Add(category.Name);
            }
            CpuCategoryComboBox.SelectedIndex = 0;
        }

        private void InitializeBiosTypes()
        {
            BiosTypeComboBox.Items.Add("SeaBIOS");
            BiosTypeComboBox.Items.Add("OVMF");
        }

        private void InitializeNetworkModels()
        {
            NetworkModelComboBox.Items.Add("virtio");
            NetworkModelComboBox.Items.Add("e1000");
            NetworkModelComboBox.Items.Add("rtl8139");
        }

        private void InitializeDisplayTypes()
        {
            DisplayTypeComboBox.Items.Add("std");
            DisplayTypeComboBox.Items.Add("cirrus");
            DisplayTypeComboBox.Items.Add("vmware");
            DisplayTypeComboBox.Items.Add("qxl");
        }

        private async Task InitializeOsTypes()
        {
            var osTypes = await _proxmoxClient.GetOsTypesAsync();
            foreach (var osType in osTypes)
            {
                OsTypeComboBox.Items.Add(osType);
            }
            OsTypeComboBox.SelectedIndex = 0;
            UpdateOsVersionComboBox(osTypes[0]);
        }

        private async Task UpdateOsVersionComboBox(string osType)
        {
            OsVersionComboBox.Items.Clear();
            var osVersions = await _proxmoxClient.GetOsVersionsAsync(osType);
            foreach (var version in osVersions)
            {
                OsVersionComboBox.Items.Add(version);
            }
            OsVersionComboBox.SelectedIndex = 0;
        }

        private async Task InitializeStorageLocations()
        {
            var storages = await _proxmoxClient.GetStorageAsync(_node);
            if (storages == null || !storages.Any())
            {
                throw new Exception("Failed to fetch storage locations.");
            }
            foreach (var storage in storages)
            {
                StorageComboBox.Items.Add(storage["storage"].ToString());
            }
        }

        private void PopulateUIWithVmConfig()
        {
            string GetConfigValue(string key) => _currentVmConfig.TryGetValue(key, out var value)
                ? value?.ToString()
                : "";

            long GetConfigValueLong(string key) => _currentVmConfig.TryGetValue(key, out var value)
                && long.TryParse(value?.ToString(), out var result)
                ? result
                : 0;

            bool GetConfigValueBool(string key) => _currentVmConfig.TryGetValue(key, out var value)
                && bool.TryParse(value?.ToString(), out var result)
                && result;

            NameTextBox.Text = GetConfigValue("name");
            CpuCoresTextBox.Text = GetConfigValue("cores");
            CpuSocketsTextBox.Text = GetConfigValue("sockets");
            MemoryTextBox.Text = (GetConfigValueLong("memory")).ToString();
            BalloonTextBox.Text = (GetConfigValueLong("balloon")).ToString();

            string cpuType = GetConfigValue("cpu");
            if (!string.IsNullOrEmpty(cpuType))
            {
                foreach (var category in _cpuTypes.Categories)
                {
                    if (category.Types.Contains(cpuType))
                    {
                        CpuCategoryComboBox.SelectedItem = category.Name;
                        CpuTypeComboBox.SelectedItem = cpuType;
                        break;
                    }
                }
            }

            BiosTypeComboBox.SelectedItem = GetConfigValue("bios") ?? "SeaBIOS";
            BootOrderTextBox.Text = GetConfigValue("boot");
            StartAtBootToggleSwitch.IsOn = GetConfigValueBool("onboot");

            string net0 = GetConfigValue("net0");
            if (!string.IsNullOrEmpty(net0))
            {
                var netParts = net0.Split(',');
                foreach (var part in netParts)
                {
                    var keyValue = part.Split('=');
                    if (keyValue.Length == 2)
                    {
                        switch (keyValue[0])
                        {
                            case "virtio":
                            case "e1000":
                            case "rtl8139":
                                NetworkModelComboBox.SelectedItem = keyValue[0];
                                MacAddressTextBox.Text = keyValue[1];
                                break;
                            case "bridge":
                                BridgeTextBox.Text = keyValue[1];
                                break;
                            case "firewall":
                                FirewallToggleSwitch.IsOn = keyValue[1] == "1";
                                break;
                        }
                    }
                }
            }

            DisplayTypeComboBox.SelectedItem = GetConfigValue("vga") ?? "std";

            string osType = GetConfigValue("ostype");
            if (!string.IsNullOrEmpty(osType))
            {
                OsTypeComboBox.SelectedItem = osType;
                UpdateOsVersionComboBox(osType);
                OsVersionComboBox.SelectedItem = GetConfigValue("osversion");
            }

            VirtioDriversCheckBox.IsChecked = GetConfigValueBool("virtio");
            VirtioDriversCheckBox.Visibility = osType.StartsWith("win") ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetupEventHandlers()
        {
            CpuCategoryComboBox.SelectionChanged += CpuCategoryComboBox_SelectionChanged;
            BiosTypeComboBox.SelectionChanged += BiosTypeComboBox_SelectionChanged;
            OsTypeComboBox.SelectionChanged += OsTypeComboBox_SelectionChanged;
            SaveButton.Click += SaveButton_Click;
            CancelButton.Click += CancelButton_Click;
        }

        private void CpuCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CpuCategoryComboBox.SelectedItem is string selectedCategory)
            {
                CpuTypeComboBox.Items.Clear();
                var selectedCpuTypes = _cpuTypes.Categories.FirstOrDefault(c => c.Name == selectedCategory)?.Types;
                if (selectedCpuTypes != null)
                {
                    foreach (var cpuType in selectedCpuTypes)
                    {
                        CpuTypeComboBox.Items.Add(cpuType);
                    }
                }
                CpuTypeComboBox.SelectedIndex = 0;
            }
        }

        private void BiosTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EfiWarningInfoBar.IsOpen = (BiosTypeComboBox.SelectedItem as string) == "OVMF";
        }

        private async void OsTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OsTypeComboBox.SelectedItem is string selectedOsType)
            {
                await UpdateOsVersionComboBox(selectedOsType);
                VirtioDriversCheckBox.Visibility = selectedOsType.StartsWith("win") ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SaveChangesAsync();
                VmUpdated?.Invoke(this, EventArgs.Empty);
                this.Close();
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"An error occurred while saving changes: {ex.Message}");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async Task SaveChangesAsync()
        {
            var changes = new Dictionary<string, string>();

            string GetConfigValue(string key) => _currentVmConfig.TryGetValue(key, out var value)
                ? value?.ToString()
                : "";

            if (NameTextBox.Text != GetConfigValue("name"))
                changes["name"] = NameTextBox.Text;

            if (int.TryParse(CpuCoresTextBox.Text, out int cores) && cores.ToString() != GetConfigValue("cores"))
                changes["cores"] = cores.ToString();

            if (int.TryParse(CpuSocketsTextBox.Text, out int sockets) && sockets.ToString() != GetConfigValue("sockets"))
                changes["sockets"] = sockets.ToString();

            if (CpuTypeComboBox.SelectedItem is string cpuType && cpuType != GetConfigValue("cpu"))
                changes["cpu"] = cpuType;

            if (int.TryParse(MemoryTextBox.Text, out int memory) && (memory * 1024).ToString() != GetConfigValue("memory"))
                changes["memory"] = (memory * 1024).ToString();

            if (int.TryParse(BalloonTextBox.Text, out int balloon) && (balloon * 1024).ToString() != GetConfigValue("balloon"))
                changes["balloon"] = (balloon * 1024).ToString();

            if (BiosTypeComboBox.SelectedItem is string biosType && biosType != GetConfigValue("bios"))
                changes["bios"] = biosType.ToLower();

            if (BootOrderTextBox.Text != GetConfigValue("boot"))
                changes["boot"] = BootOrderTextBox.Text;

            bool currentOnboot = _currentVmConfig.TryGetValue("onboot", out var onbootValue) &&
                bool.TryParse(onbootValue?.ToString(), out var result) && result;
            if (StartAtBootToggleSwitch.IsOn != currentOnboot)
                changes["onboot"] = StartAtBootToggleSwitch.IsOn ? "1" : "0";

            var newNet0 = $"{NetworkModelComboBox.SelectedItem as string}={MacAddressTextBox.Text},bridge={BridgeTextBox.Text},firewall={(FirewallToggleSwitch.IsOn ? "1" : "0")}";
            if (newNet0 != GetConfigValue("net0"))
                changes["net0"] = newNet0;

            if (DisplayTypeComboBox.SelectedItem is string displayType && displayType != GetConfigValue("vga"))
                changes["vga"] = displayType;

            if (OsTypeComboBox.SelectedItem is string osType && osType != GetConfigValue("ostype"))
                changes["ostype"] = osType;

            if (OsVersionComboBox.SelectedItem is string osVersion && osVersion != GetConfigValue("osversion"))
                changes["osversion"] = osVersion;

            if (VirtioDriversCheckBox.Visibility == Visibility.Visible)
            {
                bool currentVirtio = _currentVmConfig.TryGetValue("virtio", out var virtioValue) &&
                    bool.TryParse(virtioValue?.ToString(), out var virtioResult) && virtioResult;
                if (VirtioDriversCheckBox.IsChecked != currentVirtio)
                    changes["virtio"] = VirtioDriversCheckBox.IsChecked == true ? "1" : "0";
            }

            if (changes.Count > 0)
            {
                await _proxmoxClient.UpdateVmConfigAsync(_node, _vmid, changes);
                VmUpdated?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task ShowErrorDialogAsync(string message)
        {
            ContentDialog errorDialog = new ContentDialog
            {
                Title = "Error",
                Content = message,
                CloseButtonText = "OK"
            };

            errorDialog.XamlRoot = this.Content.XamlRoot;
            await errorDialog.ShowAsync();
        }
    }
}

