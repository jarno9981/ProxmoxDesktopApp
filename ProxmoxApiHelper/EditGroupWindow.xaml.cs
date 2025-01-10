using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProxmoxApiHelper.Helpers;

namespace ProxmoxApiHelper
{
    public sealed partial class EditGroupWindow : Window
    {
        private readonly ProxmoxClient _proxmoxClient;
        private readonly string _groupId;
        private Dictionary<string, object> _groupData;
        private List<string> _allUsers;

        public EditGroupWindow(ProxmoxClient proxmoxClient, string groupId)
        {
            this.InitializeComponent();
            _proxmoxClient = proxmoxClient;
            _groupId = groupId;
            LoadGroupDataAsync();
        }

        private async void LoadGroupDataAsync()
        {
            try
            {
               // _groupData = await _proxmoxClient.GetGroupsAsync();
                _allUsers = await _proxmoxClient.GetUsersAsync();

                GroupIdTextBox.Text = _groupId;
                CommentTextBox.Text = _groupData.ContainsKey("comment") ? _groupData["comment"].ToString() : "";

                var groupMembers = _groupData.ContainsKey("members") ? ((string)_groupData["members"]).Split(',') : new string[0];
                foreach (var user in _allUsers)
                {
                    var checkBox = new CheckBox { Content = user, IsChecked = groupMembers.Contains(user) };
                    MembersStackPanel.Children.Add(checkBox);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Load Group Error", $"Failed to load group data: {ex.Message}");
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var updatedGroupData = new Dictionary<string, object>
                {
                    ["comment"] = CommentTextBox.Text
                };

                var selectedMembers = MembersStackPanel.Children
                    .OfType<CheckBox>()
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => cb.Content.ToString())
                    .ToList();

                updatedGroupData["members"] = string.Join(",", selectedMembers);

                await _proxmoxClient.UpdateGroupAsync(_groupId, updatedGroupData);
                await ShowSuccessDialog("Group Updated", "Group information has been successfully updated.");
                this.Close();
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Update Group Error", $"Failed to update group: {ex.Message}");
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

