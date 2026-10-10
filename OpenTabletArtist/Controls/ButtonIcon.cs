using Avalonia;
using Avalonia.Media;

namespace OpenTabletArtist.Controls;

/// <summary>
/// An optional icon after a button's text, for the buttons whose theme has a slot for one (today the
/// <c>AccentButton</c>). Attached rather than a new Button subclass, so the button keeps its string
/// <c>Content</c>: the theme upper-cases that, tests and automation find the button by it, and the icon
/// is added or taken away by a style (a class toggled from a binding) with no change to the content.
/// </summary>
public static class ButtonIcon
{
    /// <summary>The icon geometry (see Themes/Icons.axaml). Null, the default, shows no icon and takes no room.</summary>
    public static readonly AttachedProperty<Geometry?> DataProperty =
        AvaloniaProperty.RegisterAttached<Visual, Geometry?>("Data", typeof(ButtonIcon));

    public static Geometry? GetData(AvaloniaObject element) => element.GetValue(DataProperty);
    public static void SetData(AvaloniaObject element, Geometry? value) => element.SetValue(DataProperty, value);
}
