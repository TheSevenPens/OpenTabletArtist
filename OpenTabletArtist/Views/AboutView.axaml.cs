using Avalonia.Controls;

namespace OpenTabletArtist.Views;

// (The inline-link plumbing that used to live here went with the Help card. Avalonia has no inline
// hyperlink, so making one phrase of a paragraph clickable meant hit-testing the pointer against that
// run's character range in the text layout — about fifty lines of it, for one sentence. The help text is
// a manual page now, reached by an ordinary link in RESOURCES, so none of it is needed.)
public partial class AboutView : UserControl
{
    public AboutView() => InitializeComponent();
}
