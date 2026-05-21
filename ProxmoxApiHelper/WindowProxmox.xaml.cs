using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.UI;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI;
using System.Threading;
using ProxmoxApiHelper.Helpers;
using Microsoft.UI.Windowing;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using System.Text;
using System.Text.Json;
using System.Net.NetworkInformation;
using NetworkInterface = ProxmoxApiHelper.Helpers.NetworkInterface;
using Windows.Storage;
using System.Diagnostics;
using System.IO;

namespace ProxmoxApiHelper
{
    public sealed partial class WindowProxmox : Window
    {
        private readonly ProxmoxClient _proxmoxClient;
        private ObservableCollection<ProxMachine> _vms;
        private ProxMachine _selectedVm;
        private AppWindow appWindow;
        private ObservableCollection<VmConfig> _massVmConfigs;
        private Random _random = new Random();
        private ObservableCollection<NetworkInterface> NetworkInterfaces { get; set; }
        private string _currentNode;

        private DispatcherTimer _serverStatsTimer;
        private bool _isServerStatsPanelVisible;
        private ApplicationDataContainer _localSettings;

        public MainPageViewModel ViewModel { get; }

        public WindowProxmox(ProxmoxClient proxmoxClient)
        {
            this.InitializeComponent();
            _proxmoxClient = proxmoxClient ?? throw new ArgumentNullException(nameof(proxmoxClient));
            _vms = new ObservableCollection<ProxMachine>();
            _massVmConfigs = new ObservableCollection<VmConfig>();
            ViewModel = new MainPageViewModel();
            NetworkInterfaces = new ObservableCollection<NetworkInterface>();
            SettingsManager.CurrentSettings.RefreshInterval = SettingsManager.CurrentSettings.RefreshInterval == 0 ? 5 : SettingsManager.CurrentSettings.RefreshInterval;
            RefreshIntervalNumberBox.Value = SettingsManager.CurrentSettings.RefreshInterval;
            InitializeServerStatsTimer(); // Initialize the timer
            this.Closed += WindowProxmox_Closed;
            InitializeAsync();
            TitleTop();
        }

        private void WindowProxmox_Closed(object sender, WindowEventArgs args)
        {
            StopServerStatsTimer();
            _serverStatsTimer = null;
            SaveSettings();
        }

       

        private void VMCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is VMItem vmItem)
            {
                vmItem.IsSelected = true;
            }
        }

        private void VMCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is VMItem vmItem)
            {
                vmItem.IsSelected = false;
            }
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
                await LoadUsersAndGroups();
                await LoadPools();

            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Initialization Error", $"Failed to initialize: {ex.Message}");
            }
        }

        private async Task LoadPools()
        {
            try
            {
                var pools = await _proxmoxClient.GetResourcePoolsAsync();
                if (pools != null && pools.Count > 0)
                {
                    PoolsListView.ItemsSource = pools;
                    PoolSelectionComboBoxProxmox.ItemsSource = pools.Select(p => p.PoolId);
                    MassVmPoolSelectionComboBox.ItemsSource = pools.Select(p => p.PoolId);


                    Console.WriteLine($"Loaded {pools.Count} pools successfully.");
                }
                else
                {
                    Console.WriteLine("No pools were retrieved or the list is empty.");
                    // Optionally, you can set a message for the user
                    // PoolsListView.ItemsSource = new List<string> { "No pools available." };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading pools: {ex.Message}");
                await ShowErrorDialog("Failed to load pools", ex.Message);
            }
        }


        private async Task LoadNodeData()
        {
            try
            {
                var nodes = await _proxmoxClient.GetNodesAsync();
                NodeSelectionComboBoxProxmox.ItemsSource = nodes.Select(n => n["node"].ToString());
                MassVmNodeSelectionComboBox.ItemsSource = nodes.Select(n => n["node"].ToString());
                NodeComboBox.ItemsSource = nodes.Select(n => n["node"].ToString());
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Node Loading Error", $"Failed to load nodes: {ex.Message}");
            }
        }

        private async void PoolsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PoolsListView.SelectedItem is Dictionary<string, object> selectedPool)
            {
                try
                {
                    string poolId = selectedPool["poolid"] as string;
                    if (string.IsNullOrEmpty(poolId))
                    {
                        throw new InvalidOperationException("Selected pool does not have a valid poolid.");
                    }

                    var poolDetails = await _proxmoxClient.GetPoolsAsync(poolId);
                    if (poolDetails == null || poolDetails.Count == 0)
                    {
                        throw new InvalidOperationException($"No details found for pool {poolId}");
                    }

                    var pool = poolDetails[0];

                    // Update UI elements
                    PoolIdTextBox.Text = poolId;
                    PoolCommentTextBox.Text = pool.ContainsKey("comment") ? pool["comment"] as string : string.Empty;

                    UpdateVMListView(pool);
                    UpdateStorageListView(pool);

                    PoolDetailsPanel.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    await ShowErrorDialog("Error", $"Failed to load pool details: {ex.Message}");
                    PoolDetailsPanel.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                ClearPoolDetails();
            }
        }

        private void UpdateVMListView(Dictionary<string, object> pool)
        {
            if (pool.ContainsKey("members") && pool["members"] is List<Dictionary<string, object>> members)
            {
                var vms = members.Where(m => m.ContainsKey("type") && m["type"] as string == "qemu").ToList();
                VMListView.ItemsSource = vms;
            }
            else
            {
                VMListView.ItemsSource = null;
            }
        }

        private void UpdateStorageListView(Dictionary<string, object> pool)
        {
            if (pool.ContainsKey("members") && pool["members"] is List<Dictionary<string, object>> members)
            {
                var storage = members.Where(m => m.ContainsKey("type") && m["type"] as string == "storage").ToList();
                StorageListView.ItemsSource = storage;
            }
            else
            {
                StorageListView.ItemsSource = null;
            }
        }


        private void ClearPoolDetails()
        {
            PoolIdTextBox.Text = string.Empty;
            PoolCommentTextBox.Text = string.Empty;
            VMListView.ItemsSource = null;
        }


        private void AddPoolButton_Click(object sender, RoutedEventArgs e)
        {
            AddPoolWindow addPoolWindow = new AddPoolWindow(_proxmoxClient);
            addPoolWindow.Activate();
        }

        private async void SavePoolButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string poolId = PoolIdTextBox.Text.Trim();
                if (string.IsNullOrEmpty(poolId))
                {
                    return;
                }

                var poolData = new Dictionary<string, object>();

                // Comment
                string comment = PoolCommentTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(comment))
                {
                    poolData["comment"] = comment;
                }

               

                // Storage
                var selectedStorage = StorageListView.SelectedItems.Cast<string>().ToList();
                if (selectedStorage.Any())
                {
                    poolData["storage"] = selectedStorage;
                }

                // VMs
                var selectedVMs = VMListView.SelectedItems.Cast<string>().ToList();
                if (selectedVMs.Any())
                {
                    poolData["vms"] = selectedVMs;
                }

                bool result = await _proxmoxClient.UpdatePoolAsync(poolId, poolData);
                if (result)
                {
                    // Optionally, refresh the pool data or close the window
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async void ShowErrorMessage(string message)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot,
                };

                await dialog.ShowAsync();
            });
        }


        private void RefreshPoolsButton_Click(object sender, RoutedEventArgs e)
        {
            LoadPools();
        }

        private async void DeletePoolButton_Click(object sender, RoutedEventArgs e)
        {
            if (PoolsListView.SelectedItem is ProxmoxClient.ResourcePool selectedPool)
            {
                var dialog = new ContentDialog
                {
                    Title = "Confirm Deletion",
                    Content = $"Are you sure you want to delete the pool '{selectedPool.PoolId}'?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    XamlRoot= this.Content.XamlRoot,
                };

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        _proxmoxClient.DeletePoolAsync($"{selectedPool.PoolId}");
                        LoadPools();

                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
        }
        private async Task LoadUsersAndGroups()
        {
            try
            {
                var users = await _proxmoxClient.GetUsersAsync();
                var groups = await _proxmoxClient.GetGroupsAsync();

              
                    ViewModel.Users.Clear();
                    foreach (var user in users)
                    {
                        ViewModel.Users.Add(new UserItem { UserId = user, IsSelected = false });
                    }

                    ViewModel.Groups.Clear();
                    foreach (var group in groups)
                    {
                        if (group.TryGetValue("groupid", out var groupId) && groupId != null)
                        {
                            ViewModel.Groups.Add(groupId.ToString());
                        }
                    }
              
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("User and Group Loading Error", $"Failed to load users and groups: {ex.Message}");
            }
        }

        private async Task RefreshVMList()
        {
            try
            {
                LoadingIndicatorProxmox.IsActive = true;
                _vms.Clear();

                var vms = await _proxmoxClient.GetAllVmsAsync();
                foreach (var vm in vms)
                    _vms.Add(new ProxMachine(vm, "qemu"));

                try
                {
                    var containers = await _proxmoxClient.GetAllLxcAsync();
                    foreach (var ct in containers)
                        _vms.Add(new ProxMachine(ct, "lxc"));
                }
                catch { }

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
                string typeLabel = _selectedVm.Type == "lxc" ? "LXC Container" : "VM";
                VmDetailsTextBlockProxmox.Text = $"Type: {typeLabel}\nName: {_selectedVm.Name}\nID: {_selectedVm.Id}\nStatus: {_selectedVm.Status}\nNode: {_selectedVm.Node}\n" +
                    $"CPU: {_selectedVm.VCPUs} cores\nMemory: {_selectedVm.RAMInMB} MB\nUptime: {_selectedVm.FormattedUptime}";
                EnableVmActionButtons();
            }
            else
            {
                VmDetailsTextBlockProxmox.Text = "Select a VM or container to view details";
                DisableVmActionButtons();
            }
        }

        private void EnableVmActionButtons()
        {
            bool isRunning = _selectedVm.Status.ToLower() == "running";
            StartButtonProxmox.IsEnabled = !isRunning;
            StopButtonProxmox.IsEnabled = isRunning;
            ResetButtonProxmox.IsEnabled = isRunning && _selectedVm.Type == "qemu";
            ShutdownButtonProxmox.IsEnabled = isRunning;
            EditButtonProxmox.IsEnabled = _selectedVm.Type == "qemu";
            DeleteButtonProxmox.IsEnabled = true;
            ConsoleViewerProxmox.IsEnabled = false;
        }

        private void DisableVmActionButtons()
        {
            StartButtonProxmox.IsEnabled = false;
            StopButtonProxmox.IsEnabled = false;
            ResetButtonProxmox.IsEnabled = false;
            ShutdownButtonProxmox.IsEnabled = false;
            EditButtonProxmox.IsEnabled = false;
            DeleteButtonProxmox.IsEnabled = false;
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
                    StatusInfoBarProxmox.Message = $"{actionName}...";
                    StatusInfoBarProxmox.IsOpen = true;
                    bool success = false;
                    bool isLxc = _selectedVm.Type == "lxc";

                    switch (action)
                    {
                        case "start":
                            success = isLxc
                                ? await _proxmoxClient.StartLxcAsync(_selectedVm.Node, _selectedVm.Id)
                                : await _proxmoxClient.StartVmAsync(_selectedVm.Node, _selectedVm.Id);
                            break;
                        case "stop":
                            success = isLxc
                                ? await _proxmoxClient.StopLxcAsync(_selectedVm.Node, _selectedVm.Id)
                                : await _proxmoxClient.StopVmAsync(_selectedVm.Node, _selectedVm.Id);
                            break;
                        case "reset":
                            success = await _proxmoxClient.ResetVmAsync(_selectedVm.Node, _selectedVm.Id);
                            break;
                        case "shutdown":
                            success = isLxc
                                ? await _proxmoxClient.ShutdownLxcAsync(_selectedVm.Node, _selectedVm.Id)
                                : await _proxmoxClient.ShutdownVmAsync(_selectedVm.Node, _selectedVm.Id);
                            break;
                    }
                    if (success)
                    {
                        StatusInfoBarProxmox.Message = $"{actionName} successful.";
                        StatusInfoBarProxmox.Severity = InfoBarSeverity.Success;
                    }
                    else
                    {
                        throw new Exception($"Failed to {action}");
                    }
                }
                catch (Exception ex)
                {
                    StatusInfoBarProxmox.Message = $"Failed to {action}: {ex.Message}";
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
                    var config = await _proxmoxClient.GetVmConfigAsync(_selectedVm.Node, _selectedVm.Id);
                    EditVmProxmox editVmWindow = new EditVmProxmox(_proxmoxClient, _selectedVm.Node, _selectedVm.Id);
                    editVmWindow.Activate();
                }
                catch (Exception ex)
                {
                    await ShowErrorDialog("Edit VM Error", $"Failed to load VM configuration: {ex.Message}");
                }
            }
        }

        private void ConsoleViewerProxmox_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null) return;
            var consoleWindow = new VncViewerWindow(_proxmoxClient, _selectedVm.Node, _selectedVm.Id, _selectedVm.Type == "lxc");
            consoleWindow.Activate();
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
            CreateLxcPanelProxmox.Visibility = Visibility.Collapsed;
            NetworkConfigPanelProxmox.Visibility = Visibility.Collapsed;
            ServerStatsPanelProxmox.Visibility = Visibility.Collapsed;
            UsersGroups.Visibility = Visibility.Collapsed;
            ManagePoolsPanel.Visibility = Visibility.Collapsed;
            SnapshotsPanelProxmox.Visibility = Visibility.Collapsed;
            BackupPanelProxmox.Visibility = Visibility.Collapsed;
            TasksPanelProxmox.Visibility = Visibility.Collapsed;
            FirewallPanelProxmox.Visibility = Visibility.Collapsed;
            NodeMgmtPanelProxmox.Visibility = Visibility.Collapsed;
            VmDetailsPanelProxmox.Visibility = Visibility.Collapsed;
            PerformancePanelProxmox.Visibility = Visibility.Collapsed;
            ClusterPanelProxmox.Visibility = Visibility.Collapsed;
            AclPanelProxmox.Visibility = Visibility.Collapsed;
            ReplicationPanelProxmox.Visibility = Visibility.Collapsed;

            StopServerStatsTimer();

            switch (navItemTag)
            {
                case "dashboard":
                    DashboardContentProxmox.Visibility = Visibility.Visible;
                    break;
                case "create_vm":
                    CreateVmPanelProxmox.Visibility = Visibility.Visible;
                    break;
                case "create_lxc":
                    CreateLxcPanelProxmox.Visibility = Visibility.Visible;
                    _ = LoadLxcNodeData();
                    break;
                case "network_config":
                    NetworkConfigPanelProxmox.Visibility = Visibility.Visible;
                    break;
                case "grps_config":
                    UsersGroups.Visibility = Visibility.Visible;
                    break;
                case "server_stats":
                    ServerStatsPanelProxmox.Visibility = Visibility.Visible;
                    LoadServerStats();
                    StartServerStatsTimer();
                    break;
                case "manage_pools":
                    ManagePoolsPanel.Visibility = Visibility.Visible;
                    break;
                case "snapshots":
                    SnapshotsPanelProxmox.Visibility = Visibility.Visible;
                    _ = LoadSnapshotsPanelAsync();
                    break;
                case "backup":
                    BackupPanelProxmox.Visibility = Visibility.Visible;
                    _ = LoadBackupPanelAsync();
                    break;
                case "tasks":
                    TasksPanelProxmox.Visibility = Visibility.Visible;
                    _ = LoadTasksPanelAsync();
                    break;
                case "firewall":
                    FirewallPanelProxmox.Visibility = Visibility.Visible;
                    _ = LoadFirewallPanelAsync();
                    break;
                case "node_mgmt":
                    NodeMgmtPanelProxmox.Visibility = Visibility.Visible;
                    _ = LoadNodeMgmtPanelAsync();
                    break;
                case "vm_details":
                    VmDetailsPanelProxmox.Visibility = Visibility.Visible;
                    _ = LoadVmDetailsPanelAsync();
                    break;
                case "performance":
                    PerformancePanelProxmox.Visibility = Visibility.Visible;
                    _ = LoadPerformancePanelAsync();
                    break;
                case "cluster":
                    ClusterPanelProxmox.Visibility = Visibility.Visible;
                    _ = LoadClusterPanelAsync();
                    break;
                case "acl":
                    AclPanelProxmox.Visibility = Visibility.Visible;
                    _ = LoadAclPanelAsync();
                    break;
                case "replication":
                    ReplicationPanelProxmox.Visibility = Visibility.Visible;
                    _ = LoadReplicationPanelAsync();
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
                    CreateVmNetworkModelComboBoxProxmox.SelectedItem == null ||
                    string.IsNullOrWhiteSpace(CreateVmBridgeTextBoxProxmox.Text))
                {
                    throw new ArgumentException("Please fill in all required fields including Network Model and Bridge.");
                }

                // Get the actual values from ComboBox items
                var osTypeValue = (OsType.SelectedItem as ComboBoxItem)?.Content.ToString().ToLower();
                var networkModelValue = (CreateVmNetworkModelComboBoxProxmox.SelectedItem as ComboBoxItem)?.Content.ToString().ToLower();
                var bootDiskValue = (DriveTypeComboBoxProxmox.SelectedItem as ComboBoxItem)?.Content.ToString().ToLower();

                // Convert checkbox values to boolean parameters
                var onbootValue = OnBootCheckBoxProxmox.IsChecked ?? false;
                var agentValue = AgentCheckBoxProxmox.IsChecked == true ? "1" : "0";
                var firewall = CreateVmFirewallCheckBoxProxmox.IsChecked == true ? "1" : "0";

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
                    net0: $"model={networkModelValue},bridge={CreateVmBridgeTextBoxProxmox.Text}" +
                          (!string.IsNullOrWhiteSpace(CreateVmVlanTagTextBoxProxmox.Text) ? $",tag={CreateVmVlanTagTextBoxProxmox.Text}" : "") +
                          $",firewall={firewall}",
                    cores: (int)NewVmCpuTextBoxProxmox.Value,
                    memory: ((int)NewVmMemoryTextBoxProxmox.Value).ToString(),
                    balloon: ((int)NewVmMemoryTextBoxProxmox.Value).ToString(),
                    disktype: bootDiskValue ?? "scsi0",
                    onboot: onbootValue,
                    agent: agentValue,
                    autostart: autostartValue,
                    sshkeys: SshKeysTextBoxProxmox.Text,
                    ipconfig0: IpConfig0TextBoxProxmox.Text,
                    tags: TagsTextBoxProxmox.Text,
                    kvm: KvmCheckBoxProxmox.IsChecked ?? false,
                    protection: ProtectionCheckBoxProxmox.IsChecked ?? false,
                    pool: PoolSelectionComboBoxProxmox.SelectedItem != null ? $"{PoolSelectionComboBoxProxmox.SelectedItem}" : null
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

        private async void NetworkInterfaceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NetworkInterfaceComboBox.SelectedItem is string selectedInterface)
            {
                try
                {
                    
                }
                catch (Exception ex)
                {
                    // Handle error (e.g., show error message to user)
                    Console.WriteLine($"Error loading network interface configuration: {ex.Message}");
                }
            }
        }

        private void IpConfigMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool isStatic = IpConfigMethodComboBox.SelectedIndex == 1;
            IpAddressTextBox.IsEnabled = isStatic;
            SubnetMaskTextBox.IsEnabled = isStatic;
            GatewayTextBox.IsEnabled = isStatic;
            DnsServersTextBox.IsEnabled = isStatic;
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
                MassVmStorageSelectionComboBox.ItemsSource = storages.Select(s => s["storage"].ToString());

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
                MassVmIsoSelectionComboBox.ItemsSource = isoList
                   .Where(c => c["content"].ToString() == "iso")
                   .Select(c => c["volid"].ToString());
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("ISO Error", $"Failed to load ISO list: {ex.Message}");
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

        private void AddUserButton_Click(object sender, RoutedEventArgs e)
        {
            var createUserWindow = new CreateUserWindow(_proxmoxClient);
            createUserWindow.Activate();
        }

        private void EditUserButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedUsers.Count == 1)
            {
                var editUserWindow = new EditUserWindow(_proxmoxClient, ViewModel.SelectedUsers[0].UserId);
                editUserWindow.Activate();
            }
            else if (ViewModel.SelectedUsers.Count > 1)
            {
                ShowErrorDialog("Edit User Error", "Please select only one user to edit.");
            }
            else
            {
                ShowErrorDialog("Edit User Error", "Please select a user to edit.");
            }
        }

        private async void DeleteUserButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedUsers.Count > 0)
            {
                var userIds = string.Join(", ", ViewModel.SelectedUsers.Select(u => u.UserId));
                ContentDialog dialog = new ContentDialog
                {
                    Title = "Confirm Delete",
                    Content = $"Are you sure you want to delete the following user(s): {userIds}?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        foreach (var user in ViewModel.SelectedUsers)
                        {
                            await _proxmoxClient.DeleteUserAsync(user.UserId);
                        }
                        await LoadUsersAndGroups();
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorDialog("Delete User Error", $"Failed to delete user(s): {ex.Message}");
                    }
                }
            }
            else
            {
                await ShowErrorDialog("Delete User Error", "Please select at least one user to delete.");
            }
        }

        private void AddGroupButton_Click(object sender, RoutedEventArgs e)
        {
            var createGroupWindow = new CreateGroup(_proxmoxClient);
            createGroupWindow.Activate();
        }

        private void EditGroupButton_Click(object sender, RoutedEventArgs e)
        {
            string groupId = GroupsListView.SelectedItem as string ?? ViewModel.SelectedGroup;
            if (!string.IsNullOrEmpty(groupId))
            {
                var editGroupWindow = new EditGroupWindow(_proxmoxClient, groupId);
                editGroupWindow.Activate();
            }
            else
            {
                ShowErrorDialog("Edit Group Error", "Please select a group to edit.");
            }
        }

        private async void DeleteGroupButton_Click(object sender, RoutedEventArgs e)
        {
            string groupId = GroupsListView.SelectedItem as string ?? ViewModel.SelectedGroup;
            if (!string.IsNullOrEmpty(groupId))
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "Confirm Delete",
                    Content = $"Are you sure you want to delete group '{groupId}'?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        await _proxmoxClient.DeleteGroupAsync(groupId);
                        ViewModel.SelectedGroup = null;
                        await LoadUsersAndGroups();
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorDialog("Delete Group Error", $"Failed to delete group: {ex.Message}");
                    }
                }
            }
            else
            {
                await ShowErrorDialog("Delete Group Error", "Please select a group to delete.");
            }
        }

        private void UserCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is UserItem userItem)
            {
                if (!ViewModel.SelectedUsers.Contains(userItem))
                {
                    ViewModel.SelectedUsers.Add(userItem);
                }
            }
        }

        private void UserCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is UserItem userItem)
            {
                ViewModel.SelectedUsers.Remove(userItem);
            }
        }

        private void GroupCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is GroupItem groupItem)
            {
                groupItem.IsSelected = true;
            }
        }

        private void GroupCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is GroupItem groupItem)
            {
                groupItem.IsSelected = false;
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadUsersAndGroups();
        }

        private string GenerateRandomVmId()
        {
            return _random.Next(100, 999999).ToString();
        }

        private string GenerateRandomVmName()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[_random.Next(s.Length)]).ToArray());
        }

       

        private async void MassCreateVmButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            MassVmCreationDialog.XamlRoot = this.Content.XamlRoot;
            await MassVmCreationDialog.ShowAsync();
        }


        private void MassVmNodeSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MassVmNodeSelectionComboBox.SelectedItem is string selectedNode)
            {
                LoadStoragesForNode(selectedNode);
                UpdateIsoComboBox(selectedNode);
            }
        }

        private async void LoadStoragesForNode(string node)
        {
            try
            {
                var storages = await _proxmoxClient.GetStorageAsync(node);
                MassVmStorageSelectionComboBox.ItemsSource = storages.Select(s => s["storage"].ToString());
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Storage Loading Error", $"Failed to load storages for node {node}: {ex.Message}");
            }
        }

        private static readonly object _lock = new object();
        private async void MassVmCreationDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            MassVmCreationProgressBar.Visibility = Visibility.Visible;
            int vmCount = (int)MassVmCountNumberBox.Value;
            MassVmCreationProgressBar.Maximum = vmCount;
            MassVmCreationProgressBar.Value = 0;

            try
            {
                // Validation
                if (MassVmNodeSelectionComboBox.SelectedItem == null ||
                    MassVmStorageSelectionComboBox.SelectedItem == null ||
                    string.IsNullOrWhiteSpace(MassVmBridgeTextBox.Text) ||
                    MassVmNetworkModelComboBox.SelectedItem == null)
                {
                    throw new ArgumentException("Please fill in all required fields.");
                }

                List<string> createdVMs = new List<string>();
                List<(string Name, string Error)> failedVMs = new List<(string Name, string Error)>();


                

                // Create VMs sequentially
                for (int i = 0; i < vmCount; i++)
                {
                    try
                    {
                        int vmIdInt = await _proxmoxClient.GetNextVmIdAsync();
                        string vmId = vmIdInt.ToString();
                        string vmName = $"VM-{GenerateRandomLetters(5)}";

                        var result = await _proxmoxClient.CreateVMAsync(
                            // Required parameters
                            node: MassVmNodeSelectionComboBox.SelectedItem.ToString(),
                            vmid: vmIdInt,
                            storage: MassVmStorageSelectionComboBox.SelectedItem.ToString(),
                            disktype: (MassVmDiskTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString().ToLower() ?? "scsi0",
                            diskSize: (int)MassVmDiskSizeGBNumberBox.Value,

                            // Basic configuration
                            name: vmName,
                            cores: (int)MassVmCpuCoresNumberBox.Value,
                            memory: ((int)MassVmMemoryMBNumberBox.Value).ToString(),
                            balloon: ((int)MassVmMemoryMBNumberBox.Value).ToString(),

                            // OS and Boot configuration
                            ostype: (MassVmOsTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString().ToLower() ?? "l26",
                            iso: MassVmIsoSelectionComboBox.SelectedItem?.ToString(),
                            bios: MassVmUefiCheckBox.IsChecked == true ? "ovmf" : "seabios",

                            // Network configuration
                            net0: $"model={(MassVmNetworkModelComboBox.SelectedItem as ComboBoxItem)?.Content.ToString().ToLower() ?? "virtio"},bridge={MassVmBridgeTextBox.Text},firewall={(MassVmFirewallCheckBox.IsChecked == true ? "1" : "0")}",

                            // Advanced options
                            acpi: true,
                            agent: MassVmAgentCheckBox.IsChecked == true ? "1" : "0",
                            autostart: false,
                            kvm: MassVmKvmCheckBox.IsChecked ?? true,
                            onboot: MassVmOnBootCheckBox.IsChecked ?? false,
                            protection: MassVmProtectionCheckBox.IsChecked ?? false,

                            // Resource pool
                            pool: MassVmPoolSelectionComboBox.SelectedItem?.ToString(),

                            // Additional configuration
                            sshkeys: "",
                            ipconfig0: "",
                            tags: MassVmTagsTextBox.Text,

                            // CPU configuration
                            cpu: "host",
                            cpuunits: 1000,

                            // System configuration
                            arch: "x86_64",

                            // Default values for other parameters
                            hotplug: "1",
                            scsihw: "virtio-scsi-pci",
                            template: false,

                            // Empty or null values for optional parameters
                            description: null,
                            searchdomain: null,
                            nameserver: null,
                            startdate: null,
                            bootdisk: null,

                            // Dictionary parameters
                            hostpciN: null,
                            ideN: null,
                            ipconfigN: null,
                            netN: null,
                            numaN: null,
                            parallelN: null,
                            sataN: null,
                            scsiN: null,
                            serialN: null,
                            unusedN: null,
                            usbN: null,
                            virtioN: null
                        );

                        if (result.Success)
                        {
                            createdVMs.Add($"{vmName} (ID: {vmId})");
                        }
                        else
                        {
                            failedVMs.Add((vmName, result.ErrorMessage ?? "Unknown error"));
                        }

                        // Update progress
                        MassVmCreationProgressBar.Value++;

                        // Add delay between VM creations
                        await Task.Delay(2000);
                    }
                    catch (Exception ex)
                    {
                        failedVMs.Add(($"VM-{i + 1}", ex.Message));
                        MassVmCreationProgressBar.Value++;
                    }
                }

                await RefreshVMList();
                MassVmCreationProgressBar.Visibility = Visibility.Collapsed;

                // Show detailed summary dialog
                await ShowDetailedMassCreationSummaryDialog(createdVMs, failedVMs);
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Mass VM Creation Error", ex.Message);
                MassVmCreationProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private string GenerateRandomLetters(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            Random random = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private async Task ShowDetailedMassCreationSummaryDialog(List<string> createdVMs, List<(string Name, string Error)> failedVMs)
        {
            StringBuilder summaryBuilder = new StringBuilder();
            summaryBuilder.AppendLine($"Successfully created VMs: {createdVMs.Count}");
            summaryBuilder.AppendLine($"Failed VM creations: {failedVMs.Count}");

            if (createdVMs.Any())
            {
                summaryBuilder.AppendLine("\nSuccessfully Created VMs:");
                foreach (var vm in createdVMs)
                {
                    summaryBuilder.AppendLine($"- {vm}");
                }
            }

            if (failedVMs.Any())
            {
                summaryBuilder.AppendLine("\nFailed VMs:");
                foreach (var (name, error) in failedVMs)
                {
                    summaryBuilder.AppendLine($"- {name}");
                    summaryBuilder.AppendLine($"  Error: {error}");
                }
            }

            ContentDialog summaryDialog = new ContentDialog
            {
                Title = "Mass VM Creation Summary",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = summaryBuilder.ToString(),
                        TextWrapping = TextWrapping.Wrap
                    },
                    MaxHeight = 400,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };

            await summaryDialog.ShowAsync();
        }

        private async Task LoadServerStats()
        {
            if (!_isServerStatsPanelVisible)
                return;

            try
            {
                var nodes = await _proxmoxClient.GetNodesAsync();
                var serverStats = new ObservableCollection<ServerStatProxmox>();

                foreach (var node in nodes)
                {
                    var nodeName = node["node"].ToString();
                    var nodeStatus = await _proxmoxClient.GetNodeStatusAsync(nodeName);
                    var vms = await _proxmoxClient.GetVMsForNodeAsync(nodeName);

                    var cpuUsage = Convert.ToDouble(nodeStatus["cpu"]) * 100;
                    var memoryUsage = (Convert.ToDouble(nodeStatus["memory"]["used"]) / Convert.ToDouble(nodeStatus["memory"]["total"])) * 100;
                    var storageUsage = (Convert.ToDouble(nodeStatus["rootfs"]["used"]) / Convert.ToDouble(nodeStatus["rootfs"]["total"])) * 100;

                    serverStats.Add(new ServerStatProxmox
                    {
                        HostName = nodeName,
                        VmCount = vms.Count,
                        CpuUsage = cpuUsage,
                        CpuUsageText = $"{cpuUsage:F1}%",
                        MemoryUsage = memoryUsage,
                        MemoryUsageText = $"{memoryUsage:F1}%",
                        StorageUsage = storageUsage,
                        StorageUsageText = $"{storageUsage:F1}%"
                    });
                }

                ServerStatsItemsControlProxmox.ItemsSource = serverStats;
                LastUpdateTextBlock.Text = $"Last updated: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Server Stats Error", $"Failed to load server stats: {ex.Message}");
            }
            finally
            {
            }
        }


        private void InitializeServerStatsTimer()
        {
            _serverStatsTimer = new DispatcherTimer();
            _serverStatsTimer.Tick += ServerStatsTimer_Tick;
            UpdateTimerInterval();
        }

        private void UpdateTimerInterval()
        {
            if (_serverStatsTimer != null)
            {
                _serverStatsTimer.Interval = TimeSpan.FromSeconds(SettingsManager.CurrentSettings.RefreshInterval);
            }
        }

        private void LoadSettings()
        {
            RefreshIntervalNumberBox.Value = SettingsManager.CurrentSettings.RefreshInterval;
        }

        private void SaveSettings()
        {
             SettingsManager.SaveSettingsAsync();
        }

        private async void RefreshStats_Click(object sender, RoutedEventArgs e)
        {
            await LoadServerStats();
        }

        private void RefreshIntervalNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (args.NewValue >= 1 && args.NewValue <= 60)
            {
                SettingsManager.CurrentSettings.RefreshInterval = (int)args.NewValue;
                UpdateTimerInterval();
            }
        }

        private async void ServerStatsTimer_Tick(object sender, object e)
        {
            if (_isServerStatsPanelVisible)
            {
                await LoadServerStats();
            }
        }

        private void StartServerStatsTimer()
        {
            _isServerStatsPanelVisible = true;
            _serverStatsTimer?.Start();
        }

        private void StopServerStatsTimer()
        {
            _isServerStatsPanelVisible = false;
            _serverStatsTimer?.Stop();
        }



        private void GroupsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is string groupId)
            {
                ViewModel.SelectedGroup = groupId;
            }
        }

        private async Task LoadNetworkInterfaces()
        {
            try
            {
                NetworkInterfaces.Clear();
                var networkData = await _proxmoxClient.GetNetworkDataAsync(_currentNode);
                foreach (var interface_ in networkData)
                {
                    // Since we're already getting NetworkInterface objects, we can add them directly
                    NetworkInterfaces.Add(interface_);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Error loading network interfaces", ex.Message);
            }
        }
        private void NodeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NodeComboBox.SelectedItem is string selectedNode)
            {
                _currentNode = selectedNode;
                LoadNetworkInterfaces();
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var networkInterface = (NetworkInterface)button.Tag;

           EditNetworkInterfaceDialog dialog = new EditNetworkInterfaceDialog(_proxmoxClient, _currentNode, networkInterface);
            dialog.Activate();
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsManager.CurrentSettings.RefreshInterval = (int)RefreshIntervalNumberBox.Value;
            SettingsManager.SaveSettingsAsync();
        }

        private async void DeleteButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null) return;

            string label = _selectedVm.Type == "lxc" ? "container" : "VM";
            ContentDialog dialog = new ContentDialog
            {
                Title = $"Confirm Delete",
                Content = $"Are you sure you want to delete {label} '{_selectedVm.Name}' (ID: {_selectedVm.Id})?\n\nThis action cannot be undone. All associated disks will be removed.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            try
            {
                StatusInfoBarProxmox.Message = $"Deleting {label}...";
                StatusInfoBarProxmox.IsOpen = true;

                bool success;
                if (_selectedVm.Type == "lxc")
                    success = await _proxmoxClient.DeleteLxcAsync(_selectedVm.Node, _selectedVm.Id, purge: true);
                else
                    success = await _proxmoxClient.DeleteVMAsync(_selectedVm.Node, _selectedVm.Id, purge: true);

                if (success)
                {
                    StatusInfoBarProxmox.Message = $"{char.ToUpper(label[0]) + label[1..]} deleted successfully.";
                    StatusInfoBarProxmox.Severity = InfoBarSeverity.Success;
                    _selectedVm = null;
                    await RefreshVMList();
                }
                else
                {
                    throw new Exception($"Server returned failure for delete operation.");
                }
            }
            catch (Exception ex)
            {
                StatusInfoBarProxmox.Message = $"Failed to delete: {ex.Message}";
                StatusInfoBarProxmox.Severity = InfoBarSeverity.Error;
                StatusInfoBarProxmox.IsOpen = true;
            }
        }

        private async Task LoadLxcNodeData()
        {
            try
            {
                var nodes = await _proxmoxClient.GetNodesAsync();
                var nodeNames = nodes.Select(n => n["node"].ToString()).ToList();
                LxcNodeSelectionComboBoxProxmox.ItemsSource = nodeNames;
                MassLxcNodeSelectionComboBox.ItemsSource = nodeNames;

                var pools = await _proxmoxClient.GetResourcePoolsAsync();
                if (pools?.Count > 0)
                {
                    LxcPoolSelectionComboBoxProxmox.ItemsSource = pools.Select(p => p.PoolId);
                    MassLxcPoolSelectionComboBox.ItemsSource = pools.Select(p => p.PoolId);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("LXC Init Error", $"Failed to load node data: {ex.Message}");
            }
        }

        private async void LxcNodeSelectionComboBoxProxmox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LxcNodeSelectionComboBoxProxmox.SelectedItem is string selectedNode)
            {
                await UpdateLxcStorageAndTemplates(selectedNode,
                    LxcStorageSelectionComboBoxProxmox,
                    LxcTemplateSelectionComboBoxProxmox);
            }
        }

        private async void MassLxcNodeSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MassLxcNodeSelectionComboBox.SelectedItem is string selectedNode)
            {
                await UpdateLxcStorageAndTemplates(selectedNode,
                    MassLxcStorageSelectionComboBox,
                    MassLxcTemplateSelectionComboBox);
            }
        }

        private async Task UpdateLxcStorageAndTemplates(string node, ComboBox storageBox, ComboBox templateBox)
        {
            try
            {
                var storages = await _proxmoxClient.GetStorageAsync(node);
                storageBox.ItemsSource = storages.Select(s => s["storage"].ToString());

                var templates = await _proxmoxClient.GetAllTemplateVolidsAsync(node);
                templateBox.ItemsSource = templates;
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("LXC Data Error", $"Failed to load storage/templates: {ex.Message}");
            }
        }

        private void LxcIpConfigComboBoxProxmox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool isStatic = LxcIpConfigComboBoxProxmox.SelectedIndex == 1;
            LxcStaticIpTextBoxProxmox.IsEnabled = isStatic;
            LxcGatewayTextBoxProxmox.IsEnabled = isStatic;
        }

        private async void CreateLxcButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            CreateLxcButtonProxmox.IsEnabled = false;
            CreateLxcProgressRing.IsActive = true;

            try
            {
                if (string.IsNullOrWhiteSpace(NewLxcHostnameTextBoxProxmox.Text) ||
                    LxcNodeSelectionComboBoxProxmox.SelectedItem == null ||
                    LxcTemplateSelectionComboBoxProxmox.SelectedItem == null ||
                    LxcStorageSelectionComboBoxProxmox.SelectedItem == null)
                {
                    throw new ArgumentException("Please fill in all required fields (Hostname, Node, Template, Storage).");
                }

                int vmid = await _proxmoxClient.GetNextVmIdAsync();
                NewLxcIdTextBoxProxmox.Text = vmid.ToString();

                string bridge = LxcBridgeTextBoxProxmox.Text;
                string net0;
                if (LxcIpConfigComboBoxProxmox.SelectedIndex == 1 && !string.IsNullOrWhiteSpace(LxcStaticIpTextBoxProxmox.Text))
                {
                    net0 = $"name=eth0,bridge={bridge},ip={LxcStaticIpTextBoxProxmox.Text}" +
                           (!string.IsNullOrWhiteSpace(LxcGatewayTextBoxProxmox.Text) ? $",gw={LxcGatewayTextBoxProxmox.Text}" : "") +
                           (!string.IsNullOrWhiteSpace(LxcVlanTagTextBoxProxmox.Text) ? $",tag={LxcVlanTagTextBoxProxmox.Text}" : "");
                }
                else
                {
                    net0 = $"name=eth0,bridge={bridge},ip=dhcp" +
                           (!string.IsNullOrWhiteSpace(LxcVlanTagTextBoxProxmox.Text) ? $",tag={LxcVlanTagTextBoxProxmox.Text}" : "");
                }

                var result = await _proxmoxClient.CreateLxcAsync(
                    node: LxcNodeSelectionComboBoxProxmox.SelectedItem.ToString(),
                    vmid: vmid,
                    ostemplate: LxcTemplateSelectionComboBoxProxmox.SelectedItem.ToString(),
                    storage: LxcStorageSelectionComboBoxProxmox.SelectedItem.ToString(),
                    diskSize: (int)NewLxcDiskNumberBoxProxmox.Value,
                    hostname: NewLxcHostnameTextBoxProxmox.Text,
                    password: string.IsNullOrWhiteSpace(LxcPasswordBoxProxmox.Password) ? null : LxcPasswordBoxProxmox.Password,
                    cores: (int)NewLxcCpuNumberBoxProxmox.Value,
                    memory: (int)NewLxcMemoryNumberBoxProxmox.Value,
                    swap: (int)NewLxcSwapNumberBoxProxmox.Value,
                    onboot: LxcOnBootCheckBoxProxmox.IsChecked ?? false,
                    start: LxcAutoStartCheckBoxProxmox.IsChecked ?? false,
                    unprivileged: LxcUnprivilegedCheckBoxProxmox.IsChecked ?? true,
                    net0: net0,
                    description: string.IsNullOrWhiteSpace(LxcDescriptionTextBoxProxmox.Text) ? null : LxcDescriptionTextBoxProxmox.Text,
                    tags: string.IsNullOrWhiteSpace(LxcTagsTextBoxProxmox.Text) ? null : LxcTagsTextBoxProxmox.Text,
                    pool: LxcPoolSelectionComboBoxProxmox.SelectedItem?.ToString(),
                    protection: LxcProtectionCheckBoxProxmox.IsChecked ?? false
                );

                if (result.Success)
                {
                    StatusInfoBarProxmox.Message = $"LXC container '{NewLxcHostnameTextBoxProxmox.Text}' created successfully (ID: {vmid}).";
                    StatusInfoBarProxmox.Severity = InfoBarSeverity.Success;
                    StatusInfoBarProxmox.IsOpen = true;
                    await RefreshVMList();
                }
                else
                {
                    throw new Exception(result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Create LXC Error", ex.Message);
            }
            finally
            {
                CreateLxcButtonProxmox.IsEnabled = true;
                CreateLxcProgressRing.IsActive = false;
            }
        }

        private async void MassCreateLxcButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            MassLxcCreationDialog.XamlRoot = this.Content.XamlRoot;
            await MassLxcCreationDialog.ShowAsync();
        }

        private async void MassLxcCreationDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            MassLxcCreationProgressBar.Visibility = Visibility.Visible;
            int count = (int)MassLxcCountNumberBox.Value;
            MassLxcCreationProgressBar.Maximum = count;
            MassLxcCreationProgressBar.Value = 0;

            try
            {
                if (MassLxcNodeSelectionComboBox.SelectedItem == null ||
                    MassLxcTemplateSelectionComboBox.SelectedItem == null ||
                    MassLxcStorageSelectionComboBox.SelectedItem == null ||
                    string.IsNullOrWhiteSpace(MassLxcBridgeTextBox.Text))
                {
                    throw new ArgumentException("Please fill in all required fields (Node, Template, Storage, Bridge).");
                }

                var createdContainers = new List<string>();
                var failedContainers = new List<(string Name, string Error)>();

                string bridge = MassLxcBridgeTextBox.Text;
                string net0 = $"name=eth0,bridge={bridge},ip=dhcp";
                string namePrefix = string.IsNullOrWhiteSpace(MassLxcNamePrefixTextBox.Text) ? "ct" : MassLxcNamePrefixTextBox.Text;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        int vmid = await _proxmoxClient.GetNextVmIdAsync();
                        string hostname = $"{namePrefix}-{i + 1}";

                        var result = await _proxmoxClient.CreateLxcAsync(
                            node: MassLxcNodeSelectionComboBox.SelectedItem.ToString(),
                            vmid: vmid,
                            ostemplate: MassLxcTemplateSelectionComboBox.SelectedItem.ToString(),
                            storage: MassLxcStorageSelectionComboBox.SelectedItem.ToString(),
                            diskSize: (int)MassLxcDiskSizeGBNumberBox.Value,
                            hostname: hostname,
                            password: string.IsNullOrWhiteSpace(MassLxcPasswordBox.Password) ? null : MassLxcPasswordBox.Password,
                            cores: (int)MassLxcCpuCoresNumberBox.Value,
                            memory: (int)MassLxcMemoryMBNumberBox.Value,
                            swap: (int)MassLxcSwapMBNumberBox.Value,
                            onboot: MassLxcOnBootCheckBox.IsChecked ?? true,
                            start: MassLxcAutoStartCheckBox.IsChecked ?? false,
                            unprivileged: MassLxcUnprivilegedCheckBox.IsChecked ?? true,
                            net0: net0,
                            pool: MassLxcPoolSelectionComboBox.SelectedItem?.ToString(),
                            tags: string.IsNullOrWhiteSpace(MassLxcTagsTextBox.Text) ? null : MassLxcTagsTextBox.Text,
                            protection: MassLxcProtectionCheckBox.IsChecked ?? false
                        );

                        if (result.Success)
                            createdContainers.Add($"{hostname} (ID: {vmid})");
                        else
                            failedContainers.Add((hostname, result.ErrorMessage ?? "Unknown error"));

                        MassLxcCreationProgressBar.Value++;
                        await Task.Delay(1500);
                    }
                    catch (Exception ex)
                    {
                        failedContainers.Add(($"{namePrefix}-{i + 1}", ex.Message));
                        MassLxcCreationProgressBar.Value++;
                    }
                }

                await RefreshVMList();
                MassLxcCreationProgressBar.Visibility = Visibility.Collapsed;

                var summary = new System.Text.StringBuilder();
                summary.AppendLine($"Successfully created: {createdContainers.Count}");
                summary.AppendLine($"Failed: {failedContainers.Count}");
                if (createdContainers.Any())
                {
                    summary.AppendLine("\nCreated Containers:");
                    foreach (var ct in createdContainers) summary.AppendLine($"  - {ct}");
                }
                if (failedContainers.Any())
                {
                    summary.AppendLine("\nFailed:");
                    foreach (var (name, error) in failedContainers) summary.AppendLine($"  - {name}: {error}");
                }

                ContentDialog summaryDialog = new ContentDialog
                {
                    Title = "Mass LXC Creation Summary",
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock { Text = summary.ToString(), TextWrapping = TextWrapping.Wrap },
                        MaxHeight = 400,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    },
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await summaryDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Mass LXC Creation Error", ex.Message);
                MassLxcCreationProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Suspend / Resume
        // ══════════════════════════════════════════════════════════════════

        private async void SuspendButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null || _selectedVm.Type != "qemu") return;
            try
            {
                await _proxmoxClient.SuspendVmAsync(_selectedVm.Node, _selectedVm.Id);
                StatusInfoBarProxmox.Message = $"Suspend command sent to VM {_selectedVm.Name}.";
                StatusInfoBarProxmox.Severity = InfoBarSeverity.Success;
                StatusInfoBarProxmox.IsOpen = true;
                await Task.Delay(2000);
                await RefreshVMList();
            }
            catch (Exception ex) { await ShowErrorDialog("Suspend Error", ex.Message); }
        }

        private async void ResumeButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null || _selectedVm.Type != "qemu") return;
            try
            {
                await _proxmoxClient.ResumeVmAsync(_selectedVm.Node, _selectedVm.Id);
                StatusInfoBarProxmox.Message = $"Resume command sent to VM {_selectedVm.Name}.";
                StatusInfoBarProxmox.Severity = InfoBarSeverity.Success;
                StatusInfoBarProxmox.IsOpen = true;
                await Task.Delay(2000);
                await RefreshVMList();
            }
            catch (Exception ex) { await ShowErrorDialog("Resume Error", ex.Message); }
        }

        // ══════════════════════════════════════════════════════════════════
        // Clone
        // ══════════════════════════════════════════════════════════════════

        private async void CloneButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null) return;
            try
            {
                var nodes = await _proxmoxClient.GetNodesAsync();
                var nodeNames = nodes.Select(n => n["node"].ToString()).ToList();
                CloneTargetNodeComboBox.ItemsSource = nodeNames;

                var storages = await _proxmoxClient.GetStorageAsync(_selectedVm.Node);
                CloneStorageComboBox.ItemsSource = storages.Select(s => s.TryGetValue("storage", out var sv) ? sv?.ToString() : null).Where(s => s != null).ToList();

                CloneNewNameTextBox.Text = $"{_selectedVm.Name}-clone";
                CloneNewIdTextBox.Text = "";
                CloneDialog.XamlRoot = this.Content.XamlRoot;
                await CloneDialog.ShowAsync();
            }
            catch (Exception ex) { await ShowErrorDialog("Clone Error", ex.Message); }
        }

        private async void CloneDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_selectedVm == null) return;
            var deferral = args.GetDeferral();
            try
            {
                if (string.IsNullOrWhiteSpace(CloneNewNameTextBox.Text))
                { await ShowErrorDialog("Validation", "New name is required."); return; }

                int newId = string.IsNullOrWhiteSpace(CloneNewIdTextBox.Text)
                    ? await _proxmoxClient.GetNextVmIdAsync()
                    : int.Parse(CloneNewIdTextBox.Text);

                string targetNode = CloneTargetNodeComboBox.SelectedItem as string;
                string storage = CloneStorageComboBox.SelectedItem as string;
                bool full = CloneFullToggle.IsOn;

                bool success;
                if (_selectedVm.Type == "lxc")
                    success = await _proxmoxClient.CloneLxcAsync(_selectedVm.Node, _selectedVm.Id, newId,
                        CloneNewNameTextBox.Text, full, storage, targetNode);
                else
                    success = await _proxmoxClient.CloneVmAsync(_selectedVm.Node, _selectedVm.Id, newId,
                        CloneNewNameTextBox.Text, full, storage, targetNode);

                if (success)
                {
                    StatusInfoBarProxmox.Message = $"Clone started: new ID {newId}. Refresh in a moment.";
                    StatusInfoBarProxmox.Severity = InfoBarSeverity.Success;
                    StatusInfoBarProxmox.IsOpen = true;
                }
                else throw new Exception("Server returned failure.");
            }
            catch (Exception ex) { args.Cancel = true; await ShowErrorDialog("Clone Error", ex.Message); }
            finally { deferral.Complete(); }
        }

        // ══════════════════════════════════════════════════════════════════
        // Migrate
        // ══════════════════════════════════════════════════════════════════

        private async void MigrateButtonProxmox_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null) return;
            try
            {
                var nodes = await _proxmoxClient.GetNodesAsync();
                MigrateTargetNodeComboBox.ItemsSource = nodes
                    .Select(n => n["node"].ToString())
                    .Where(n => n != _selectedVm.Node)
                    .ToList();

                MigrateDialog.XamlRoot = this.Content.XamlRoot;
                await MigrateDialog.ShowAsync();
            }
            catch (Exception ex) { await ShowErrorDialog("Migrate Error", ex.Message); }
        }

        private async void MigrateDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_selectedVm == null) return;
            var deferral = args.GetDeferral();
            try
            {
                if (MigrateTargetNodeComboBox.SelectedItem is not string target)
                { await ShowErrorDialog("Validation", "Select a target node."); return; }

                bool online = MigrateOnlineToggle.IsOn;
                bool withDisks = MigrateWithLocalDisksCheckBox.IsChecked == true;

                bool success;
                if (_selectedVm.Type == "lxc")
                    success = await _proxmoxClient.MigrateLxcAsync(_selectedVm.Node, _selectedVm.Id, target, online);
                else
                    success = await _proxmoxClient.MigrateVmAsync(_selectedVm.Node, _selectedVm.Id, target, online, withDisks);

                if (success)
                {
                    StatusInfoBarProxmox.Message = $"Migration to {target} started.";
                    StatusInfoBarProxmox.Severity = InfoBarSeverity.Success;
                    StatusInfoBarProxmox.IsOpen = true;
                }
                else throw new Exception("Server returned failure.");
            }
            catch (Exception ex) { args.Cancel = true; await ShowErrorDialog("Migrate Error", ex.Message); }
            finally { deferral.Complete(); }
        }

        // ══════════════════════════════════════════════════════════════════
        // Snapshots panel
        // ══════════════════════════════════════════════════════════════════

        private async Task LoadSnapshotsPanelAsync()
        {
            try
            {
                var allVms = _vms.Select(v => new SnapshotVmEntry { DisplayName = $"{v.Type.ToUpper()} {v.Id} — {v.Name} ({v.Node})", Vm = v }).ToList();
                SnapshotVmComboBox.ItemsSource = allVms;
                SnapshotVmComboBox.DisplayMemberPath = "DisplayName";
                if (_selectedVm != null)
                    SnapshotVmComboBox.SelectedItem = allVms.FirstOrDefault(x => x.Vm.Id == _selectedVm.Id);
            }
            catch (Exception ex) { await ShowErrorDialog("Snapshots Error", ex.Message); }
        }

        private async void SnapshotVmComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SnapshotVmComboBox.SelectedItem is SnapshotVmEntry entry)
                await RefreshSnapshotList(entry.Vm);
        }

        private async Task RefreshSnapshotList(ProxMachine vm)
        {
            try
            {
                SnapshotProgressRing.IsActive = true;
                List<Dictionary<string, object>> snaps;
                if (vm.Type == "lxc")
                    snaps = await _proxmoxClient.GetLxcSnapshotsAsync(vm.Node, vm.Id);
                else
                    snaps = await _proxmoxClient.GetVmSnapshotsAsync(vm.Node, vm.Id);

                var items = snaps
                    .Where(s => s.TryGetValue("name", out var n) && n?.ToString() != "current")
                    .Select(s => new SnapshotItem
                    {
                        Name = s.TryGetValue("name", out var n) ? n?.ToString() : "",
                        Description = s.TryGetValue("description", out var d) ? d?.ToString() : "",
                        Date = s.TryGetValue("snaptime", out var t) && long.TryParse(t?.ToString(), out long ts)
                            ? DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime.ToString("g") : "",
                    }).ToList();

                SnapshotsListView.ItemsSource = items;
            }
            catch (Exception ex) { await ShowErrorDialog("Snapshot Load Error", ex.Message); }
            finally { SnapshotProgressRing.IsActive = false; }
        }

        private async void CreateSnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (SnapshotVmComboBox.SelectedItem is not SnapshotVmEntry) return;
            SnapshotNameTextBox.Text = $"snap-{DateTime.Now:yyyyMMdd-HHmm}";
            SnapshotDescriptionTextBox.Text = "";
            SnapshotIncludeRamCheckBox.IsChecked = false;
            CreateSnapshotDialog.XamlRoot = this.Content.XamlRoot;
            await CreateSnapshotDialog.ShowAsync();
        }

        private async void CreateSnapshotDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (SnapshotVmComboBox.SelectedItem is not SnapshotVmEntry entry) return;
            var deferral = args.GetDeferral();
            try
            {
                if (string.IsNullOrWhiteSpace(SnapshotNameTextBox.Text))
                { await ShowErrorDialog("Validation", "Snapshot name is required."); return; }

                bool success;
                if (entry.Vm.Type == "lxc")
                    success = await _proxmoxClient.CreateLxcSnapshotAsync(entry.Vm.Node, entry.Vm.Id,
                        SnapshotNameTextBox.Text, SnapshotDescriptionTextBox.Text);
                else
                    success = await _proxmoxClient.CreateVmSnapshotAsync(entry.Vm.Node, entry.Vm.Id,
                        SnapshotNameTextBox.Text, SnapshotDescriptionTextBox.Text,
                        SnapshotIncludeRamCheckBox.IsChecked == true);

                if (success)
                    await RefreshSnapshotList(entry.Vm);
                else
                    throw new Exception("Server returned failure.");
            }
            catch (Exception ex) { args.Cancel = true; await ShowErrorDialog("Snapshot Error", ex.Message); }
            finally { deferral.Complete(); }
        }

        private async void RollbackSnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (SnapshotVmComboBox.SelectedItem is not SnapshotVmEntry entry) return;
            if (SnapshotsListView.SelectedItem is not SnapshotItem snap) return;

            ContentDialog confirm = new()
            {
                Title = "Confirm Rollback",
                Content = $"Roll back '{entry.Vm.Name}' to snapshot '{snap.Name}'?\nAll changes since this snapshot will be lost.",
                PrimaryButtonText = "Rollback",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                SnapshotProgressRing.IsActive = true;
                bool success = entry.Vm.Type == "lxc"
                    ? await _proxmoxClient.RollbackLxcSnapshotAsync(entry.Vm.Node, entry.Vm.Id, snap.Name)
                    : await _proxmoxClient.RollbackVmSnapshotAsync(entry.Vm.Node, entry.Vm.Id, snap.Name);
                if (!success) throw new Exception("Server returned failure.");
            }
            catch (Exception ex) { await ShowErrorDialog("Rollback Error", ex.Message); }
            finally { SnapshotProgressRing.IsActive = false; }
        }

        private async void DeleteSnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (SnapshotVmComboBox.SelectedItem is not SnapshotVmEntry entry) return;
            if (SnapshotsListView.SelectedItem is not SnapshotItem snap) return;

            ContentDialog confirm = new()
            {
                Title = "Delete Snapshot",
                Content = $"Delete snapshot '{snap.Name}' from '{entry.Vm.Name}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                SnapshotProgressRing.IsActive = true;
                bool success = entry.Vm.Type == "lxc"
                    ? await _proxmoxClient.DeleteLxcSnapshotAsync(entry.Vm.Node, entry.Vm.Id, snap.Name)
                    : await _proxmoxClient.DeleteVmSnapshotAsync(entry.Vm.Node, entry.Vm.Id, snap.Name);
                if (success) await RefreshSnapshotList(entry.Vm);
                else throw new Exception("Server returned failure.");
            }
            catch (Exception ex) { await ShowErrorDialog("Delete Snapshot Error", ex.Message); }
            finally { SnapshotProgressRing.IsActive = false; }
        }

        // ══════════════════════════════════════════════════════════════════
        // Backup panel
        // ══════════════════════════════════════════════════════════════════

        private async Task LoadBackupPanelAsync()
        {
            try
            {
                var nodes = await _proxmoxClient.GetNodesAsync();
                var nodeNames = nodes.Select(n => n["node"].ToString()).ToList();
                BackupNodeComboBox.ItemsSource = nodeNames;
                BackupVmComboBox.ItemsSource = _vms.Select(v => $"{v.Type.ToUpper()} {v.Id} — {v.Name}").ToList();
                await RefreshBackupListAsync();
            }
            catch (Exception ex) { await ShowErrorDialog("Backup Panel Error", ex.Message); }
        }

        private async Task RefreshBackupListAsync()
        {
            try
            {
                BackupProgressRing.IsActive = true;
                string node = BackupNodeComboBox.SelectedItem as string;
                string storage = BackupStorageComboBox.SelectedItem as string;
                var backups = await _proxmoxClient.GetBackupsAsync(node, storage);

                BackupsListView.ItemsSource = backups.Select(b => new BackupItem
                {
                    VolId = b.TryGetValue("volid", out var v) ? v?.ToString() : "",
                    Storage = b.TryGetValue("_storage", out var s) ? s?.ToString() : "",
                    Node = b.TryGetValue("_node", out var n) ? n?.ToString() : "",
                    SizeBytes = b.TryGetValue("size", out var sz) && long.TryParse(sz?.ToString(), out long bytes) ? bytes : 0,
                    CreatedAt = b.TryGetValue("ctime", out var ct) && long.TryParse(ct?.ToString(), out long ts)
                        ? DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime : DateTime.MinValue,
                }).ToList();
            }
            catch (Exception ex) { await ShowErrorDialog("Backup Load Error", ex.Message); }
            finally { BackupProgressRing.IsActive = false; }
        }

        private async void BackupNodeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackupNodeComboBox.SelectedItem is string node)
            {
                var storages = await _proxmoxClient.GetStorageAsync(node);
                BackupStorageComboBox.ItemsSource = storages
                    .Select(s => s.TryGetValue("storage", out var sv) ? sv?.ToString() : null)
                    .Where(s => s != null).ToList();
                BackupTargetStorageComboBox.ItemsSource = BackupStorageComboBox.ItemsSource;
                RestoreStorageComboBox.ItemsSource = BackupStorageComboBox.ItemsSource;
            }
        }

        private async void BackupStorageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => await RefreshBackupListAsync();

        private async void RefreshBackupsButton_Click(object sender, RoutedEventArgs e)
            => await RefreshBackupListAsync();

        private async void CreateBackupButton_Click(object sender, RoutedEventArgs e)
        {
            CreateBackupDialog.XamlRoot = this.Content.XamlRoot;
            await CreateBackupDialog.ShowAsync();
        }

        private async void CreateBackupDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                string node = BackupNodeComboBox.SelectedItem as string;
                if (string.IsNullOrEmpty(node)) { await ShowErrorDialog("Validation", "Select a node first."); return; }

                if (BackupVmComboBox.SelectedItem is not string vmEntry) { await ShowErrorDialog("Validation", "Select a VM/LXC."); return; }
                string vmid = vmEntry.Split(' ')[1];

                string storage = (BackupTargetStorageComboBox.SelectedItem as string) ?? "";
                string mode = (BackupModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "snapshot";
                string compress = (BackupCompressComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "zstd";

                string upid = await _proxmoxClient.CreateBackupAsync(node, vmid, storage, mode, compress);
                if (upid != null)
                {
                    StatusInfoBarProxmox.Message = $"Backup started (UPID: {upid.Substring(0, Math.Min(upid.Length, 30))}…)";
                    StatusInfoBarProxmox.Severity = InfoBarSeverity.Success;
                    StatusInfoBarProxmox.IsOpen = true;
                    await Task.Delay(3000);
                    await RefreshBackupListAsync();
                }
                else throw new Exception("Server returned failure.");
            }
            catch (Exception ex) { args.Cancel = true; await ShowErrorDialog("Backup Error", ex.Message); }
            finally { deferral.Complete(); }
        }

        private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (BackupsListView.SelectedItem is not BackupItem) return;
            RestoreNewVmIdTextBox.Text = "";
            RestoreHostnameTextBox.Text = "restored-container";
            RestoreBackupDialog.XamlRoot = this.Content.XamlRoot;
            await RestoreBackupDialog.ShowAsync();
        }

        private async void RestoreBackupDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (BackupsListView.SelectedItem is not BackupItem backup) return;
            var deferral = args.GetDeferral();
            try
            {
                string restoreStorage = RestoreStorageComboBox.SelectedItem as string ?? backup.Storage;
                int newVmid = string.IsNullOrWhiteSpace(RestoreNewVmIdTextBox.Text)
                    ? await _proxmoxClient.GetNextVmIdAsync()
                    : int.Parse(RestoreNewVmIdTextBox.Text);
                bool unique = RestoreUniqueCheckBox.IsChecked == true;

                bool isLxc = backup.VolId?.Contains("-ct-") == true || backup.VolId?.Contains("vzdump-lxc") == true;
                bool success;
                if (isLxc)
                    success = await _proxmoxClient.RestoreLxcBackupAsync(backup.Node, backup.Storage,
                        backup.VolId, newVmid, RestoreHostnameTextBox.Text, unique);
                else
                    success = await _proxmoxClient.RestoreVmBackupAsync(backup.Node, backup.Storage,
                        backup.VolId, newVmid, unique);

                if (success)
                {
                    StatusInfoBarProxmox.Message = $"Restore started: new ID {newVmid}.";
                    StatusInfoBarProxmox.Severity = InfoBarSeverity.Success;
                    StatusInfoBarProxmox.IsOpen = true;
                }
                else throw new Exception("Server returned failure.");
            }
            catch (Exception ex) { args.Cancel = true; await ShowErrorDialog("Restore Error", ex.Message); }
            finally { deferral.Complete(); }
        }

        private async void DeleteBackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (BackupsListView.SelectedItem is not BackupItem backup) return;
            ContentDialog confirm = new()
            {
                Title = "Delete Backup",
                Content = $"Permanently delete backup '{backup.VolId}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                BackupProgressRing.IsActive = true;
                bool success = await _proxmoxClient.DeleteBackupAsync(backup.Node, backup.Storage, backup.VolId);
                if (success) await RefreshBackupListAsync();
                else throw new Exception("Server returned failure.");
            }
            catch (Exception ex) { await ShowErrorDialog("Delete Backup Error", ex.Message); }
            finally { BackupProgressRing.IsActive = false; }
        }

        // ══════════════════════════════════════════════════════════════════
        // Tasks panel
        // ══════════════════════════════════════════════════════════════════

        private async Task LoadTasksPanelAsync()
        {
            try
            {
                var nodes = await _proxmoxClient.GetNodesAsync();
                TaskNodeComboBox.ItemsSource = nodes.Select(n => n["node"].ToString()).ToList();
                if (TaskNodeComboBox.Items.Count > 0) TaskNodeComboBox.SelectedIndex = 0;
            }
            catch (Exception ex) { await ShowErrorDialog("Tasks Error", ex.Message); }
        }

        private async void TaskNodeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => await RefreshTasksAsync();

        private async void RefreshTasksButton_Click(object sender, RoutedEventArgs e)
            => await RefreshTasksAsync();

        private async Task RefreshTasksAsync()
        {
            if (TaskNodeComboBox.SelectedItem is not string node) return;
            try
            {
                var tasks = await _proxmoxClient.GetNodeTasksAsync(node, all: true);
                TasksListView.ItemsSource = tasks.Select(t => new TaskItem
                {
                    Upid = t.TryGetValue("upid", out var u) ? u?.ToString() : "",
                    Type = t.TryGetValue("type", out var tp) ? tp?.ToString() : "",
                    Id = t.TryGetValue("id", out var id) ? id?.ToString() : "",
                    Status = t.TryGetValue("status", out var s) ? s?.ToString() : "running",
                    Node = node,
                    StartTime = t.TryGetValue("starttime", out var st) && long.TryParse(st?.ToString(), out long sts)
                        ? DateTimeOffset.FromUnixTimeSeconds(sts).LocalDateTime : DateTime.MinValue,
                    EndTime = t.TryGetValue("endtime", out var et) && long.TryParse(et?.ToString(), out long ets)
                        ? (DateTime?)DateTimeOffset.FromUnixTimeSeconds(ets).LocalDateTime : null,
                }).ToList();
            }
            catch (Exception ex) { await ShowErrorDialog("Task Load Error", ex.Message); }
        }

        private async void TasksListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TasksListView.SelectedItem is not TaskItem task) return;
            try
            {
                var log = await _proxmoxClient.GetTaskLogAsync(task.Node, task.Upid);
                var sb = new StringBuilder();
                foreach (var line in log ?? new List<Dictionary<string, object>>())
                {
                    if (line.TryGetValue("t", out var text))
                        sb.AppendLine(text?.ToString());
                }
                TaskLogTextBlock.Text = sb.ToString();
            }
            catch (Exception ex) { TaskLogTextBlock.Text = $"Error loading log: {ex.Message}"; }
        }

        private async void StopTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (TasksListView.SelectedItem is not TaskItem task) return;
            try
            {
                await _proxmoxClient.StopTaskAsync(task.Node, task.Upid);
                await RefreshTasksAsync();
            }
            catch (Exception ex) { await ShowErrorDialog("Stop Task Error", ex.Message); }
        }

        // ══════════════════════════════════════════════════════════════════
        // Firewall panel
        // ══════════════════════════════════════════════════════════════════

        private async Task LoadFirewallPanelAsync()
        {
            var allVms = _vms.Select(v => new SnapshotVmEntry { DisplayName = $"{v.Type.ToUpper()} {v.Id} — {v.Name} ({v.Node})", Vm = v }).ToList();
            FirewallVmComboBox.ItemsSource = allVms;
            FirewallVmComboBox.DisplayMemberPath = "DisplayName";
        }

        private async void FirewallVmComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FirewallVmComboBox.SelectedItem is SnapshotVmEntry entry && entry.Vm.Type == "qemu")
                await RefreshFirewallRulesAsync(entry.Vm);
        }

        private async void FirewallEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (FirewallVmComboBox.SelectedItem is not SnapshotVmEntry entry || entry.Vm.Type != "qemu") return;
            try
            {
                await _proxmoxClient.SetVmFirewallEnabledAsync(entry.Vm.Node, entry.Vm.Id, FirewallEnabledToggle.IsOn);
            }
            catch (Exception ex) { await ShowErrorDialog("Firewall Error", ex.Message); }
        }

        private async void RefreshFirewallButton_Click(object sender, RoutedEventArgs e)
        {
            if (FirewallVmComboBox.SelectedItem is SnapshotVmEntry entry)
                await RefreshFirewallRulesAsync(entry.Vm);
        }

        private async Task RefreshFirewallRulesAsync(ProxMachine vm)
        {
            try
            {
                var rules = await _proxmoxClient.GetFirewallRulesAsync(vm.Node, vm.Id);
                FirewallRulesListView.ItemsSource = rules.Select((r, i) => new FirewallRuleItem
                {
                    Pos = r.TryGetValue("pos", out var p) ? p?.ToString() : i.ToString(),
                    Action = r.TryGetValue("action", out var a) ? a?.ToString() : "",
                    Type = r.TryGetValue("type", out var t) ? t?.ToString() : "",
                    Source = r.TryGetValue("source", out var s) ? s?.ToString() : "any",
                    Dest = r.TryGetValue("dest", out var d) ? d?.ToString() : "any",
                    Comment = r.TryGetValue("comment", out var c) ? c?.ToString() : "",
                    Enabled = !r.TryGetValue("enable", out var en) || en?.ToString() != "0",
                }).ToList();
            }
            catch (Exception ex) { await ShowErrorDialog("Firewall Rules Error", ex.Message); }
        }

        private async void AddFirewallRuleButton_Click(object sender, RoutedEventArgs e)
        {
            FwRuleActionComboBox.SelectedIndex = 0;
            FwRuleTypeComboBox.SelectedIndex = 0;
            FwRuleProtoComboBox.SelectedIndex = 1;
            FwRuleSourceTextBox.Text = "";
            FwRuleDestTextBox.Text = "";
            FwRuleDportTextBox.Text = "";
            FwRuleSportTextBox.Text = "";
            FwRuleCommentTextBox.Text = "";
            FwRuleEnabledCheckBox.IsChecked = true;
            AddFirewallRuleDialog.XamlRoot = this.Content.XamlRoot;
            await AddFirewallRuleDialog.ShowAsync();
        }

        private async void AddFirewallRuleDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (FirewallVmComboBox.SelectedItem is not SnapshotVmEntry entry) return;
            var deferral = args.GetDeferral();
            try
            {
                string action = (FwRuleActionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ACCEPT";
                string type = (FwRuleTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "in";
                string proto = (FwRuleProtoComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                bool success = await _proxmoxClient.AddFirewallRuleAsync(
                    entry.Vm.Node, entry.Vm.Id,
                    action, type,
                    FwRuleSourceTextBox.Text.Trim(),
                    FwRuleDestTextBox.Text.Trim(),
                    proto,
                    FwRuleDportTextBox.Text.Trim(),
                    FwRuleSportTextBox.Text.Trim(),
                    FwRuleCommentTextBox.Text.Trim(),
                    FwRuleEnabledCheckBox.IsChecked == true);

                if (success) await RefreshFirewallRulesAsync(entry.Vm);
                else throw new Exception("Server returned failure.");
            }
            catch (Exception ex) { args.Cancel = true; await ShowErrorDialog("Add Rule Error", ex.Message); }
            finally { deferral.Complete(); }
        }

        private async void DeleteFirewallRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (FirewallVmComboBox.SelectedItem is not SnapshotVmEntry entry) return;
            if (FirewallRulesListView.SelectedItem is not FirewallRuleItem rule) return;
            if (!int.TryParse(rule.Pos, out int pos)) return;
            try
            {
                bool success = await _proxmoxClient.DeleteFirewallRuleAsync(entry.Vm.Node, entry.Vm.Id, pos);
                if (success) await RefreshFirewallRulesAsync(entry.Vm);
                else throw new Exception("Server returned failure.");
            }
            catch (Exception ex) { await ShowErrorDialog("Delete Rule Error", ex.Message); }
        }

        // ══════════════════════════════════════════════════════════════════
        // Node Management panel
        // ══════════════════════════════════════════════════════════════════

        private async Task LoadNodeMgmtPanelAsync()
        {
            try
            {
                var nodes = await _proxmoxClient.GetNodesAsync();
                NodeMgmtNodeComboBox.ItemsSource = nodes.Select(n => n["node"].ToString()).ToList();
                if (NodeMgmtNodeComboBox.Items.Count > 0) NodeMgmtNodeComboBox.SelectedIndex = 0;
            }
            catch (Exception ex) { await ShowErrorDialog("Node Mgmt Error", ex.Message); }
        }

        private async void NodeMgmtNodeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NodeMgmtNodeComboBox.SelectedItem is string node)
            {
                await LoadNodeDnsAsync(node);
                await LoadNodeSyslogAsync(node);
            }
        }

        private async Task LoadNodeDnsAsync(string node)
        {
            try
            {
                var dns = await _proxmoxClient.GetNodeDnsAsync(node);
                NodeDnsSearchTextBox.Text = dns.TryGetValue("search", out var s) ? s?.ToString() : "";
                NodeDns1TextBox.Text = dns.TryGetValue("dns1", out var d1) ? d1?.ToString() : "";
                NodeDns2TextBox.Text = dns.TryGetValue("dns2", out var d2) ? d2?.ToString() : "";
                NodeDns3TextBox.Text = dns.TryGetValue("dns3", out var d3) ? d3?.ToString() : "";
            }
            catch { }
        }

        private async Task LoadNodeSyslogAsync(string node)
        {
            try
            {
                var lines = await _proxmoxClient.GetNodeSyslogAsync(node, 200);
                var sb = new StringBuilder();
                foreach (var line in lines ?? new List<Dictionary<string, object>>())
                {
                    if (line.TryGetValue("t", out var t)) sb.AppendLine(t?.ToString());
                }
                SyslogTextBlock.Text = sb.ToString();
            }
            catch (Exception ex) { SyslogTextBlock.Text = $"Could not load syslog: {ex.Message}"; }
        }

        private async void NodeRebootButton_Click(object sender, RoutedEventArgs e)
        {
            if (NodeMgmtNodeComboBox.SelectedItem is not string node) return;
            ContentDialog confirm = new()
            {
                Title = "Reboot Node",
                Content = $"Reboot node '{node}'? All VMs on this node will be affected.",
                PrimaryButtonText = "Reboot",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            try { await _proxmoxClient.RebootNodeAsync(node); }
            catch (Exception ex) { await ShowErrorDialog("Reboot Error", ex.Message); }
        }

        private async void NodeShutdownButton_Click(object sender, RoutedEventArgs e)
        {
            if (NodeMgmtNodeComboBox.SelectedItem is not string node) return;
            ContentDialog confirm = new()
            {
                Title = "Shutdown Node",
                Content = $"Shut down node '{node}'? All VMs on this node will be stopped.",
                PrimaryButtonText = "Shutdown",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            try { await _proxmoxClient.ShutdownNodeAsync(node); }
            catch (Exception ex) { await ShowErrorDialog("Shutdown Error", ex.Message); }
        }

        private async void SaveNodeDnsButton_Click(object sender, RoutedEventArgs e)
        {
            if (NodeMgmtNodeComboBox.SelectedItem is not string node) return;
            try
            {
                bool success = await _proxmoxClient.UpdateNodeDnsAsync(node,
                    NodeDnsSearchTextBox.Text.Trim(),
                    NodeDns1TextBox.Text.Trim(),
                    NodeDns2TextBox.Text.Trim(),
                    NodeDns3TextBox.Text.Trim());
                if (!success) throw new Exception("Server returned failure.");
            }
            catch (Exception ex) { await ShowErrorDialog("DNS Save Error", ex.Message); }
        }

        private async void RefreshSyslogButton_Click(object sender, RoutedEventArgs e)
        {
            if (NodeMgmtNodeComboBox.SelectedItem is string node)
                await LoadNodeSyslogAsync(node);
        }

        // ──────────────────────────────────────────────────────────────────
        // VM Details panel
        // ──────────────────────────────────────────────────────────────────

        private async Task LoadVmDetailsPanelAsync()
        {
            try
            {
                VmDetailsVmComboBox.Items.Clear();
                var allVms = _vms.ToList();
                foreach (var vm in allVms)
                    VmDetailsVmComboBox.Items.Add(new SnapshotVmEntry { DisplayName = $"{vm.Name} ({vm.Id}) [{vm.Node}]", Vm = vm });
                if (_selectedVm != null)
                {
                    for (int i = 0; i < VmDetailsVmComboBox.Items.Count; i++)
                    {
                        if (VmDetailsVmComboBox.Items[i] is SnapshotVmEntry entry && entry.Vm.Id == _selectedVm.Id)
                        {
                            VmDetailsVmComboBox.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Error loading VM details panel: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void VmDetailsVmComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VmDetailsVmComboBox.SelectedItem is SnapshotVmEntry entry)
                await RefreshVmDetailsAsync(entry.Vm);
        }

        private async void VmDetailsRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (VmDetailsVmComboBox.SelectedItem is SnapshotVmEntry entry)
                await RefreshVmDetailsAsync(entry.Vm);
        }

        private async void VmDetailsConsoleButton_Click(object sender, RoutedEventArgs e)
        {
            if (VmDetailsVmComboBox.SelectedItem is SnapshotVmEntry entry)
            {
                var consoleWindow = new VncViewerWindow(_proxmoxClient, entry.Vm.Node, entry.Vm.Id, entry.Vm.Type == "lxc");
                consoleWindow.Activate();
            }
        }

        private async Task RefreshVmDetailsAsync(ProxMachine vm)
        {
            try
            {
                bool isLxc = vm.Type == "lxc";
                VmDetailsIdText.Text = vm.Id;
                VmDetailsNameText.Text = vm.Name;
                VmDetailsNodeText.Text = vm.Node;
                VmDetailsTypeText.Text = isLxc ? "LXC Container" : "QEMU Virtual Machine";

                Dictionary<string, object> status;
                if (isLxc)
                    status = await _proxmoxClient.GetLxcCurrentStatusAsync(vm.Node, vm.Id);
                else
                    status = await _proxmoxClient.GetVmCurrentStatusAsync(vm.Node, vm.Id);

                string vmStatus = status.GetValueOrDefault("status")?.ToString() ?? "unknown";
                VmDetailsStatusText.Text = vmStatus;

                if (status.TryGetValue("uptime", out var uptimeObj) && long.TryParse(uptimeObj?.ToString(), out long uptime))
                {
                    var ts = TimeSpan.FromSeconds(uptime);
                    VmDetailsUptimeText.Text = $"{(int)ts.TotalDays}d {ts.Hours:D2}h {ts.Minutes:D2}m";
                }
                else VmDetailsUptimeText.Text = "—";

                if (status.TryGetValue("cpu", out var cpuObj) && double.TryParse(cpuObj?.ToString(), out double cpu))
                {
                    double cpuPct = cpu * 100;
                    VmDetailsCpuBar.Value = cpuPct;
                    VmDetailsCpuText.Text = $"{cpuPct:F1}%";
                }
                else { VmDetailsCpuBar.Value = 0; VmDetailsCpuText.Text = "—"; }

                if (status.TryGetValue("mem", out var memObj) && status.TryGetValue("maxmem", out var maxMemObj)
                    && long.TryParse(memObj?.ToString(), out long mem) && long.TryParse(maxMemObj?.ToString(), out long maxMem) && maxMem > 0)
                {
                    double memPct = (double)mem / maxMem * 100;
                    VmDetailsMemBar.Value = memPct;
                    VmDetailsMemText.Text = $"{mem / 1024 / 1024} MB / {maxMem / 1024 / 1024} MB ({memPct:F1}%)";
                }
                else { VmDetailsMemBar.Value = 0; VmDetailsMemText.Text = "—"; }

                if (status.TryGetValue("diskread", out var drObj) && status.TryGetValue("diskwrite", out var dwObj))
                    VmDetailsDiskText.Text = $"R: {FormatBytes(drObj?.ToString())} / W: {FormatBytes(dwObj?.ToString())}";
                else VmDetailsDiskText.Text = "—";

                if (status.TryGetValue("netin", out var niObj) && status.TryGetValue("netout", out var noObj))
                    VmDetailsNetText.Text = $"In: {FormatBytes(niObj?.ToString())} / Out: {FormatBytes(noObj?.ToString())}";
                else VmDetailsNetText.Text = "—";

                // Hardware config
                Dictionary<string, object> config;
                if (isLxc)
                    config = await _proxmoxClient.GetLxcConfigAsync(vm.Node, vm.Id);
                else
                    config = await _proxmoxClient.GetVmConfigAsync(vm.Node, vm.Id);

                var hwLines = new System.Text.StringBuilder();
                string[] hwKeys = { "cores", "sockets", "memory", "balloon", "cpu", "bios", "machine",
                    "rootfs", "net0", "net1", "scsi0", "ide0", "virtio0", "sata0", "ostype" };
                foreach (var key in hwKeys)
                {
                    if (config.TryGetValue(key, out var val) && val != null)
                        hwLines.AppendLine($"{key}: {val}");
                }
                VmDetailsHardwareText.Text = hwLines.Length > 0 ? hwLines.ToString().TrimEnd() : "No hardware info available";

                // Guest agent (VMs only)
                if (!isLxc)
                {
                    var osInfo = await _proxmoxClient.GetVmAgentOsInfoAsync(vm.Node, vm.Id);
                    if (osInfo.Count > 0)
                    {
                        string osName = osInfo.GetValueOrDefault("pretty-name")?.ToString()
                            ?? osInfo.GetValueOrDefault("name")?.ToString() ?? "Unknown OS";
                        string kernel = osInfo.GetValueOrDefault("kernel-version")?.ToString() ?? "";
                        VmDetailsAgentOsText.Text = string.IsNullOrEmpty(kernel) ? osName : $"{osName}\nKernel: {kernel}";
                    }
                    else VmDetailsAgentOsText.Text = "Guest agent not available";

                    var netIfaces = await _proxmoxClient.GetVmAgentNetworkInterfacesAsync(vm.Node, vm.Id);
                    var netLines = new System.Collections.ObjectModel.ObservableCollection<string>();
                    foreach (var iface in netIfaces)
                    {
                        string name = iface.GetValueOrDefault("name")?.ToString() ?? "?";
                        if (name == "lo") continue;
                        var ipAddresses = new List<string>();
                        if (iface.TryGetValue("ip-addresses", out var ipsObj) && ipsObj?.ToString() is string ipsStr)
                        {
                            try
                            {
                                var arr = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(ipsStr);
                                foreach (var ip in arr.EnumerateArray())
                                {
                                    string ipAddr = ip.GetProperty("ip-address").GetString();
                                    string ipType = ip.GetProperty("ip-address-type").GetString();
                                    if (ipType != "ipv6" || !ipAddr.StartsWith("fe80"))
                                        ipAddresses.Add(ipAddr);
                                }
                            }
                            catch { }
                        }
                        string ipDisplay = ipAddresses.Count > 0 ? string.Join(", ", ipAddresses) : "no IP";
                        netLines.Add($"{name}: {ipDisplay}");
                    }
                    VmDetailsAgentNetList.ItemsSource = netLines;
                }
                else
                {
                    VmDetailsAgentOsText.Text = "LXC container";
                    var lxcIfaces = await _proxmoxClient.GetLxcNetworkInterfacesAsync(vm.Node, vm.Id);
                    var netLines = new System.Collections.ObjectModel.ObservableCollection<string>();
                    foreach (var iface in lxcIfaces)
                    {
                        string name = iface.GetValueOrDefault("name")?.ToString() ?? "?";
                        string ip = iface.GetValueOrDefault("inet")?.ToString() ?? iface.GetValueOrDefault("inet6")?.ToString() ?? "no IP";
                        netLines.Add($"{name}: {ip}");
                    }
                    VmDetailsAgentNetList.ItemsSource = netLines;
                }

                // Pending changes (VMs only)
                if (!isLxc)
                {
                    var pending = await _proxmoxClient.GetVmPendingAsync(vm.Node, vm.Id);
                    if (pending != null && pending.Count > 0)
                    {
                        var pendingLines = new System.Text.StringBuilder();
                        foreach (var item in pending)
                        {
                            string key = item.GetValueOrDefault("key")?.ToString() ?? "?";
                            string value = item.GetValueOrDefault("value")?.ToString() ?? "";
                            string pending2 = item.GetValueOrDefault("pending")?.ToString() ?? "";
                            pendingLines.AppendLine($"{key}: {value} → {pending2}");
                        }
                        VmDetailsPendingText.Text = pendingLines.Length > 0 ? pendingLines.ToString().TrimEnd() : "No pending changes";
                    }
                    else VmDetailsPendingText.Text = "No pending changes";
                }
                else VmDetailsPendingText.Text = "Pending config tracking not available for LXC";
            }
            catch (Exception ex)
            {
                ShowStatus($"Error loading VM details: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private string FormatBytes(string bytesStr)
        {
            if (!long.TryParse(bytesStr, out long bytes)) return bytesStr ?? "—";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024:F1} MB";
            return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
        }

        // ──────────────────────────────────────────────────────────────────
        // Performance panel
        // ──────────────────────────────────────────────────────────────────

        private async Task LoadPerformancePanelAsync()
        {
            try
            {
                PerfNodeComboBox.Items.Clear();
                var nodes = await _proxmoxClient.GetNodesAsync();
                foreach (var node in nodes)
                {
                    string name = node.GetValueOrDefault("node")?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(name)) PerfNodeComboBox.Items.Add(name);
                }
                if (PerfNodeComboBox.Items.Count > 0)
                    PerfNodeComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                ShowStatus($"Error loading performance panel: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void PerfNodeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await RefreshPerformanceAsync();
        }

        private async void PerfRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshPerformanceAsync();
        }

        private async Task RefreshPerformanceAsync()
        {
            try
            {
                string node = PerfNodeComboBox.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(node)) return;

                string timeframe = "hour";
                if (PerfTimeframeComboBox.SelectedItem is ComboBoxItem tfi && tfi.Tag?.ToString() is string tf)
                    timeframe = tf;

                var nodeStatus = await _proxmoxClient.GetNodeStatusAsync(node);
                if (nodeStatus != null)
                {
                    if (nodeStatus.TryGetValue("cpu", out var cpuVal) && double.TryParse(cpuVal?.ToString(), out double cpu))
                    {
                        PerfNodeCpuBar.Value = cpu * 100;
                        PerfNodeCpuText.Text = $"{cpu * 100:F1}%";
                    }
                    if (nodeStatus.TryGetValue("memory", out var memVal) && memVal is System.Text.Json.JsonElement memJe)
                    {
                        try
                        {
                            long used = memJe.GetProperty("used").GetInt64();
                            long total = memJe.GetProperty("total").GetInt64();
                            if (total > 0)
                            {
                                double pct = (double)used / total * 100;
                                PerfNodeMemBar.Value = pct;
                                PerfNodeMemText.Text = $"{used / 1024 / 1024} MB / {total / 1024 / 1024} MB ({pct:F1}%)";
                            }
                        }
                        catch { }
                    }
                    PerfNodeDiskText.Text = nodeStatus.GetValueOrDefault("rootfs") != null ? "See storage panel" : "—";
                    PerfNodeNetText.Text = "—";
                }

                // VM metrics list
                var perfItems = new System.Collections.ObjectModel.ObservableCollection<PerfVmItem>();
                var vmsOnNode = _vms.Where(v => v.Node == node).ToList();
                foreach (var vm in vmsOnNode)
                {
                    try
                    {
                        bool isLxc = vm.Type == "lxc";
                        Dictionary<string, object> vmStatus = isLxc
                            ? await _proxmoxClient.GetLxcCurrentStatusAsync(vm.Node, vm.Id)
                            : await _proxmoxClient.GetVmCurrentStatusAsync(vm.Node, vm.Id);

                        double cpuPct = vmStatus.TryGetValue("cpu", out var c) && double.TryParse(c?.ToString(), out double cv) ? cv * 100 : 0;
                        double memPct = 0;
                        string memLabel = "—";
                        if (vmStatus.TryGetValue("mem", out var m) && vmStatus.TryGetValue("maxmem", out var mm)
                            && long.TryParse(m?.ToString(), out long memUsed) && long.TryParse(mm?.ToString(), out long maxMem) && maxMem > 0)
                        {
                            memPct = (double)memUsed / maxMem * 100;
                            memLabel = $"{memPct:F0}%";
                        }
                        perfItems.Add(new PerfVmItem
                        {
                            Name = $"{vm.Name} ({vm.Id})",
                            CpuPercent = cpuPct,
                            CpuLabel = $"{cpuPct:F1}%",
                            MemPercent = memPct,
                            MemLabel = memLabel
                        });
                    }
                    catch { }
                }
                PerfVmListView.ItemsSource = perfItems;
            }
            catch (Exception ex)
            {
                ShowStatus($"Error refreshing performance: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Cluster panel
        // ──────────────────────────────────────────────────────────────────

        private async Task LoadClusterPanelAsync()
        {
            try
            {
                await RefreshClusterAsync();
            }
            catch (Exception ex)
            {
                ShowStatus($"Error loading cluster panel: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void ClusterRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshClusterAsync();
        }

        private async Task RefreshClusterAsync()
        {
            try
            {
                var clusterStatus = await _proxmoxClient.GetClusterStatusAsync();
                var nodeItems = new List<object>();
                var summaryLines = new System.Text.StringBuilder();

                foreach (var item in clusterStatus)
                {
                    string type = item.GetValueOrDefault("type")?.ToString() ?? "";
                    if (type == "node")
                    {
                        nodeItems.Add(new
                        {
                            Name = item.GetValueOrDefault("name")?.ToString() ?? "?",
                            Role = item.GetValueOrDefault("local")?.ToString() == "1" ? "local" : "cluster",
                            Online = item.GetValueOrDefault("online")?.ToString() == "1"
                        });
                    }
                    else if (type == "cluster")
                    {
                        string clName = item.GetValueOrDefault("name")?.ToString() ?? "?";
                        string quorate = item.GetValueOrDefault("quorate")?.ToString();
                        string nodes = item.GetValueOrDefault("nodes")?.ToString() ?? "?";
                        summaryLines.AppendLine($"Cluster: {clName}");
                        summaryLines.AppendLine($"Nodes: {nodes}");
                        summaryLines.AppendLine($"Quorum: {(quorate == "1" ? "OK" : "Lost")}");
                    }
                }
                ClusterNodesListView.ItemsSource = nodeItems;
                ClusterSummaryText.Text = summaryLines.Length > 0 ? summaryLines.ToString().TrimEnd() : "Standalone node (no cluster)";

                var haResources = await _proxmoxClient.GetHaResourcesAsync();
                var haItems = haResources.Select(ha => new
                {
                    Sid = ha.GetValueOrDefault("sid")?.ToString() ?? "?",
                    State = ha.GetValueOrDefault("state")?.ToString() ?? "unknown"
                }).ToList<object>();
                ClusterHaListView.ItemsSource = haItems;
            }
            catch (Exception ex)
            {
                ShowStatus($"Error refreshing cluster: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // ACL & Roles panel
        // ──────────────────────────────────────────────────────────────────

        private async Task LoadAclPanelAsync()
        {
            try
            {
                await RefreshAclAsync();
            }
            catch (Exception ex)
            {
                ShowStatus($"Error loading ACL panel: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void AclRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAclAsync();
        }

        private async Task RefreshAclAsync()
        {
            try
            {
                var acls = await _proxmoxClient.GetAclAsync();
                var aclItems = acls.Select(acl =>
                {
                    string userId = acl.GetValueOrDefault("ugid")?.ToString() ?? "";
                    string type = acl.GetValueOrDefault("type")?.ToString() ?? "";
                    string entity = type == "group" ? $"@{userId}" : userId;
                    bool propagate = acl.GetValueOrDefault("propagate")?.ToString() == "1";
                    return new
                    {
                        Path = acl.GetValueOrDefault("path")?.ToString() ?? "/",
                        Roleid = acl.GetValueOrDefault("roleid")?.ToString() ?? "?",
                        Entity = entity,
                        Propagate = propagate ? "yes" : "no"
                    };
                }).ToList<object>();
                AclListView.ItemsSource = aclItems;

                var roles = await _proxmoxClient.GetRolesAsync();
                var roleItems = roles.Select(r => new
                {
                    Roleid = r.GetValueOrDefault("roleid")?.ToString() ?? "?",
                    Special = r.GetValueOrDefault("special")?.ToString() == "1" ? "built-in" : "custom"
                }).ToList<object>();
                RolesListView.ItemsSource = roleItems;
            }
            catch (Exception ex)
            {
                ShowStatus($"Error refreshing ACL: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void AddAclButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Add ACL Rule",
                    PrimaryButtonText = "Add",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };
                var pathBox = new TextBox { Header = "Path", PlaceholderText = "/" };
                var roleBox = new TextBox { Header = "Role ID", PlaceholderText = "PVEVMAdmin" };
                var userBox = new TextBox { Header = "User/Group ID", PlaceholderText = "user@pam" };
                var stack = new StackPanel { Spacing = 8 };
                stack.Children.Add(pathBox);
                stack.Children.Add(roleBox);
                stack.Children.Add(userBox);
                dialog.Content = stack;
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    bool ok = await _proxmoxClient.UpdateAclAsync(pathBox.Text, roleBox.Text, userBox.Text, null, true, false);
                    if (ok) await RefreshAclAsync();
                    else ShowStatus("Failed to add ACL rule", InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Error adding ACL rule: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void DeleteAclButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AclListView.SelectedItem == null)
                {
                    ShowStatus("Select an ACL rule to delete", InfoBarSeverity.Warning);
                    return;
                }
                dynamic item = AclListView.SelectedItem;
                bool ok = await _proxmoxClient.UpdateAclAsync(item.Path, item.Roleid, item.Entity, null, true, true);
                if (ok) await RefreshAclAsync();
                else ShowStatus("Failed to delete ACL rule", InfoBarSeverity.Error);
            }
            catch (Exception ex)
            {
                ShowStatus($"Error deleting ACL rule: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Replication panel
        // ──────────────────────────────────────────────────────────────────

        private async Task LoadReplicationPanelAsync()
        {
            try
            {
                await RefreshReplicationAsync();
            }
            catch (Exception ex)
            {
                ShowStatus($"Error loading replication panel: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void ReplRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshReplicationAsync();
        }

        private async Task RefreshReplicationAsync()
        {
            try
            {
                var jobs = await _proxmoxClient.GetReplicationJobsAsync();
                var items = jobs.Select(j =>
                {
                    bool enabled = j.GetValueOrDefault("disable")?.ToString() != "1";
                    return new ReplJobItem
                    {
                        Id = j.GetValueOrDefault("id")?.ToString() ?? "?",
                        Target = j.GetValueOrDefault("target")?.ToString() ?? "?",
                        Type = j.GetValueOrDefault("type")?.ToString() ?? "?",
                        Schedule = j.GetValueOrDefault("schedule")?.ToString() ?? "*/15",
                        EnabledLabel = enabled ? "Enabled" : "Disabled"
                    };
                }).ToList();
                ReplJobsListView.ItemsSource = items;
            }
            catch (Exception ex)
            {
                ShowStatus($"Error refreshing replication jobs: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void AddReplJobButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Add Replication Job",
                    PrimaryButtonText = "Create",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };
                var idBox = new TextBox { Header = "Job ID (vmid-index, e.g. 100-0)", PlaceholderText = "100-0" };
                var targetBox = new TextBox { Header = "Target Node", PlaceholderText = "node2" };
                var scheduleBox = new TextBox { Header = "Schedule (cron)", PlaceholderText = "*/15" };
                var stack = new StackPanel { Spacing = 8 };
                stack.Children.Add(idBox);
                stack.Children.Add(targetBox);
                stack.Children.Add(scheduleBox);
                dialog.Content = stack;
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    string schedule = string.IsNullOrWhiteSpace(scheduleBox.Text) ? "*/15" : scheduleBox.Text;
                    bool ok = await _proxmoxClient.CreateReplicationJobAsync(idBox.Text, targetBox.Text, "local", schedule, true);
                    if (ok) await RefreshReplicationAsync();
                    else ShowStatus("Failed to create replication job", InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Error adding replication job: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void DeleteReplJobButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ReplJobsListView.SelectedItem is not ReplJobItem job)
                {
                    ShowStatus("Select a replication job to delete", InfoBarSeverity.Warning);
                    return;
                }
                bool ok = await _proxmoxClient.DeleteReplicationJobAsync(job.Id);
                if (ok) await RefreshReplicationAsync();
                else ShowStatus("Failed to delete replication job", InfoBarSeverity.Error);
            }
            catch (Exception ex)
            {
                ShowStatus($"Error deleting replication job: {ex.Message}", InfoBarSeverity.Error);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helper data models
    // ──────────────────────────────────────────────────────────────────────

    public class SnapshotVmEntry
    {
        public string DisplayName { get; set; }
        public ProxMachine Vm { get; set; }
    }

    public class SnapshotItem
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Date { get; set; }
    }

    public class BackupItem
    {
        public string VolId { get; set; }
        public string Storage { get; set; }
        public string Node { get; set; }
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string SizeFormatted => SizeBytes > 1_073_741_824
            ? $"{SizeBytes / 1_073_741_824.0:F1} GB"
            : $"{SizeBytes / 1_048_576.0:F0} MB";
        public string DateFormatted => CreatedAt == DateTime.MinValue ? "" : CreatedAt.ToString("g");
    }

    public class TaskItem
    {
        public string Upid { get; set; }
        public string Type { get; set; }
        public string Id { get; set; }
        public string Status { get; set; }
        public string Node { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Duration
        {
            get
            {
                var end = EndTime ?? DateTime.Now;
                var span = end - StartTime;
                return span.TotalSeconds < 60 ? $"{(int)span.TotalSeconds}s"
                    : span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
                    : $"{(int)span.TotalHours}h {span.Minutes}m";
            }
        }
        public SolidColorBrush StatusColor => Status?.ToLower() switch
        {
            "ok" => new SolidColorBrush(Color.FromArgb(255, 34, 197, 94)),
            "error" => new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)),
            _ => new SolidColorBrush(Color.FromArgb(255, 234, 179, 8)),
        };
    }

    public class FirewallRuleItem
    {
        public string Pos { get; set; }
        public string Action { get; set; }
        public string Type { get; set; }
        public string Source { get; set; }
        public string Dest { get; set; }
        public string Comment { get; set; }
        public bool Enabled { get; set; }
        public SolidColorBrush ActionBrush => Action?.ToUpper() switch
        {
            "ACCEPT" => new SolidColorBrush(Color.FromArgb(40, 34, 197, 94)),
            "DROP" => new SolidColorBrush(Color.FromArgb(40, 239, 68, 68)),
            "REJECT" => new SolidColorBrush(Color.FromArgb(40, 234, 179, 8)),
            _ => new SolidColorBrush(Colors.Transparent),
        };
        public SolidColorBrush EnabledBrush => Enabled
            ? new SolidColorBrush(Color.FromArgb(255, 34, 197, 94))
            : new SolidColorBrush(Color.FromArgb(255, 156, 163, 175));
    }



    public class PerfVmItem
    {
        public string Name { get; set; }
        public double CpuPercent { get; set; }
        public string CpuLabel { get; set; }
        public double MemPercent { get; set; }
        public string MemLabel { get; set; }
    }

    public class ReplJobItem
    {
        public string Id { get; set; }
        public string Target { get; set; }
        public string Type { get; set; }
        public string Schedule { get; set; }
        public string EnabledLabel { get; set; }
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
        public string Type { get; set; } = "qemu";

        public ProxMachine(Dictionary<string, object> vmData, string type = "qemu")
        {
            Type = type;
            Id = vmData["vmid"].ToString();
            Status = vmData["status"].ToString();
            Node = vmData.TryGetValue("node", out var nodeVal) ? nodeVal.ToString() : "";

            if (type == "lxc")
                Name = vmData.TryGetValue("name", out var lxcName) ? lxcName.ToString() : $"CT-{Id}";
            else
                Name = vmData.TryGetValue("name", out var vmName) ? vmName.ToString() : $"VM-{Id}";

            if (vmData.TryGetValue("cpus", out object cpusObj))
            {
                if (cpusObj is int cpus) VCPUs = cpus;
                else if (int.TryParse(cpusObj?.ToString(), out int parsedCpus)) VCPUs = parsedCpus;
            }

            if (vmData.TryGetValue("maxmem", out object maxmemObj))
            {
                if (maxmemObj is long maxmem) RAMInBytes = maxmem;
                else if (long.TryParse(maxmemObj?.ToString(), out long parsedMem)) RAMInBytes = parsedMem;
            }

            if (vmData.TryGetValue("uptime", out object uptimeObj))
            {
                if (uptimeObj is long uptimeSeconds) Uptime = TimeSpan.FromSeconds(uptimeSeconds);
                else if (long.TryParse(uptimeObj?.ToString(), out long parsedUptime)) Uptime = TimeSpan.FromSeconds(parsedUptime);
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

    public class VmConfig : INotifyPropertyChanged
    {
        private string _id;
        private string _name;
        private int _cpuCores = 1;
        private int _memoryMB = 512;
        private int _diskSizeGB = 8;
        private string _osType = "Other";
        private bool _useUefi;
        private bool _startOnBoot;
        private bool _enableQemuGuestAgent = true;
        private string _tags;
        private string _description;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public int CpuCores
        {
            get => _cpuCores;
            set { _cpuCores = value; OnPropertyChanged(); }
        }

        public int MemoryMB
        {
            get => _memoryMB;
            set { _memoryMB = value; OnPropertyChanged(); }
        }

        public int DiskSizeGB
        {
            get => _diskSizeGB;
            set { _diskSizeGB = value; OnPropertyChanged(); }
        }

        public string OsType
        {
            get => _osType;
            set { _osType = value; OnPropertyChanged(); }
        }

        public bool UseUefi
        {
            get => _useUefi;
            set { _useUefi = value; OnPropertyChanged(); }
        }

        public bool StartOnBoot
        {
            get => _startOnBoot;
            set { _startOnBoot = value; OnPropertyChanged(); }
        }

        public bool EnableQemuGuestAgent
        {
            get => _enableQemuGuestAgent;
            set { _enableQemuGuestAgent = value; OnPropertyChanged(); }
        }

        public string Tags
        {
            get => _tags;
            set { _tags = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
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

    public class UserItem : INotifyPropertyChanged
    {
        private string _userId;
        private bool _isSelected;

        public string UserId
        {
            get => _userId;
            set
            {
                _userId = value;
                OnPropertyChanged();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class GroupItem : INotifyPropertyChanged
    {
        private string _groupId;
        private bool _isSelected;

        public string GroupId
        {
            get => _groupId;
            set
            {
                _groupId = value;
                OnPropertyChanged();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class VMItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public int Id { get; set; }  // Changed from string to int
        public string Name { get; set; }
        public string Node { get; set; }

        public string Status { get; set; }
        public int CpuCount { get; set; }
        public long MemoryMB { get; set; }
        public long DiskGB { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class MainPageViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<UserItem> _selectedUsers;
        private string _selectedGroup;
        private bool _isLoading;

        public ObservableCollection<UserItem> Users { get; } = new ObservableCollection<UserItem>();
        public ObservableCollection<string> Groups { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AvailableGroups { get; } = new ObservableCollection<string>();

        public ObservableCollection<UserItem> SelectedUsers
        {
            get => _selectedUsers;
            set
            {
                _selectedUsers = value;
                OnPropertyChanged();
            }
        }

        public string SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                _selectedGroup = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public MainPageViewModel()
        {
            SelectedUsers = new ObservableCollection<UserItem>();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

