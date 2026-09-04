using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Circus.Users.App.Controls;

/// <summary>
/// Строка настройки в стиле параметров Windows: значок, заголовок, пояснение
/// и управляющий элемент справа.
/// </summary>
public sealed partial class SettingCard : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingCard),
        new PropertyMetadata(string.Empty, (d, _) => ((SettingCard)d).OnPropertyChanged(nameof(HasDescription))));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingCard),
        new PropertyMetadata(string.Empty, (d, _) => ((SettingCard)d).OnPropertyChanged(nameof(HasGlyph))));

    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent), typeof(object), typeof(SettingCard), new PropertyMetadata(null));

    public static readonly DependencyProperty SubContentProperty = DependencyProperty.Register(
        nameof(SubContent), typeof(object), typeof(SettingCard), new PropertyMetadata(null));

    public static readonly DependencyProperty SubContentVisibilityProperty = DependencyProperty.Register(
        nameof(SubContentVisibility), typeof(Visibility), typeof(SettingCard),
        new PropertyMetadata(Visibility.Collapsed));

    public SettingCard() => InitializeComponent();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Код символа Segoe Fluent Icons / Segoe MDL2 Assets.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Управляющий элемент, который показывается справа.</summary>
    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    /// <summary>Вложенный подпункт, который показывается под основной строкой.</summary>
    public object? SubContent
    {
        get => GetValue(SubContentProperty);
        set => SetValue(SubContentProperty, value);
    }

    public Visibility SubContentVisibility
    {
        get => (Visibility)GetValue(SubContentVisibilityProperty);
        set => SetValue(SubContentVisibilityProperty, value);
    }

    public Visibility HasGlyph => string.IsNullOrEmpty(Glyph) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility HasDescription => string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
