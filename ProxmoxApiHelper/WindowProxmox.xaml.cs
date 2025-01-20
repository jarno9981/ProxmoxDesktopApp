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
            ConsoleViewerProxmox.IsEnabled = false;
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
            UsersGroups.Visibility = Visibility.Collapsed;
            ManagePoolsPanel.Visibility = Visibility.Collapsed;

            StopServerStatsTimer();
         

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
                case "grps_config":
                    UsersGroups.Visibility = Visibility.Visible;
                    break;
                case "server_stats":
                    ServerStatsPanelProxmox.Visibility = Visibility.Visible;
                    LoadServerStats(); // Initial load
                    StartServerStatsTimer();
                    break;
                case "manage_pools":
                    ManagePoolsPanel.Visibility = Visibility.Visible;
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
            if (GroupsListView.SelectedItem is GroupItem selectedGroup)
            {
                var editGroupWindow = new EditGroupWindow(_proxmoxClient, selectedGroup.GroupId);
                editGroupWindow.Activate();
            }
        }

        private async void DeleteGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedGroup != null)
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "Confirm Delete",
                    Content = $"Are you sure you want to delete group {ViewModel.SelectedGroup}?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        await _proxmoxClient.DeleteGroupAsync(ViewModel.SelectedGroup);
                        await LoadUsersAndGroups();
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorDialog("Delete Group Error", $"Failed to delete group: {ex.Message}");
                    }
                }
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

        private string GetNextVMID()
        {
            // Use lock to ensure thread-safe random number generation
            lock (_lock)
            {
                return _random.Next(250, 1000).ToString("D3");
            }
        }
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
                        string vmId = GetNextVMID();
                        string vmName = $"VM-{GenerateRandomLetters(5)}";

                        var result = await _proxmoxClient.CreateVMAsync(
                            // Required parameters
                            node: MassVmNodeSelectionComboBox.SelectedItem.ToString(),
                            vmid: int.Parse(vmId),
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
            if (e.ClickedItem is GroupItem clickedGroup)
            {
                // Toggle the IsSelected property
                clickedGroup.IsSelected = !clickedGroup.IsSelected;
                clickedGroup.GroupId = clickedGroup.GroupId;
                // You can add additional logic here if needed
                // For example, you might want to update some UI elements or perform some action
                // based on the group selection change
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

