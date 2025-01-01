using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Data;
using System.Collections.Generic;
using Microsoft.UI;

namespace EsxiApiHelper
{
    public sealed partial class DashboardEsxi : Window
    {
        private readonly ESXiClient _client;
        private ObservableCollection<ESXiVirtualMachine> _vms;
        private ESXiVirtualMachine _selectedVm;

        public DashboardEsxi(ESXiClient client)
        {
            this.InitializeComponent();
            _client = client;
            _vms = new ObservableCollection<ESXiVirtualMachine>();
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                await RefreshVMList();
                SetupEventHandlers();
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Initialization Error", $"Failed to initialize: {ex.Message}");
            }
        }

        private void SetupEventHandlers()
        {
            RefreshButtonEsxi.Click += async (s, e) => await RefreshVMList();
            VmListViewEsxi.SelectionChanged += VmListViewEsxi_SelectionChanged;
            VmListViewEsxi.ItemClick += VmListViewEsxi_ItemClick;
            StartButtonEsxi.Click += async (s, e) => await PerformVmAction("powered_on");
            StopButtonEsxi.Click += async (s, e) => await PerformVmAction("powered_off");
            ResetButtonEsxi.Click += async (s, e) => await PerformVmAction("reset");
            ShutdownButtonEsxi.Click += async (s, e) => await PerformVmAction("shutdown");
            EditButtonEsxi.Click += EditButtonEsxi_Click;
            ConsoleViewerEsxi.Click += ConsoleViewerEsxi_Click;
            CreateVmButtonEsxi.Click += CreateVmButtonEsxi_Click;
            ConfigureNetworkButtonEsxi.Click += ConfigureNetworkButtonEsxi_Click;
            NavViewEsxi.SelectionChanged += NavViewEsxi_SelectionChanged;
        }

        private async Task RefreshVMList()
        {
            try
            {
                LoadingIndicatorEsxi.IsActive = true;
                var vms = await _client.GetVirtualMachinesAsync();
                _vms.Clear();
                foreach (var vm in vms)
                {
                    _vms.Add(new ESXiVirtualMachine(vm));
                }
                VmListViewEsxi.ItemsSource = _vms;
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Refresh Error", $"Failed to refresh VM list: {ex.Message}");
            }
            finally
            {
                LoadingIndicatorEsxi.IsActive = false;
            }
        }

        private void VmListViewEsxi_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedVm = VmListViewEsxi.SelectedItem as ESXiVirtualMachine;
            UpdateVmDetails();
        }

        private void VmListViewEsxi_ItemClick(object sender, ItemClickEventArgs e)
        {
            _selectedVm = e.ClickedItem as ESXiVirtualMachine;
            UpdateVmDetails();
        }

        private void UpdateVmDetails()
        {
            if (_selectedVm != null)
            {
                VmDetailsTextBlockEsxi.Text = $"Name: {_selectedVm.Name}\nStatus: {_selectedVm.Status}\nHost: {_selectedVm.Host}";
                EnableVmActionButtons();
            }
            else
            {
                VmDetailsTextBlockEsxi.Text = "Select a VM to view details";
                DisableVmActionButtons();
            }
        }

        private void EnableVmActionButtons()
        {
            StartButtonEsxi.IsEnabled = _selectedVm.Status.ToLower() != "powered_on";
            StopButtonEsxi.IsEnabled = _selectedVm.Status.ToLower() == "powered_on";
            ResetButtonEsxi.IsEnabled = _selectedVm.Status.ToLower() == "powered_on";
            ShutdownButtonEsxi.IsEnabled = _selectedVm.Status.ToLower() == "powered_on";
            EditButtonEsxi.IsEnabled = true;
            ConsoleViewerEsxi.IsEnabled = _selectedVm.Status.ToLower() == "powered_on";
        }

        private void DisableVmActionButtons()
        {
            StartButtonEsxi.IsEnabled = false;
            StopButtonEsxi.IsEnabled = false;
            ResetButtonEsxi.IsEnabled = false;
            ShutdownButtonEsxi.IsEnabled = false;
            EditButtonEsxi.IsEnabled = false;
            ConsoleViewerEsxi.IsEnabled = false;
        }

        private async Task PerformVmAction(string action)
        {
            if (_selectedVm != null)
            {
                try
                {
                    StatusInfoBarEsxi.Message = $"Performing {action} operation...";
                    StatusInfoBarEsxi.IsOpen = true;
                    bool success = await _client.PowerOperationAsync(_selectedVm.Id, action);
                    if (success)
                    {
                        await RefreshVMList();
                        StatusInfoBarEsxi.Message = $"VM {action} operation successful.";
                        StatusInfoBarEsxi.Severity = InfoBarSeverity.Success;
                    }
                    else
                    {
                        throw new Exception($"Failed to perform {action} operation");
                    }
                }
                catch (Exception ex)
                {
                    StatusInfoBarEsxi.Message = $"Failed to perform {action} operation: {ex.Message}";
                    StatusInfoBarEsxi.Severity = InfoBarSeverity.Error;
                }
                finally
                {
                    StatusInfoBarEsxi.IsOpen = true;
                }
            }
        }

        private async void EditButtonEsxi_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm != null)
            {
                await ShowErrorDialog("Not Implemented", "VM editing is not yet implemented.");
            }
        }

        private async void ConsoleViewerEsxi_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm != null)
            {
                await ShowErrorDialog("Not Implemented", "Console viewer is not yet implemented.");
            }
        }

        private async void CreateVmButtonEsxi_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var createSpec = new Dictionary<string, object>
                {
                    ["name"] = NewVmNameTextBoxEsxi.Text,
                    ["guest_os"] = (OsTypeComboBoxEsxi.SelectedItem as ComboBoxItem)?.Content.ToString(),
                    ["cpu"] = new Dictionary<string, object>
                    {
                        ["count"] = int.Parse(NewVmCpuTextBoxEsxi.Text)
                    },
                    ["memory"] = new Dictionary<string, object>
                    {
                        ["size_MiB"] = int.Parse(NewVmMemoryTextBoxEsxi.Text)
                    },
                    ["disks"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["new_vmdk"] = new Dictionary<string, object>
                            {
                                ["capacity"] = long.Parse(NewVmDiskTextBoxEsxi.Text) * 1024 * 1024 * 1024
                            }
                        }
                    }
                };

                string vmId = await _client.CreateVmAsync(createSpec);
                await RefreshVMList();
                StatusInfoBarEsxi.Message = $"VM created successfully with ID: {vmId}";
                StatusInfoBarEsxi.Severity = InfoBarSeverity.Success;
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Create VM Error", $"Failed to create VM: {ex.Message}");
            }
        }

        private async void ConfigureNetworkButtonEsxi_Click(object sender, RoutedEventArgs e)
        {
            await ShowErrorDialog("Not Implemented", "Network configuration is not yet implemented.");
        }

        private void NavViewEsxi_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer != null)
            {
                string navItemTag = args.SelectedItemContainer.Tag.ToString();
                NavViewEsxi_Navigate(navItemTag);
            }
        }

        private void NavViewEsxi_Navigate(string navItemTag)
        {
            DashboardContentEsxi.Visibility = Visibility.Collapsed;
            CreateVmPanelEsxi.Visibility = Visibility.Collapsed;
            NetworkConfigPanelEsxi.Visibility = Visibility.Collapsed;
            ServerStatsPanelEsxi.Visibility = Visibility.Collapsed;

            switch (navItemTag)
            {
                case "dashboard":
                    DashboardContentEsxi.Visibility = Visibility.Visible;
                    break;
                case "create_vm":
                    CreateVmPanelEsxi.Visibility = Visibility.Visible;
                    break;
                case "network_config":
                    NetworkConfigPanelEsxi.Visibility = Visibility.Visible;
                    break;
                case "server_stats":
                    ServerStatsPanelEsxi.Visibility = Visibility.Visible;
                    break;
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
    }

    public class ESXiVirtualMachine
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public string Host { get; set; }

        public ESXiVirtualMachine(Dictionary<string, object> vmData)
        {
            Id = vmData["vm"].ToString();
            Name = vmData["name"].ToString();
            Status = vmData["power_state"].ToString();
            Host = vmData["host"].ToString();
        }
    }

    public class ServerStatEsxi
    {
        public string HostNameEsxi { get; set; }
        public double CpuUsageEsxi { get; set; }
        public string CpuUsageTextEsxi { get; set; }
        public double MemoryUsageEsxi { get; set; }
        public string MemoryUsageTextEsxi { get; set; }
    }

    public class ConverterEsxi : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string status)
            {
                return status.ToLower() switch
                {
                    "powered_on" => new SolidColorBrush(Colors.Green),
                    "powered_off" => new SolidColorBrush(Colors.Red),
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

