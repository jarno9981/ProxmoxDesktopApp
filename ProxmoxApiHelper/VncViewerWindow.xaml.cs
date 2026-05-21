using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Windows.Graphics;
using AppWindow = Microsoft.UI.Windowing.AppWindow;
using Microsoft.UI;

namespace ProxmoxApiHelper
{
    public sealed partial class VncViewerWindow : Window, IDisposable
    {
        private readonly ProxmoxClient _proxmoxClient;
        private readonly string _node;
        private readonly string _vmid;
        private readonly bool _isLxc;
        private bool _isConnected;
        private string _novncUrl;

        public VncViewerWindow(ProxmoxClient proxmoxClient, string node, string vmid, bool isLxc = false)
        {
            this.InitializeComponent();
            _proxmoxClient = proxmoxClient;
            _node = node;
            _vmid = vmid;
            _isLxc = isLxc;

            string vmType = isLxc ? "LXC" : "VM";
            Title = $"{vmType} Console — {_vmid} on {_node}";
            VmTitleTextBlock.Text = $"{vmType} {_vmid} — {_node}";

            SetWindowSize(1280, 820);
            InitializeAsync();
        }

        private void SetWindowSize(int width, int height)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new SizeInt32(width, height));
        }

        private async void InitializeAsync()
        {
            try
            {
                UpdateStatus("Initializing console...");
                await VncWebView.EnsureCoreWebView2Async();
                VncWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                VncWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                VncWebView.CoreWebView2.Settings.IsWebMessageEnabled = false;
                VncWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                await ConnectConsoleAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Initialization error: {ex.Message}");
                ConnectionProgressRing.IsActive = false;
            }
        }

        private async Task ConnectConsoleAsync()
        {
            try
            {
                UpdateStatus("Requesting VNC proxy ticket...");
                ConnectionProgressRing.IsActive = true;

                ProxmoxClient.VncProxyResult proxy;
                if (_isLxc)
                    proxy = await _proxmoxClient.GetLxcVncProxyAsync(_node, _vmid);
                else
                    proxy = await _proxmoxClient.GetVmVncProxyAsync(_node, _vmid);

                if (proxy == null || string.IsNullOrEmpty(proxy.Ticket))
                {
                    UpdateStatus("Failed to obtain VNC ticket from Proxmox");
                    ConnectionProgressRing.IsActive = false;
                    return;
                }

                var baseUri = new Uri(_proxmoxClient.ApiUrl);
                string host = baseUri.Host;
                int webPort = baseUri.Port > 0 ? baseUri.Port : 8006;
                string vmType = _isLxc ? "lxc" : "qemu";
                string wsPath = $"api2/json/nodes/{_node}/{vmType}/{_vmid}/vncwebsocket";

                string encodedTicket = Uri.EscapeDataString(proxy.Ticket);
                string encodedPath = Uri.EscapeDataString(wsPath);

                _novncUrl = $"https://{host}:{webPort}/novnc/vnc.html" +
                    $"?path={encodedPath}" +
                    $"&port={proxy.Port}" +
                    $"&vncticket={encodedTicket}" +
                    $"&autoconnect=true" +
                    $"&resize=scale";

                VncWebView.Source = new Uri(_novncUrl);
                UpdateStatus($"Connecting to {(_isLxc ? "LXC" : "VM")} {_vmid} console...");
            }
            catch (Exception ex)
            {
                ConnectionProgressRing.IsActive = false;
                UpdateStatus($"Connection error: {ex.Message}");
            }
        }

        private void OnNavigationCompleted(Microsoft.Web.WebView2.Core.CoreWebView2 sender,
            Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ConnectionProgressRing.IsActive = false;
                if (args.IsSuccess)
                {
                    _isConnected = true;
                    UpdateStatus($"Console ready — {(_isLxc ? "LXC" : "VM")} {_vmid} on {_node}");
                    ResolutionTextBlock.Text = "noVNC";
                }
                else
                {
                    UpdateStatus($"Failed to load console (error {args.WebErrorStatus})");
                }
                UpdateButtonStates();
            });
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_novncUrl))
            {
                ConnectionProgressRing.IsActive = true;
                UpdateStatus("Reloading...");
                VncWebView.Reload();
            }
            else
            {
                _ = ConnectConsoleAsync();
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void SendCtrlAltDelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isConnected && VncWebView.CoreWebView2 != null)
                {
                    await VncWebView.CoreWebView2.ExecuteScriptAsync(
                        "document.querySelector('canvas')?.dispatchEvent(new KeyboardEvent('keydown',{key:'Delete',ctrlKey:true,altKey:true}));");
                }
            }
            catch { }
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        private void UpdateResolution(int width, int height)
        {
            ResolutionTextBlock.Text = $"{width}×{height}";
        }

        private void UpdateButtonStates()
        {
            DisconnectButton.IsEnabled = true;
            SendCtrlAltDelButton.IsEnabled = _isConnected;
        }

        public void Dispose()
        {
            VncWebView?.Close();
        }
    }
}
