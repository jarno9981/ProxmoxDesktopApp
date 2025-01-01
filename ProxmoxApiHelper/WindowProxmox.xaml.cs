using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI;
using System.Threading;
using ProxmoxApiHelper.Helpers;
using Microsoft.UI.Windowing;

namespace ProxmoxApiHelper
{
    public sealed partial class WindowProxmox : Window
    {
        private readonly ProxmoxClient _proxmoxClient;
        private ObservableCollection<ProxMachine> _vms;
        private ProxMachine _selectedVm;
        private AppWindow appWindow;

        public WindowProxmox(ProxmoxClient proxmoxClient)
        {
            this.InitializeComponent();
            _proxmoxClient = proxmoxClient ?? throw new ArgumentNullException(nameof(proxmoxClient));
            _vms = new ObservableCollection<ProxMachine>();
            InitializeAsync();
            TitleTop();
        }

        public void TitleTop()
        {
            nint hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon("apiwindowlogo.ico");

            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                throw new Exception("Unsupported OS version.");
            }
        }
        private async void InitializeAsync()
        {
            try
            {
                await LoadNodeData();
                await RefreshVMList();
                await LoadServerStats();
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Initialization Error", $"Failed to initialize: {ex.Message}");
            }
        }

        private async Task LoadNodeData()
        {
            try
            {
                var nodes = await _proxmoxClient.GetNodesAsync();
                NodeSelectionComboBoxProxmox.ItemsSource = nodes.Select(n => n["node"].ToString());
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Node Loading Error", $"Failed to load nodes: {ex.Message}");
            }
        }

        private async Task RefreshVMList()
        {
            try
            {
                LoadingIndicatorProxmox.IsActive = true;
                var vms = await _proxmoxClient.GetAllVmsAsync();
                _vms.Clear();
                foreach (var vm in vms)
                {
                    _vms.Add(new ProxMachine(vm));
                }
                VmListViewProxmox.ItemsSource = _vms;
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Refresh Error", $"Failed to refresh VM list: {ex.Message}");
            }
            finally
            {
                LoadingIndicatorProxmox.IsActive = false;
            }
        }

        private async void RefreshButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            await RefreshVMList();
        }

        private void VmListViewProxmox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedVm = VmListViewProxmox.SelectedItem as ProxMachine;
            UpdateVmDetails();
        }

        private void VmListViewProxmox_ItemClick(object sender, ItemClickEventArgs e)
        {
            _selectedVm = e.ClickedItem as ProxMachine;
            UpdateVmDetails();
        }

        private void UpdateVmDetails()
        {
            if (_selectedVm != null)
            {
                VmDetailsTextBlockProxmox.Text = $"Name: {_selectedVm.Name}\nStatus: {_selectedVm.Status}\nNode: {_selectedVm.Node}\n" +
                    $"CPU: {_selectedVm.VCPUs} cores\nMemory: {_selectedVm.RAMInMB} MB\nUptime: {_selectedVm.FormattedUptime}";
                EnableVmActionButtons();
            }
            else
            {
                VmDetailsTextBlockProxmox.Text = "Select a VM to view details";
                DisableVmActionButtons();
            }
        }

        private void EnableVmActionButtons()
        {
            StartButtonProxmox.IsEnabled = _selectedVm.Status.ToLower() != "running";
            StopButtonProxmox.IsEnabled = _selectedVm.Status.ToLower() == "running";
            ResetButtonProxmox.IsEnabled = _selectedVm.Status.ToLower() == "running";
            ShutdownButtonProxmox.IsEnabled = _selectedVm.Status.ToLower() == "running";
            EditButtonProxmox.IsEnabled = true;
            ConsoleViewerProxmox.IsEnabled = _selectedVm.Status.ToLower() == "running";
        }

        private void DisableVmActionButtons()
        {
            StartButtonProxmox.IsEnabled = false;
            StopButtonProxmox.IsEnabled = false;
            ResetButtonProxmox.IsEnabled = false;
            ShutdownButtonProxmox.IsEnabled = false;
            EditButtonProxmox.IsEnabled = false;
            ConsoleViewerProxmox.IsEnabled = false;
        }

        private async void StartButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            await PerformVmAction("start", "Starting");
        }

        private async void StopButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            await PerformVmAction("stop", "Stopping");
        }

        private async void ResetButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            await PerformVmAction("reset", "Resetting");
        }

        private async void ShutdownButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            await PerformVmAction("shutdown", "Shutting down");
        }

        private async Task PerformVmAction(string action, string actionName)
        {
            if (_selectedVm != null)
            {
                try
                {
                    StatusInfoBarProxmox.Message = $"{actionName} VM...";
                    StatusInfoBarProxmox.IsOpen = true;
                    bool success = false;
                    switch (action)
                    {
                        case "start":
                            success = await _proxmoxClient.StartVmAsync(_selectedVm.Node, _selectedVm.Id);
                            break;
                        case "stop":
                            success = await _proxmoxClient.StopVmAsync(_selectedVm.Node, _selectedVm.Id);
                            break;
                        case "reset":
                            success = await _proxmoxClient.ResetVmAsync(_selectedVm.Node, _selectedVm.Id);
                            break;
                        case "shutdown":
                            success = await _proxmoxClient.ShutdownVmAsync(_selectedVm.Node, _selectedVm.Id);
                            break;
                    }
                    if (success)
                    {
                        await RefreshVMList();
                        StatusInfoBarProxmox.Message = $"VM {actionName.ToLower()} successful.";
                        StatusInfoBarProxmox.Severity = InfoBarSeverity.Success;
                    }
                    else
                    {
                        throw new Exception($"Failed to {action} VM");
                    }
                }
                catch (Exception ex)
                {
                    StatusInfoBarProxmox.Message = $"Failed to {action} VM: {ex.Message}";
                    StatusInfoBarProxmox.Severity = InfoBarSeverity.Error;
                }
                finally
                {
                    StatusInfoBarProxmox.IsOpen = true;
                    await Task.Delay(1000);
                    await RefreshVMList();
                }
            }
        }

        private async void EditButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm != null)
            {
                try
                {
                   // var config = await _proxmoxClient.GetVmConfigAsync(_selectedVm.Node, _selectedVm.Id);
                   // EditVmProxmox editVmWindow = new EditVmProxmox(_proxmoxClient, _selectedVm.Node, _selectedVm.Id, config);
                    //editVmWindow.Activate();
                }
                catch (Exception ex)
                {
                    await ShowErrorDialog("Edit VM Error", $"Failed to load VM configuration: {ex.Message}");
                }
            }
        }

        private async void ConsoleViewerProxmox_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm != null)
            {
                try
                {
                    VncViewerWindow vnc = new VncViewerWindow(_proxmoxClient, _selectedVm.Node, _selectedVm.Id);
                    vnc.Activate();
                }
                catch (Exception ex)
                {
                    await ShowErrorDialog("VNC Error", $"Failed to open VNC viewer: {ex.Message}");
                }
            }
        }

        private void NavViewProxmox_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer != null)
            {
                string navItemTag = args.SelectedItemContainer.Tag.ToString();
                NavViewProxmox_Navigate(navItemTag);
            }
        }

        private void NavViewProxmox_Navigate(string navItemTag)
        {
            DashboardContentProxmox.Visibility = Visibility.Collapsed;
            CreateVmPanelProxmox.Visibility = Visibility.Collapsed;
            NetworkConfigPanelProxmox.Visibility = Visibility.Collapsed;
            ServerStatsPanelProxmox.Visibility = Visibility.Collapsed;

            switch (navItemTag)
            {
                case "dashboard":
                    DashboardContentProxmox.Visibility = Visibility.Visible;
                    break;
                case "create_vm":
                    CreateVmPanelProxmox.Visibility = Visibility.Visible;
                    break;
                case "network_config":
                    NetworkConfigPanelProxmox.Visibility = Visibility.Visible;
                    break;
                case "server_stats":
                    ServerStatsPanelProxmox.Visibility = Visibility.Visible;
                    break;
            }
        }


        private async void CreateVmButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            CreateVmButtonProxmox.IsEnabled = false;
            CreateVmProgressRing.IsActive = true;

            try
            {
                // Validation code remains the same
                if (string.IsNullOrWhiteSpace(NewVmIdTextBoxProxmox.Text) ||
                    string.IsNullOrWhiteSpace(NewVmNameTextBoxProxmox.Text) ||
                    NewVmCpuTextBoxProxmox.Value == 0 ||
                    NewVmMemoryTextBoxProxmox.Value == 0 ||
                    NodeSelectionComboBoxProxmox.SelectedItem == null ||
                    StorageSelectionComboBoxProxmox.SelectedItem == null ||
                    NetworkModelComboBoxProxmox.SelectedItem == null ||
                    string.IsNullOrWhiteSpace(BridgeTextBoxProxmox.Text))
                {
                    throw new ArgumentException("Please fill in all required fields.");
                }

                // Get the actual values from ComboBox items
                var osTypeValue = (OsType.SelectedItem as ComboBoxItem)?.Content.ToString().ToLower();
                var networkModelValue = (NetworkModelComboBoxProxmox.SelectedItem as ComboBoxItem)?.Content.ToString().ToLower();
                var bootDiskValue = (DriveTypeComboBoxProxmox.SelectedItem as ComboBoxItem)?.Content.ToString().ToLower();

                // Convert checkbox values to boolean parameters
                var onbootValue = OnBootCheckBoxProxmox.IsChecked ?? false;
                var agentValue = AgentCheckBoxProxmox.IsChecked == true ? "1" : "0";
                var firewall = FirewallCheckBoxProxmox.IsChecked == true ? "1" : "0";

                var autostartValue = AutoStartCheckBoxProxmox.IsChecked ?? false;

                var result = await _proxmoxClient.CreateVMAsync(
                    node: NodeSelectionComboBoxProxmox.SelectedItem.ToString(),
                    vmid: int.Parse(NewVmIdTextBoxProxmox.Text),
                    storage: StorageSelectionComboBoxProxmox.SelectedItem.ToString(),
                    diskSize: (int)NewVmDiskTextBoxProxmox.Value,
                    name: NewVmNameTextBoxProxmox.Text,
                    iso: IsoSelectionComboBoxProxmox.SelectedItem != null ? $"{IsoSelectionComboBoxProxmox.SelectedItem}" : null,
                    ostype: osTypeValue ?? "l26", // Default to Linux 2.6+ kernel if not selected
                    bios: UefiCheckBoxProxmox.IsChecked == true ? "ovmf" : "seabios",
                    net0: $"model={networkModelValue},bridge={BridgeTextBoxProxmox.Text},firewall={firewall}",
                    cores: (int)NewVmCpuTextBoxProxmox.Value,
                    memory: ((int)NewVmMemoryTextBoxProxmox.Value).ToString(),
                    balloon: ((int)NewVmMemoryTextBoxProxmox.Value).ToString(),
                    disktype: bootDiskValue ?? "scsi0",
                    onboot: onbootValue,
                    agent: AgentCheckBoxProxmox.IsChecked == true ? "1" : "0",
                    autostart: autostartValue,
                    sshkeys: SshKeysTextBoxProxmox.Text,
                    ipconfig0: IpConfig0TextBoxProxmox.Text,
                    tags: TagsTextBoxProxmox.Text,
                    kvm: KvmCheckBoxProxmox.IsChecked ?? false,
                    protection: ProtectionCheckBoxProxmox.IsChecked ?? false
                    ); 



                if (result.Success)
                {
                    StatusInfoBarProxmox.Message = "VM created successfully";
                    StatusInfoBarProxmox.Severity = InfoBarSeverity.Success;
                }
                else
                {
                    throw new Exception(result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                StatusInfoBarProxmox.Message = $"Failed to create VM: {ex.Message}";
                StatusInfoBarProxmox.Severity = InfoBarSeverity.Error;
                await ShowErrorDialog("Create VM Error", ex.Message);
            }
            finally
            {
                StatusInfoBarProxmox.IsOpen = true;
                CreateVmButtonProxmox.IsEnabled = true;
                CreateVmProgressRing.IsActive = false;
            }
        }

        private async void NodeSelectionComboBoxProxmox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NodeSelectionComboBoxProxmox.SelectedItem != null)
            {
                string selectedNode = NodeSelectionComboBoxProxmox.SelectedItem.ToString();
                await UpdateStorageComboBox(selectedNode);
                await UpdateIsoComboBox(selectedNode);
            }
        }

        private async Task UpdateStorageComboBox(string nodeName)
        {
            try
            {
                var storages = await _proxmoxClient.GetStorageAsync(nodeName);
                StorageSelectionComboBoxProxmox.ItemsSource = storages.Select(s => s["storage"].ToString());
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Storage Error", $"Failed to load storage list: {ex.Message}");
            }
        }

        private async Task UpdateIsoComboBox(string nodeName)
        {
            try
            {
                var isoList = await _proxmoxClient.GetStorageContentAsync(nodeName, "local");
                IsoSelectionComboBoxProxmox.ItemsSource = isoList
                    .Where(c => c["content"].ToString() == "iso")
                    .Select(c => c["volid"].ToString());
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("ISO Error", $"Failed to load ISO list: {ex.Message}");
            }
        }

        private async void ConfigureNetworkButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var networkConfig = new Dictionary<string, string>
                {
                    ["iface"] = NetworkInterfaceComboBoxProxmox.SelectedItem.ToString(),
                    ["address"] = IpAddressTextBoxProxmox.Text,
                    ["netmask"] = SubnetMaskTextBoxProxmox.Text,
                    ["gateway"] = GatewayTextBoxProxmox.Text
                };

                bool success = await _proxmoxClient.ConfigureNetworkAsync(networkConfig);
                if (success)
                {
                    StatusInfoBar2Proxmox.Message = "Network configuration updated successfully.";
                    StatusInfoBar2Proxmox.Severity = InfoBarSeverity.Success;
                }
                else
                {
                    throw new Exception("Failed to update network configuration");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Network Configuration Error", $"Failed to configure network: {ex.Message}");
            }
        }

        private async Task LoadServerStats()
        {
            try
            {
                var nodes = await _proxmoxClient.GetNodesAsync();
                var serverStats = new ObservableCollection<ServerStatProxmox>();

                foreach (var node in nodes)
                {
                    var nodeName = node["node"].ToString();
                    var nodeStatus = await _proxmoxClient.GetNodeStatusAsync(nodeName);

                    serverStats.Add(new ServerStatProxmox
                    {
                        HostName = nodeName,
                        CpuUsage = Convert.ToDouble(nodeStatus["cpu"]) * 100,
                        CpuUsageText = $"{Convert.ToDouble(nodeStatus["cpu"]) * 100:F1}%",
                        MemoryUsage = (Convert.ToDouble(nodeStatus["memory"]["used"]) / Convert.ToDouble(nodeStatus["memory"]["total"])) * 100,
                        MemoryUsageText = $"{(Convert.ToDouble(nodeStatus["memory"]["used"]) / Convert.ToDouble(nodeStatus["memory"]["total"])) * 100:F1}%"
                    });
                }

                ServerStatsItemsControlProxmox.ItemsSource = serverStats;
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Server Stats Error", $"Failed to load server stats: {ex.Message}");
            }
        }

        private async Task ShowErrorDialog(string title, string message)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot,
            };

            await dialog.ShowAsync();
        }

        private string GenerateRandomMacAddress()
        {
            var random = new Random();
            var macBytes = new byte[6];
            random.NextBytes(macBytes);

            // Ensure locally administered and unicast
            macBytes[0] = (byte)((macBytes[0] & 0xFE) | 0x02);

            return string.Join(":", macBytes.Select(b => b.ToString("X2")));
        }

        private void OsTypeComboBoxProxmox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OsType == null) return;

            OsType.Items.Clear();

            var selectedItem = OsTypeComboBoxProxmox.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;

            switch (selectedItem.Content.ToString())
            {
                case "Windows":
                    var windowsVersions = new[] { "wxp", "w2k", "w2k3", "w2k8", "wvista", "win7", "win8", "win10", "win11" };
                    foreach (var version in windowsVersions)
                    {
                        OsType.Items.Add(new ComboBoxItem { Content = version });
                    }
                    break;

                case "Linux":
                    var linuxVersions = new[] { "l24", "l26" };
                    foreach (var version in linuxVersions)
                    {
                        OsType.Items.Add(new ComboBoxItem { Content = version });
                    }
                    break;

                case "Other":
                    OsType.Items.Add(new ComboBoxItem { Content = "other" });
                    break;
            }

            if (OsType.Items.Count > 0)
            {
                OsType.SelectedIndex = 0;
            }
        }
    }

    public class ProxMachine
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public string Node { get; set; }
        public int VCPUs { get; set; }
        public long RAMInBytes { get; set; }
        public TimeSpan Uptime { get; set; }

        public ProxMachine(Dictionary<string, object> vmData)
        {
            Id = vmData["vmid"].ToString();
            Name = vmData["name"].ToString();
            Status = vmData["status"].ToString();
            Node = vmData["node"].ToString();

            if (vmData.TryGetValue("cpus", out object cpusObj) && cpusObj is int cpus)
            {
                VCPUs = cpus;
            }

            if (vmData.TryGetValue("maxmem", out object maxmemObj) && maxmemObj is long maxmem)
            {
                RAMInBytes = maxmem;
            }

            if (vmData.TryGetValue("uptime", out object uptimeObj) && uptimeObj is long uptimeSeconds)
            {
                Uptime = TimeSpan.FromSeconds(uptimeSeconds);
            }
        }

        public long RAMInMB => RAMInBytes / (1024 * 1024);

        public string FormattedUptime
        {
            get
            {
                if (Uptime.TotalSeconds == 0)
                    return "Not running";

                return Uptime.Days > 0
                    ? $"{Uptime.Days}d {Uptime.Hours}h {Uptime.Minutes}m"
                    : $"{Uptime.Hours}h {Uptime.Minutes}m {Uptime.Seconds}s";
            }
        }
    }

    public class ServerStatProxmox
    {
        public string HostName { get; set; }
        public double CpuUsage { get; set; }
        public string CpuUsageText { get; set; }
        public double MemoryUsage { get; set; }
        public string MemoryUsageText { get; set; }
    }

    public class StatusToColorConverterProxmox : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string status)
            {
                return status.ToLower() switch
                {
                    "running" => new SolidColorBrush(Colors.Green),
                    "stopped" => new SolidColorBrush(Colors.Red),
                    _ => new SolidColorBrush(Colors.Gray),
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}

