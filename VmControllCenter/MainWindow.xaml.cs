using System;
using System.Threading.Tasks;
using EsxiApiHelper;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProxmoxApiHelper;
using Windows.Graphics;
using Windows.Storage;

namespace VmControllCenter
{
    public sealed partial class MainWindow : Window
    {
        private ProxmoxClient proxmoxClient;
        private AppWindow appWindow;
        private ESXiClient esxi;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "VM Control Center - Login";

            SetWindowSize(675, 985);
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

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string platform = (PlatformComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            string url = UrlTextBox.Text.TrimEnd('/');
            string username = UsernameTextBox.Text;
            string password = PasswordBox.Password;

            if (string.IsNullOrEmpty(platform) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                StatusTextBlock.Text = "Please fill in all fields.";
                return;
            }


            StatusTextBlock.Text = "Logging in...";
            LoginButton.IsEnabled = false;
            DisableInputs(false);

            try
            {
                if (platform == "Proxmox")
                {
                    // Create new client instance for each login attempt
                    if (proxmoxClient != null)
                    {
                        proxmoxClient = null;
                    }

                    await InitializeProxmox(url, username, password);
                    await NavigateToDashboard();
                }
                else if (platform == "ESXi")
                {
                    InitializeEsxi(url, username, password);
                    await NavigateToDashboard();
                }
            }
            catch (Exception ex)
            {
                var baseException = ex.GetBaseException();
                StatusTextBlock.Text = $"Login failed: {baseException.Message}";
                DisableInputs(true);
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }

        private async Task InitializeEsxi(string url, string username, string password)
        {
            try
            {
                ESXiClient eSXiClient = new ESXiClient(url,username,password);
                esxi = eSXiClient;
                eSXiClient.InitializeAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to connect to Esxi server: {ex.Message}", ex);
            }
        }

        private async Task InitializeProxmox(string url, string username, string password)
        {
            try
            {
                proxmoxClient = new ProxmoxClient(url, username, password, "pam");
                await proxmoxClient.InitializeAsync();
                StatusTextBlock.Text = "Login successful! Loading dashboard...";
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to connect to Proxmox server: {ex.Message}", ex);
            }
        }

        private async Task NavigateToDashboard()
        {
            try
            {
                string platform = (PlatformComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();

                if (platform == "Proxmox")
                {
                    // Create and show the dashboard window
                    var dashboardWindow = new WindowProxmox(proxmoxClient);
                    dashboardWindow.Activate();
                }
                else if (platform == "ESXi")
                {
                    // Create and show the dashboard window
                    var dashboardWindow = new DashboardEsxi(esxi); 
                    dashboardWindow.Activate();
                }
                   

                // Close the login window
                this.Close();
            }
            catch (Exception ex)
            {
                if (proxmoxClient != null)
                {
                    proxmoxClient = null;
                }
                throw new Exception($"Failed to open dashboard: {ex.Message}", ex);
            }
        }

        private void DisableInputs(bool enabled)
        {
            PlatformComboBox.IsEnabled = enabled;
            UrlTextBox.IsEnabled = enabled;
            UsernameTextBox.IsEnabled = enabled;
            PasswordBox.IsEnabled = enabled;
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            if (proxmoxClient != null)
            {
                proxmoxClient = null;
            }
        }
    }
}