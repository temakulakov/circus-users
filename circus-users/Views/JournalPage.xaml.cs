using Circus.Users.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Circus.Users.App.Views;

/// <summary>Журнал операций: что приложение меняло в каталоге и в почте.</summary>
public sealed partial class JournalPage : Page
{
    public JournalPage()
    {
        ViewModel = App.Services.GetRequiredService<JournalViewModel>();
        InitializeComponent();
    }

    internal JournalViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Журнал читается при каждом заходе: его дополняют другие администраторы.
        _ = ViewModel.RefreshAsync();
    }

    private void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = ViewModel.RefreshAsync();
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }
}
