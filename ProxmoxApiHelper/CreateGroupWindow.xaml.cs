using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using ProxmoxApiHelper.Helpers;

namespace ProxmoxApiHelper
{
    public sealed partial class CreateGroupWindow : Window
    {
        private readonly ProxmoxClient _proxmoxClient;

        public CreateGroupWindow(ProxmoxClient proxmoxClient)
        {
            this.InitializeComponent();
            _proxmoxClient = proxmoxClient;
        }

        private async void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string groupId = GroupIdTextBox.Text;
                string comment = CommentTextBox.Text;

                if (string.IsNullOrWhiteSpace(groupId))
                {
                    throw new ArgumentException("Group ID is required.");
                }

                //await _proxmoxClient.CreateGroupAsync(groupId, comment);
               // await ShowSuccessDialog("Group Created", $"Group '{groupId}' has been successfully created.");
                this.Close();
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Create Group Error", $"Failed to create group: {ex.Message}");
            }
        }

        private async Task ShowErrorDialog(string title, string message)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async Task ShowSuccessDialog(string title, string message)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };

            await dialog.ShowAsync();
        }
    }
}

