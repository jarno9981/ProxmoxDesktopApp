using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProxmoxApiHelper.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Microsoft.UI.Dispatching;
using Microsoft.UI;
using Windows.UI.Popups;

namespace ProxmoxApiHelper
{
    public sealed partial class EditUserWindow : Window
    {
        private readonly ProxmoxClient _proxmoxClient;
        private readonly string _userId;
        private Dictionary<string, object> _userData;
        private List<Dictionary<string, object>> _allGroups;

        public EditUserWindow(ProxmoxClient proxmoxClient, string userId)
        {
            this.InitializeComponent();
            _proxmoxClient = proxmoxClient ?? throw new ArgumentNullException(nameof(proxmoxClient));
            _userId = userId ?? throw new ArgumentNullException(nameof(userId));

            SetWindowSize(600, 1025);
            SetupTitleBar();

            // Remove the Content initialization as it's already set by InitializeComponent
            LoadUserDataAsync();

        }


        private async void LoadUserDataAsync()
        {
            try
            {
                _userData = await _proxmoxClient.GetUserConfigAsync(_userId);
                _allGroups = await _proxmoxClient.GetGroupsAsync();

                await PopulateUserDataAsync();
            }
            catch (Exception ex)
            {
                
            }
        }

        private async Task PopulateUserDataAsync()
        {
            
                UserIdTextBox.Text = _userId;
                EmailTextBox.Text = _userData.TryGetValue("email", out var email) ? email.ToString() : "";
                EnabledCheckBox.IsChecked = _userData.TryGetValue("enable", out var enable) && (bool)enable;

                if (_userData.TryGetValue("expire", out var expireObj) && long.TryParse(expireObj.ToString(), out var expireSeconds))
                {
                    ExpiryDatePicker.Date = DateTimeOffset.FromUnixTimeSeconds(expireSeconds).Date;
                }
                else
                {
                    ExpiryDatePicker.Date = DateTime.Now.AddYears(1).Date;
                }

                var userGroups = _userData.TryGetValue("groups", out var groupsObj) ? groupsObj.ToString().Split(',') : Array.Empty<string>();

                GroupsStackPanel.Children.Clear();
                foreach (var group in _allGroups)
                {
                    if (group.TryGetValue("groupid", out var groupId) && groupId != null)
                    {
                        var checkBox = new CheckBox
                        {
                            Content = groupId.ToString(),
                            IsChecked = userGroups.Contains(groupId.ToString())
                        };
                        GroupsStackPanel.Children.Add(checkBox);
                    }
                }
           
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
            appWindow.Title = "Edit User";
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var updatedUserConfig = new UserConfig
                {
                    Email = EmailTextBox.Text,
                    Enable = EnabledCheckBox.IsChecked ?? false,
                    Expire = (int?)ExpiryDatePicker.Date?.ToUnixTimeSeconds()
                };

                var selectedGroups = GroupsStackPanel.Children
                    .OfType<CheckBox>()
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => cb.Content.ToString())
                    .ToList();

                updatedUserConfig.Groups = selectedGroups;

                bool success = await _proxmoxClient.UpdateUserAsync(_userId, updatedUserConfig);
                if (success)
                {
                    MessageDialog successDialog = new MessageDialog("User information has been successfully updated.", "User Updated");
                    await successDialog.ShowAsync();
                    this.Close();
                }
                else
                {
                    MessageDialog errorDialog = new MessageDialog("Failed to update user. Please try again.", "Update User Error");
                    await errorDialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                MessageDialog errorDialog = new MessageDialog($"Failed to update user: {ex.Message}", "Update User Error");
                await errorDialog.ShowAsync();
            }
        }
    }


}

