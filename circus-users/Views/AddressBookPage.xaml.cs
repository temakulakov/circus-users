using System.Text;
using Circus.Users.App.ViewModels;
using Circus.Users.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;

namespace Circus.Users.App.Views;

/// <summary>Адресная книга: таблица сотрудников с настраиваемыми колонками.</summary>
public sealed partial class AddressBookPage : Page
{
    public AddressBookPage()
    {
        ViewModel = App.Services.GetRequiredService<AddressBookViewModel>();
        InitializeComponent();

        // Источник группировки нельзя задать через x:Bind в ресурсах — связываем в коде.
        ((CollectionViewSource)Resources["GroupedRows"]).Source = ViewModel.Groups;
    }

    internal AddressBookViewModel ViewModel { get; }

    private void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.RefreshCommand.Execute(null);
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnExportAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ExportAsync();
    }

    private async void OnExportClick(object sender, RoutedEventArgs e) => await ExportAsync();

    /// <summary>
    /// Выгружает то, что видно на экране: те же колонки и тот же фильтр.
    /// </summary>
    private async Task ExportAsync()
    {
        if (ViewModel.VisibleCount == 0)
        {
            await ShowMessageAsync("Выгружать нечего", "В таблице сейчас нет ни одной строки.");
            return;
        }

        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = ViewModel.SuggestedExportName,
        };

        picker.FileTypeChoices.Add("Таблица CSV", [".csv"]);

        // Без владельца системный диалог в WinUI не открывается.
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            // Метка порядка байтов обязательна: без неё Excel в русской локали
            // открывает файл в однобайтовой кодировке и показывает мусор вместо кириллицы.
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            await File.WriteAllTextAsync(file.Path, ViewModel.BuildCsv(), encoding);

            await ShowMessageAsync(
                "Готово",
                $"Выгружено строк: {ViewModel.VisibleCount}.\n{file.Path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("Не удалось сохранить файл", ex.Message);
        }
    }


    // Карандаш проявляется по наведению. Состояния наведения у элемента списка
    // в разметке не выразить, поэтому меняем прозрачность вручную.
    private static void SetEditButtonOpacity(object sender, double opacity)
    {
        if (sender is FrameworkElement row && row.FindName("EditButton") is Button button)
        {
            button.Opacity = opacity;
        }
    }

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e) => SetEditButtonOpacity(sender, 1);

    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e) => SetEditButtonOpacity(sender, 0);

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EmployeeRow row })
        {
            await ShowEditDialogAsync(row.Employee);
        }
    }

    /// <remarks>
    /// Меню собирается на месте и только для администратора: готовый ContextFlyout
    /// открывался бы у всех, пусть и без единого доступного пункта.
    /// </remarks>
    private void OnRowRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (!ViewModel.IsAdmin || sender is not FrameworkElement { Tag: EmployeeRow row } element)
        {
            return;
        }

        var employee = row.Employee;
        var flyout = new MenuFlyout();

        var edit = new MenuFlyoutItem { Text = "Изменить данные", Icon = new SymbolIcon(Symbol.Edit) };
        edit.Click += async (_, _) => await ShowEditDialogAsync(employee);
        flyout.Items.Add(edit);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var block = new MenuFlyoutItem { Text = "Заблокировать учётную запись", Icon = new SymbolIcon(Symbol.BlockContact) };
        block.Click += async (_, _) => await ConfirmBlockAsync(employee);
        flyout.Items.Add(block);

        var dismiss = new MenuFlyoutItem { Text = "Уволить", Icon = new SymbolIcon(Symbol.LeaveChat) };
        dismiss.Click += async (_, _) => await ConfirmDismissAsync(employee);
        flyout.Items.Add(dismiss);

        flyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
        e.Handled = true;
    }

    private async Task ShowEditDialogAsync(EmployeeCard employee)
    {
        ViewModel.PrepareEdit(employee);

        EditDialog.XamlRoot = XamlRoot;
        EditDialog.Title = $"Изменить: {employee.DisplayName}";

        if (await EditDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var error = await ViewModel.SaveEditAsync();
        if (error is not null)
        {
            await ShowMessageAsync("Не удалось сохранить", error);
        }
    }

    private async Task ConfirmBlockAsync(EmployeeCard employee)
    {
        var confirmed = await ConfirmAsync(
            "Заблокировать учётную запись",
            $"Учётная запись «{employee.DisplayName}» будет отключена. Сотрудник не сможет войти в домен. Членство в группах сохранится.",
            "Заблокировать");

        if (!confirmed)
        {
            return;
        }

        var error = await ViewModel.SetBlockedAsync(employee, isBlocked: true);
        if (error is not null)
        {
            await ShowMessageAsync("Не удалось заблокировать", error);
        }
    }

    private async Task ConfirmDismissAsync(EmployeeCard employee)
    {
        var confirmed = await ConfirmAsync(
            "Уволить сотрудника",
            $"Учётная запись «{employee.DisplayName}» будет отключена, а членство во всех группах снято. "
            + "Снятые группы попадут в журнал операций — только по нему увольнение можно откатить.",
            "Уволить");

        if (!confirmed)
        {
            return;
        }

        var error = await ViewModel.DismissAsync(employee);
        if (error is not null)
        {
            await ShowMessageAsync("Не удалось уволить", error);
        }
    }

    private async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                // К тексту подтверждения всегда добавляется, куда именно уйдёт действие.
                Text = message + Environment.NewLine + Environment.NewLine + ViewModel.WriteTargetNotice,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = confirmText,
            CloseButtonText = "Отмена",
            // Опасное действие не должно срабатывать по нажатию Enter.
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
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

    private async void OnConfigureFieldsClick(object sender, RoutedEventArgs e)
    {
        FieldsDialog.XamlRoot = XamlRoot;
        ApplyForEveryoneCheck.IsChecked = false;

        var result = await FieldsDialog.ShowAsync();

        switch (result)
        {
            case ContentDialogResult.Primary when ApplyForEveryoneCheck.IsChecked == true:
                var error = await ViewModel.ApplyFieldsForEveryoneAsync();
                if (error is not null)
                {
                    await ShowMessageAsync("Не удалось применить для всех", error);
                }

                break;

            case ContentDialogResult.Primary:
                ViewModel.ApplyFieldsCommand.Execute(null);
                break;

            case ContentDialogResult.Secondary:
                ViewModel.ResetFieldsCommand.Execute(null);
                break;
        }
    }
}
