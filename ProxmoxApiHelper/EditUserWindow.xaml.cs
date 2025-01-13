using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProxmoxApiHelper.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Graphics;
using AppWindow = Microsoft.UI.Windowing.AppWindow;

namespace ProxmoxApiHelper
{
    public sealed partial class EditUserWindow : Window
    {
        private readonly ProxmoxClient _proxmoxClient;
        private readonly string _userId;
        private Dictionary<string, object> _userData;
        private List<Dictionary<string, object>> _allGroups;
        private AppWindow appWindow;

        public EditUserWindow(ProxmoxClient proxmoxClient, string userId)
        {
            this.InitializeComponent();
            _proxmoxClient = proxmoxClient ?? throw new ArgumentNullException(nameof(proxmoxClient));
            _userId = userId ?? throw new ArgumentNullException(nameof(userId));

            SetWindowSize(600, 1025);

            LoadUserDataAsync();
        }

       

        private void SetWindowSize(int width, int height)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new SizeInt32(width, height));
        }


        private async void LoadUserDataAsync()
        {
            try
            {
                var response = await _proxmoxClient.GetUserConfigAsync(_userId);
                _userData = new Dictionary<string, object>();

                foreach (var kvp in response)
                {
                    switch (kvp.Value.ValueKind)
                    {
                        case JsonValueKind.String:
                            _userData[kvp.Key] = kvp.Value.GetString();
                            break;
                        case JsonValueKind.Number:
                            _userData[kvp.Key] = kvp.Value.GetInt64();
                            break;
                        case JsonValueKind.True:
                        case JsonValueKind.False:
                            _userData[kvp.Key] = kvp.Value.GetBoolean();
                            break;
                        case JsonValueKind.Array:
                            var array = new List<string>();
                            foreach (var item in kvp.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    array.Add(item.GetString());
                                }
                            }
                            _userData[kvp.Key] = array;
                            break;
                        case JsonValueKind.Object:
                            var dict = new Dictionary<string, object>();
                            foreach (var prop in kvp.Value.EnumerateObject())
                            {
                                dict[prop.Name] = prop.Value.GetString();
                            }
                            _userData[kvp.Key] = dict;
                            break;
                    }
                }

                _allGroups = await _proxmoxClient.GetGroupsAsync();
                await PopulateUserDataAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Failed to load user data", ex.Message);
            }
        }

        private async Task PopulateUserDataAsync()
        {
            try
            {
                UserIdTextBox.Text = _userId;

                if (_userData.TryGetValue("email", out var email))
                {
                    EmailTextBox.Text = email?.ToString() ?? "";
                }

                if (_userData.TryGetValue("enable", out var enable))
                {
                    EnabledCheckBox.IsChecked = enable is bool enableBool && enableBool;
                }

                if (_userData.TryGetValue("firstname", out var firstName))
                {
                    FirstNameTextBox.Text = firstName?.ToString() ?? "";
                }

                if (_userData.TryGetValue("lastname", out var lastName))
                {
                    LastNameTextBox.Text = lastName?.ToString() ?? "";
                }

                if (_userData.TryGetValue("comment", out var comment))
                {
                    CommentTextBox.Text = comment?.ToString() ?? "";
                }

                if (_userData.TryGetValue("expire", out var expire) && expire != null)
                {
                    if (long.TryParse(expire.ToString(), out long expireSeconds))
                    {
                        ExpiryDatePicker.Date = DateTimeOffset.FromUnixTimeSeconds(expireSeconds).Date;
                    }
                }
                else
                {
                    ExpiryDatePicker.Date = DateTimeOffset.Now;
                }

                if (_userData.TryGetValue("keys", out var keys))
                {
                    KeysTextBox.Text = keys?.ToString() ?? "";
                }

                // Handle groups
                var userGroups = new List<string>();
                if (_userData.TryGetValue("groups", out var groups))
                {
                    if (groups is string groupsStr)
                    {
                        userGroups.AddRange(groupsStr.Split(',', StringSplitOptions.RemoveEmptyEntries));
                    }
                    else if (groups is List<string> groupsList)
                    {
                        userGroups.AddRange(groupsList);
                    }
                }

                var groupItems = _allGroups.Select(group =>
                {
                    group.TryGetValue("groupid", out var groupId);
                    return new GroupItem
                    {
                        GroupId = groupId?.ToString(),
                        IsSelected = userGroups.Contains(groupId?.ToString())
                    };
                }).ToList();

                GroupsListView.ItemsSource = groupItems;

                // Handle tokens
                if (_userData.TryGetValue("tokens", out var tokens) && tokens is Dictionary<string, object> tokenDict)
                {
                    TokensListView.Items.Clear();
                    foreach (var token in tokenDict)
                    {
                        TokensListView.Items.Add(new TokenItem { Name = token.Key });
                    }
                }

            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Error populating user data", ex.Message);
            }
        }



        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var updatedUserConfig = new UserConfig
                {
                    Email = EmailTextBox.Text,
                    Enable = EnabledCheckBox.IsChecked ?? false,
                    Firstname = FirstNameTextBox.Text,
                    Lastname = LastNameTextBox.Text,
                    Comment = CommentTextBox.Text,
                    Keys = KeysTextBox.Text,
                    Append = AppendCheckBox.IsChecked ?? false
                };

                // Only set the password if a new one is provided
                if (!string.IsNullOrWhiteSpace(PasswordBox.Password))
                {
                    updatedUserConfig.Password = PasswordBox.Password;
                }
                
                updatedUserConfig.Expire = (int)ExpiryDatePicker.Date.ToUnixTimeSeconds();
                

                var selectedGroups = (GroupsListView.ItemsSource as List<GroupItem>)?
                    .Where(gi => gi.IsSelected)
                    .Select(gi => gi.GroupId)
                    .ToList();

                updatedUserConfig.Groups = selectedGroups ;

                bool success = await _proxmoxClient.UpdateUserAsync(_userId, updatedUserConfig);
                if (success)
                {
                    await ShowSuccessDialog("User information has been successfully updated.");
                    this.Close();
                }
                else
                {
                    await ShowErrorDialog("Update User Error", "Failed to update user. Please try again.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Update User Error", $"Failed to update user: {ex.Message}");
            }
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
    }

    public class TokenItem
    {
        public string Name { get; set; }
    }
}

