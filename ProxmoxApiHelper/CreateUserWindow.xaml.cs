using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProxmoxApiHelper.Helpers;
using Windows.Graphics;

namespace ProxmoxApiHelper
{
    public sealed partial class CreateUserWindow : Window
    {
        private readonly ProxmoxClient _proxmoxClient;
        public CreateUserViewModel ViewModel { get; }

        private AppWindow appWindow;

        public CreateUserWindow(ProxmoxClient proxmoxClient)
        {
            this.InitializeComponent();
            _proxmoxClient = proxmoxClient ?? throw new ArgumentNullException(nameof(proxmoxClient));
            ViewModel = new CreateUserViewModel();

            // Load available groups when window is initialized
            LoadGroupsAsync();
            SetWindowSize(675, 985);
            TitleTop();
        }

        public void TitleTop()
        {
            nint hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon("apiwindowlogo.ico");

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

        private async void LoadGroupsAsync()
        {
            try
            {
                ViewModel.IsLoading = true;
                var groups = await _proxmoxClient.GetGroupsAsync();

               
                    ViewModel.AvailableGroups.Clear();
                    foreach (var group in groups)
                    {
                        if (group.TryGetValue("groupid", out var groupId) && groupId != null)
                        {
                            ViewModel.AvailableGroups.Add(groupId.ToString());
                        }
                    }
               
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Failed to load groups", ex.Message);
            }
            finally
            {
                ViewModel.IsLoading = false;
            }
        }

        private async void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
                return;

            try
            {
                ViewModel.IsLoading = true;
                ViewModel.ErrorMessage = string.Empty;

                var userConfig = new UserConfig
                {
                    Password = ViewModel.Password,
                    Email = ViewModel.Email,
                    Firstname = ViewModel.FirstName,
                    Lastname = ViewModel.LastName,
                    Enable = ViewModel.IsEnabled,
                    Groups = ViewModel.SelectedGroup != null ? new List<string> { ViewModel.SelectedGroup } : new List<string>(),
                    Comment = ViewModel.Comment,
                    Keys = ViewModel.Keys,
                    Expire = ViewModel.ExpiryDate.HasValue ?
                        (int)(ViewModel.ExpiryDate.Value.DateTime - DateTime.UnixEpoch).TotalSeconds :
                        (int?)null
                };

                bool success = await _proxmoxClient.CreateUserAsync(ViewModel.UserId, userConfig);
                if (success)
                {
                    await ShowSuccessDialog();
                    this.Close();
                }
                else
                {
                    await ShowErrorDialog("Failed to create user", "The operation was not successful. Please try again.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Error creating user", ex.Message);
            }
            finally
            {
                ViewModel.IsLoading = false;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(ViewModel.UserId))
            {
                ViewModel.ErrorMessage = "User ID is required";
                return false;
            }

            if (!ViewModel.UserId.Contains("@"))
            {
                ViewModel.ErrorMessage = "User ID must be in the format name@realm";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ViewModel.Password))
            {
                ViewModel.ErrorMessage = "Password is required";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(ViewModel.Email) &&
                !System.Text.RegularExpressions.Regex.IsMatch(ViewModel.Email,
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                ViewModel.ErrorMessage = "Invalid email format";
                return false;
            }

            ViewModel.ErrorMessage = string.Empty;
            return true;
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

        private async Task ShowSuccessDialog()
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = "Success",
                Content = "User created successfully",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };

            await dialog.ShowAsync();
        }
    }

    public class CreateUserViewModel : INotifyPropertyChanged
    {
        private string _userId;
        private string _password;
        private string _firstName;
        private string _lastName;
        private string _email;
        private bool _isEnabled = true;
        private DateTimeOffset? _expiryDate;
        private string _comment;
        private string _keys;
        private string _errorMessage;
        private bool _isLoading;
        private string _selectedGroup;

        public ObservableCollection<string> AvailableGroups { get; } = new ObservableCollection<string>();

        public string UserId
        {
            get => _userId;
            set => SetProperty(ref _userId, value);
        }

        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        public string FirstName
        {
            get => _firstName;
            set => SetProperty(ref _firstName, value);
        }

        public string LastName
        {
            get => _lastName;
            set => SetProperty(ref _lastName, value);
        }

        public string Email
        {
            get => _email;
            set => SetProperty(ref _email, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public DateTimeOffset? ExpiryDate
        {
            get => _expiryDate;
            set => SetProperty(ref _expiryDate, value);
        }

        public string Comment
        {
            get => _comment;
            set => SetProperty(ref _comment, value);
        }

        public string Keys
        {
            get => _keys;
            set => SetProperty(ref _keys, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string SelectedGroup
        {
            get => _selectedGroup;
            set => SetProperty(ref _selectedGroup, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
                return;

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

