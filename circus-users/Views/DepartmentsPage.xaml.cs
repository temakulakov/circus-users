using System.ComponentModel;
using System.Runtime.CompilerServices;
using Circus.Users.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Circus.Users.App.Views;

/// <summary>Настройка подразделений: дерево и шаблон нового сотрудника.</summary>
public sealed partial class DepartmentsPage : Page, INotifyPropertyChanged
{
    public DepartmentsPage()
    {
        ViewModel = App.Services.GetRequiredService<DepartmentsViewModel>();
        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal DepartmentsViewModel ViewModel { get; }

    public Visibility FormVisibility =>
        ViewModel.SelectedNode is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PlaceholderVisibility =>
        ViewModel.SelectedNode is null ? Visibility.Visible : Visibility.Collapsed;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DepartmentsViewModel.SelectedNode))
        {
            OnPropertyChanged(nameof(FormVisibility));
            OnPropertyChanged(nameof(PlaceholderVisibility));
        }
    }

    private void OnManagerTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Подсказки пересчитываются только на ввод с клавиатуры: программная подстановка
        // выбранного руководителя не должна снова открывать список.
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        ViewModel.UpdateManagerSuggestions(sender.Text);
        ViewModel.ClearManagerIfEmpty();
    }

    private void OnManagerChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is ManagerOption option)
        {
            ViewModel.PickManager(option);
        }
    }

    private async void OnDepartmentToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || ViewModel.SelectedNode is null)
        {
            return;
        }

        // Положение переключателя уже изменилось; если оно совпадает с моделью,
        // это отклик на смену подразделения, а не действие человека.
        if (toggle.IsOn == ViewModel.IsDepartment)
        {
            return;
        }

        if (!toggle.IsOn && !await ConfirmExclusionAsync())
        {
            toggle.IsOn = true;
            return;
        }

        var error = await ViewModel.SetIsDepartmentAsync(toggle.IsOn);
        if (error is null)
        {
            return;
        }

        toggle.IsOn = ViewModel.IsDepartment;
        await ShowErrorAsync("Не удалось изменить отметку", error);
    }

    private async Task<bool> ConfirmExclusionAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Исключить каталог из подразделений?",
            Content = new TextBlock
            {
                Text = "В этом каталоге нельзя будет заводить сотрудников, он исчезнет из выбора "
                       + "на странице создания, а шаблон станет недоступен. Отметка сохраняется "
                       + "в общих настройках и видна всем администраторам. Сам каталог не меняется.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Исключить",
            CloseButtonText = "Отмена",
            // Умолчание — отмена: действие меняет поведение приложения для всех.
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var error = await ViewModel.SaveTemplateAsync();
        if (error is null)
        {
            return;
        }

        await ShowErrorAsync("Не удалось сохранить шаблон", error);
    }

    private async Task ShowErrorAsync(string title, string message)
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
