using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics;
using AppWindow = Microsoft.UI.Windowing.AppWindow;

namespace ProxmoxApiHelper
{
    public sealed partial class VncViewerWindow : Window, IDisposable
    {
        private readonly ProxmoxClient _proxmoxClient;
        private readonly string _node;
        private readonly string _vmid;
        private readonly bool _isLxc;
        private string _consoleUrl;
        private bool _certTrustEstablished;

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

                // Create a WebView2 environment with --ignore-certificate-errors so that
                // Proxmox's self-signed certificates are accepted without prompting.
                string cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ProxmoxDesktopApp", "WebView2Cache");
                Directory.CreateDirectory(cacheDir);

                var opts = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = "--ignore-certificate-errors"
                };
                var env = await CoreWebView2Environment.CreateAsync(null, cacheDir, opts);
                await VncWebView.EnsureCoreWebView2Async(env);

                // Belt-and-suspenders: also accept certs via the event API.
                // Per docs, NavigationCompleted fires with IsSuccess=false BEFORE this
                // event, so we suppress that false-failure in OnNavigationCompleted.
                VncWebView.CoreWebView2.ServerCertificateErrorDetected += (s, e) =>
                {
                    e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
                    _certTrustEstablished = true;
                };

                VncWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                VncWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                VncWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                await ConnectConsoleAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Init error: {ex.Message}");
                ConnectionProgressRing.IsActive = false;
            }
        }

        private async Task ConnectConsoleAsync()
        {
            try
            {
                UpdateStatus("Connecting to Proxmox console...");
                ConnectionProgressRing.IsActive = true;
                _certTrustEstablished = false;

                var baseUri = new Uri(_proxmoxClient.ApiUrl);
                string host = baseUri.Host;
                int port = baseUri.Port > 0 ? baseUri.Port : 8006;

                // Inject PVEAuthCookie so the Proxmox web UI accepts the session
                // without showing a login page.
                string authCookie = _proxmoxClient.GetAuthCookie();
                if (!string.IsNullOrEmpty(authCookie))
                {
                    var cm = VncWebView.CoreWebView2.CookieManager;
                    var c = cm.CreateCookie("PVEAuthCookie", authCookie, host, "/");
                    c.IsSecure = true;
                    cm.AddOrUpdateCookie(c);
                }

                // ?console=kvm/lxc makes the Proxmox web UI render ONLY the VNC/console
                // panel — this is the same URL Proxmox opens in its own popup windows.
                // It uses the noVNC client bundled inside the Proxmox web app, so it works
                // regardless of whether /usr/share/novnc-pve exists on the server.
                string consoleType = _isLxc ? "lxc" : "kvm";
                _consoleUrl = $"https://{host}:{port}/?console={consoleType}" +
                              $"&vmid={_vmid}&node={_node}&resize=off&cmd=";

                VncWebView.Source = new Uri(_consoleUrl);
            }
            catch (Exception ex)
            {
                ConnectionProgressRing.IsActive = false;
                UpdateStatus($"Connection error: {ex.Message}");
            }
        }

        private void OnNavigationCompleted(CoreWebView2 sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ConnectionProgressRing.IsActive = false;

                if (args.IsSuccess)
                {
                    UpdateStatus(
                        $"{(_isLxc ? "LXC" : "VM")} {_vmid} — {_node}  |  Proxmox Console");
                    ResolutionTextBlock.Text = "noVNC";
                    UpdateButtonStates();
                }
                else
                {
                    // Per WebView2 docs: NavigationCompleted fires IsSuccess=false BEFORE
                    // ServerCertificateErrorDetected. If cert trust is pending, retry once
                    // the cert event handler approves it (it auto-retries). Otherwise report.
                    bool isCertError =
                        args.WebErrorStatus == CoreWebView2WebErrorStatus.CertificateIsInvalid ||
                        args.WebErrorStatus == CoreWebView2WebErrorStatus.CertificateExpired ||
                        args.WebErrorStatus == CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect ||
                        args.WebErrorStatus == CoreWebView2WebErrorStatus.CertificateRevoked;

                    if (!isCertError)
                    {
                        UpdateStatus($"Navigation failed: {args.WebErrorStatus}");
                        UpdateButtonStates();
                    }
                    // cert errors are handled by ServerCertificateErrorDetected → auto-retry
                }
            });
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectionProgressRing.IsActive = true;
            _ = ConnectConsoleAsync();
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void SendCtrlAltDelButton_Click(object sender, RoutedEventArgs e)
        {
            if (VncWebView.CoreWebView2 == null) return;
            try
            {
                // Try the Proxmox/noVNC toolbar "Send Ctrl+Alt+Del" button first,
                // then fall back to dispatching key events directly to the VNC canvas.
                await VncWebView.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        var btn = document.querySelector(
                            '[data-action=""ctrl-alt-del""], ' +
                            'button[title*=""Ctrl""], ' +
                            '.x-btn[data-tip*=""Ctrl""]');
                        if (btn) { btn.click(); return; }
                        var canvas = document.querySelector('canvas');
                        if (!canvas) return;
                        canvas.focus();
                        var mk = function(type, key, code, kc, ctrl, alt) {
                            canvas.dispatchEvent(new KeyboardEvent(type, {
                                key: key, code: code, keyCode: kc,
                                ctrlKey: ctrl, altKey: alt, bubbles: true, cancelable: true
                            }));
                        };
                        mk('keydown','Control','ControlLeft',17,false,false);
                        mk('keydown','Alt','AltLeft',18,true,false);
                        mk('keydown','Delete','Delete',46,true,true);
                        mk('keyup','Delete','Delete',46,true,true);
                        mk('keyup','Alt','AltLeft',18,true,false);
                        mk('keyup','Control','ControlLeft',17,false,false);
                    })();
                ");
            }
            catch { }
        }

        private void UpdateStatus(string message) => StatusTextBlock.Text = message;
        private void UpdateResolution(int width, int height) =>
            ResolutionTextBlock.Text = $"{width}×{height}";

        private void UpdateButtonStates()
        {
            DisconnectButton.IsEnabled = true;
            SendCtrlAltDelButton.IsEnabled = true;
        }

        public void Dispose() => VncWebView?.Close();
    }
}
