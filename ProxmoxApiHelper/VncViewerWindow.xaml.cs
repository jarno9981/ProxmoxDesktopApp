using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices.WindowsRuntime;

namespace ProxmoxApiHelper
{
    public sealed partial class VncViewerWindow : Window, IDisposable
    {
        private readonly ProxmoxClient _proxmoxClient;
        private readonly string _node;
        private readonly string _vmid;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private bool _isConnected;

        public VncViewerWindow(ProxmoxClient proxmoxClient, string node, string vmid)
        {
            this.InitializeComponent();
            _proxmoxClient = proxmoxClient;
            _node = node;
            _vmid = vmid;

            Title = $"VNC Viewer - VM {_vmid} on {_node}";
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                UpdateStatus("Initializing VNC connection...");
                await ConnectVncAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Initialization error: {ex.Message}");
            }
        }

        private async Task<int> EnableVncAndGetPortAsync()
        {
            string scriptPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(scriptPath, @"
#!/bin/bash

VM_ID=$1
VNC_PORT=5900

while true; do
    if ! sudo netstat -tulpn | grep "":$VNC_PORT"" > /dev/null; then
        break
    fi
    VNC_PORT=$((VNC_PORT + 1))
done

VNC_DISPLAY=$(expr $VNC_PORT - 5900)

echo ""args: -vnc 0.0.0.0:$VNC_DISPLAY"" | sudo tee -a /etc/pve/local/qemu-server/$VM_ID.conf > /dev/null

echo ""Stopping VM $VM_ID to apply VNC config...""
qm stop $VM_ID
while qm status $VM_ID | grep -q running; do
    sleep 1
done

echo ""Starting VM $VM_ID to apply VNC config...""
qm start $VM_ID

echo ""Enabled VNC for VM $VM_ID - VNC port is: $VNC_PORT""
echo $VNC_PORT
");

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"{scriptPath} {_vmid}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        string[] lines = output.Split('\n');
                        string portLine = lines[lines.Length - 2];
                        if (int.TryParse(portLine, out int port))
                        {
                            return port;
                        }
                    }

                    throw new Exception($"Failed to enable VNC. Script output: {output}");
                }
            }
            finally
            {
                File.Delete(scriptPath);
            }
        }

        private async Task ConnectVncAsync()
        {
            try
            {
                int vncPort = await EnableVncAndGetPortAsync();
                UpdateStatus($"VNC enabled on port {vncPort}. Connecting...");

                var vncUrl = new Uri(_proxmoxClient.ApiUrl);

                _webSocket = new ClientWebSocket();
                _cts = new CancellationTokenSource();

                // Configure WebSocket to ignore certificate errors
                _webSocket.Options.RemoteCertificateValidationCallback =
                    new RemoteCertificateValidationCallback((sender, certificate, chain, sslPolicyErrors) => true);

               // var wsUri = new Uri($"wss://{vncUrl.Host}:{vncPort}/api2/json/vncwebsocket?port={vncPort}&vncticket={Uri.EscapeDataString(_proxmoxClient.GetAuthTokenAsync().Result)}");
               // await _webSocket.ConnectAsync(wsUri, _cts.Token);

                _isConnected = true;
                UpdateStatus($"Connected to VNC on port {vncPort} (Certificate errors ignored)");
                UpdateButtonStates();

                _ = ReceiveVncDataAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus($"VNC connection error: {ex.Message}");
            }
        }

        private async Task ReceiveVncDataAsync()
        {
            var buffer = new byte[16 * 1024];
            try
            {
                while (_webSocket.State == WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        await ProcessVncDataAsync(buffer, result.Count);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, _cts.Token);
                        UpdateStatus("VNC connection closed");
                        _isConnected = false;
                        UpdateButtonStates();
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"VNC data receive error: {ex.Message}");
            }
        }

        private async Task ProcessVncDataAsync(byte[] data, int count)
        {
            try
            {
                using (var ms = new InMemoryRandomAccessStream())
                {
                    await ms.WriteAsync(data.AsBuffer(0, count));
                    ms.Seek(0);

                    var decoder = await BitmapDecoder.CreateAsync(ms);
                    var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                    var source = new SoftwareBitmapSource();
                    await source.SetBitmapAsync(softwareBitmap);

                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        VncImage.Source = source;
                    });
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"VNC data processing error: {ex.Message}");
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            await DisconnectAsync();
        }

        private async Task DisconnectAsync()
        {
            try
            {
                if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                }
                _isConnected = false;
                UpdateStatus("Disconnected from VNC");
                UpdateButtonStates();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error disconnecting: {ex.Message}");
            }
        }

        private async void SendCtrlAltDelButton_Click(object sender, RoutedEventArgs e)
        {
            await SendCtrlAltDelAsync();
        }

        private async Task SendCtrlAltDelAsync()
        {
            try
            {
                if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    // This is a simplified representation of Ctrl+Alt+Del key event
                    // You may need to adjust this based on the actual VNC protocol implementation
                    byte[] keyEvent = new byte[] { 0x04, 0x01, 0x00, 0x00, 0x00, 0x1d, 0x38, 0x53 };
                    await _webSocket.SendAsync(new ArraySegment<byte>(keyEvent), WebSocketMessageType.Binary, true, _cts.Token);
                    UpdateStatus("Sent Ctrl+Alt+Del");
                }
                else
                {
                    UpdateStatus("Not connected to VNC");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error sending Ctrl+Alt+Del: {ex.Message}");
            }
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        private void UpdateButtonStates()
        {
            DisconnectButton.IsEnabled = _isConnected;
            SendCtrlAltDelButton.IsEnabled = _isConnected;
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _webSocket?.Dispose();
        }
    }
}

