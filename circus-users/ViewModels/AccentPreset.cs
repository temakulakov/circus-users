using Circus.Users.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Circus.Users.App.ViewModels;

/// <summary>Образец цвета в палитре акцента.</summary>
public sealed partial class AccentPreset : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionVisibility))]
    private bool _isSelected;

    public AccentPreset(Color color, bool isCustom)
    {
        Color = color;
        IsCustom = isCustom;
        Brush = new SolidColorBrush(color);
        Hex = ColorHex.ToHex(color);
    }

    public Color Color { get; }

    /// <summary>Пресет добавлен пользователем — такой можно удалить.</summary>
    public bool IsCustom { get; }

    public SolidColorBrush Brush { get; }

    public string Hex { get; }

    /// <summary>Галочка на выбранном цвете.</summary>
    public Visibility SelectionVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;
}
