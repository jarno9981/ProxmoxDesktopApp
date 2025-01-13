using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using ProxmoxApiHelper.Helpers;
using Microsoft.UI;

namespace ProxmoxApiHelper
{
    public sealed partial class CreateGroup : Window
    {
        private readonly ProxmoxClient _proxmoxClient;
        private ObservableCollection<UserConfig> AvailableUsers { get; set; }
        private ObservableCollection<UserConfig> GroupMembers { get; set; }

        public CreateGroup(ProxmoxClient proxmoxClient)
        {
            this.InitializeComponent();
            _proxmoxClient = proxmoxClient ?? throw new ArgumentNullException(nameof(proxmoxClient));

            AvailableUsers = new ObservableCollection<UserConfig>();
            GroupMembers = new ObservableCollection<UserConfig>();

            AvailableUsersListView.ItemsSource = AvailableUsers;
            GroupMembersListView.ItemsSource = GroupMembers;

            SetWindowSize(625, 975);
            SetupTitleBar();
            LoadAvailableUsersAsync();
        }

        private async void LoadAvailableUsersAsync()
        {
            try
            {
                var users = await _proxmoxClient.GetUsersAsync();
                foreach (var user in users)
                {

                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Failed to load users", ex.Message);
            }
        }

        private void AddUsersButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedUsers = AvailableUsersListView.SelectedItems.Cast<UserConfig>().ToList();
            foreach (var user in selectedUsers)
            {
                AvailableUsers.Remove(user);
                GroupMembers.Add(user);
            }
        }

        private void RemoveUsersButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedUsers = GroupMembersListView.SelectedItems.Cast<UserConfig>().ToList();
            foreach (var user in selectedUsers)
            {
                GroupMembers.Remove(user);
                AvailableUsers.Add(user);
            }
        }

        private async void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            string groupId = GroupIdTextBox.Text.Trim();
            string comment = CommentTextBox.Text.Trim();

            if (string.IsNullOrEmpty(groupId))
            {
                await ShowErrorDialog("Invalid Group ID", "Please enter a valid Group ID.");
                return;
            }

            try
            {
                // First, create the group
                bool groupCreated = await _proxmoxClient.CreateGroupAsync(groupId, comment);
                if (groupCreated)
                {
                    // If group creation is successful, add members to the group
                    bool allMembersAdded = true;
                    foreach (var member in GroupMembers)
                    {
                        bool memberAdded = await _proxmoxClient.AddUserToGroupAsync(member, groupId);
                        if (!memberAdded)
                        {
                            allMembersAdded = false;
                            await ShowErrorDialog("Add Member Error", $"Failed to add user {member.Email} to group {groupId}.");
                        }
                    }

                    if (allMembersAdded)
                    {
                        await ShowSuccessDialog("Group created successfully and all members added.");
                    }
                    else
                    {
                        await ShowSuccessDialog("Group created successfully, but some members could not be added. Please check and try adding them manually.");
                    }
                    this.Close();
                }
                else
                {
                    await ShowErrorDialog("Create Group Error", "Failed to create group. Please try again.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Create Group Error", $"Failed to create group: {ex.Message}");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async Task ShowErrorDialog(string title, string message)
        {
            ContentDialog errorDialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await errorDialog.ShowAsync();
        }

        private async Task ShowSuccessDialog(string message)
        {
            ContentDialog successDialog = new ContentDialog
            {
                Title = "Success",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await successDialog.ShowAsync();
        }

        private void SetWindowSize(int width, int height)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new SizeInt32(width, height));
        }

        private void SetupTitleBar()
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon("apiwindowlogo.ico");
            appWindow.Title = "Create Group";
        }
    }
}

