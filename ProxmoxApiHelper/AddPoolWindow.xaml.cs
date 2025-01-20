using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ProxmoxApiHelper
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class AddPoolWindow : Window
    {
        private ProxmoxClient _proxmoxClient;
        private AppWindow appWindow;


        public AddPoolWindow(ProxmoxClient proxmoxClient)
        {
            this.InitializeComponent();
            SetWindowSize(625, 975);
            TitleTop();
            _proxmoxClient = proxmoxClient;
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

        private async void CreatePoolButton_Click(object sender, RoutedEventArgs e)
        {
            string poolId = PoolIdTextBox.Text.Trim();
            string comment = CommentTextBox.Text.Trim();

            if (string.IsNullOrEmpty(poolId))
            {
                await ShowMessageDialogAsync("Error", "Pool ID is required.");
                return;
            }

            try
            {
                bool result = await _proxmoxClient.CreatePoolAsync(poolId, comment);
                if (result)
                {
                    await ShowMessageDialogAsync("Success", $"Pool '{poolId}' created successfully.");
                    this.Close();
                }
                else
                {
                    await ShowMessageDialogAsync("Error", "Failed to create pool. Please try again.");
                }
            }
            catch (Exception ex)
            {
                await ShowMessageDialogAsync("Error", $"An error occurred: {ex.Message}");
            }
        }

        private async Task ShowMessageDialogAsync(string title, string content)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK"
            };

            dialog.XamlRoot = this.Content.XamlRoot;
            await dialog.ShowAsync();
        }
    }
}
