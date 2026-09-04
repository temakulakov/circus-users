using System.Numerics;
using Circus.Users.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;

namespace Circus.Users.App.Views;

/// <summary>Страница настроек оформления.</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }

    internal SettingsViewModel ViewModel { get; }

    /// <remarks>
    /// Обработчик, а не привязка команды: ElementName-привязки внутри шаблона
    /// ItemsRepeater не разрешаются. Сам образец приходит через Tag с компилируемой
    /// привязкой — DataContext реализованным элементам ItemsRepeater тоже не проставляет.
    /// </remarks>
    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccentPreset preset } button)
        {
            return;
        }

        ViewModel.SelectPreset(preset);
        PlayPulse(button);
    }

    /// <remarks>
    /// Меню собирается на месте и только для своих цветов. Готовый ContextFlyout в разметке
    /// открывался бы и для стандартных образцов — пустым, потому что единственный его пункт
    /// в этом случае скрыт.
    /// </remarks>
    private void OnPresetRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AccentPreset { IsCustom: true } preset } element)
        {
            return;
        }

        var remove = new MenuFlyoutItem { Text = "Удалить цвет", Icon = new SymbolIcon(Symbol.Delete) };
        remove.Click += (_, _) => ViewModel.RemovePresetCommand.Execute(preset);

        var flyout = new MenuFlyout();
        flyout.Items.Add(remove);
        flyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });

        e.Handled = true;
    }

    /// <summary>Короткая пульсация образца — подтверждение, что цвет принят.</summary>
    private static void PlayPulse(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var size = element.ActualSize;
        visual.CenterPoint = new Vector3(size.X / 2, size.Y / 2, 0);

        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0.0f, Vector3.One);
        animation.InsertKeyFrame(0.35f, new Vector3(1.28f, 1.28f, 1.0f));
        animation.InsertKeyFrame(0.65f, new Vector3(0.94f, 0.94f, 1.0f));
        animation.InsertKeyFrame(1.0f, Vector3.One);
        animation.Duration = TimeSpan.FromMilliseconds(380);

        visual.StartAnimation("Scale", animation);
    }
}
