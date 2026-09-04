using System.ComponentModel;
using System.Runtime.CompilerServices;
using Circus.Users.App.ViewModels;
using Circus.Users.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Circus.Users.App.Views;

/// <summary>Создание сотрудника в каталоге и почте.</summary>
public sealed partial class AddUserPage : Page, INotifyPropertyChanged
{
    private ProvisionedUser? _lastCreated;

    public AddUserPage()
    {
        ViewModel = App.Services.GetRequiredService<AddUserViewModel>();
        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal AddUserViewModel ViewModel { get; }

    /// <summary>Подсказка под логином не дублирует полосу с ошибкой.</summary>
    public Visibility HintVisibility =>
        ViewModel.LoginHint.Length > 0 && !ViewModel.HasLoginProblem ? Visibility.Visible : Visibility.Collapsed;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AddUserViewModel.LoginHint) or nameof(AddUserViewModel.HasLoginProblem))
        {
            OnPropertyChanged(nameof(HintVisibility));
        }
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Создать сотрудника",
            Content = new TextBlock
            {
                Text = $"Будет создана учётная запись «{ViewModel.AccountName}» "
                       + $"в подразделении «{ViewModel.SelectedDepartment?.Name}»."
                       + Environment.NewLine + Environment.NewLine
                       + ViewModel.WriteTargetNotice,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Создать",
            CloseButtonText = "Отмена",
            // Создание — необратимое действие, поэтому Enter его не запускает.
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var (result, error) = await ViewModel.CreateAsync();

        if (error is not null || result is null)
        {
            await ShowMessageAsync("Не удалось создать сотрудника", error ?? "Неизвестная ошибка.");
            return;
        }

        await ShowResultAsync(result);
        ViewModel.ResetCommand.Execute(null);
    }

    private async Task ShowResultAsync(ProvisionedUser created)
    {
        _lastCreated = created;

        ResultAccount.Text = created.AccountName;
        ResultDomainPassword.Text = created.DomainPassword;
        ResultEmail.Text = created.Email;
        ResultMailPassword.Text = created.MailPassword ?? "почтовый модуль ещё не подключён";

        ResultWarnings.IsOpen = created.Warnings.Count > 0;
        ResultWarnings.Message = string.Join(Environment.NewLine, created.Warnings);

        ResultDialog.XamlRoot = XamlRoot;
        await ResultDialog.ShowAsync();

        // Пароли не должны пережить закрытие диалога.
        _lastCreated = null;
        ResultDomainPassword.Text = string.Empty;
        ResultMailPassword.Text = string.Empty;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_lastCreated is not { } created || sender is not FrameworkElement { Tag: string what })
        {
            return;
        }

        var text = what switch
        {
            "account" => created.AccountName,
            "domain-password" => created.DomainPassword,
            "email" => created.Email,
            "mail-password" => created.MailPassword ?? string.Empty,
            _ => string.Join(
                Environment.NewLine,
                $"Учётная запись: {created.AccountName}",
                $"Пароль в домене: {created.DomainPassword}",
                $"Почта: {created.Email}",
                $"Пароль от почты: {created.MailPassword ?? "—"}"),
        };

        if (text.Length == 0)
        {
            return;
        }

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Закрыть",
        };

        await dialog.ShowAsync();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
