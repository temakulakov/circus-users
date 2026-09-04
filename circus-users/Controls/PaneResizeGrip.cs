using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Circus.Users.App.Controls;

/// <summary>
/// Узкая полоса на правом краю панели навигации, за которую панель тянут мышью.
/// </summary>
/// <remarks>
/// Отдельный класс нужен только ради курсора: <c>ProtectedCursor</c> у <c>UIElement</c>
/// защищённый, задать его снаружи нельзя.
/// </remarks>
internal sealed partial class PaneResizeGrip : Grid
{
    public PaneResizeGrip() =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}
