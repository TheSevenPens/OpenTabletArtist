using System.Collections.Generic;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The SETTINGS <b>System</b> pivot laid out in two columns: the left stacks the integration toggles
/// (Startup + Shortcut); the right holds Driver Cleanup (conflicting-driver warnings + the cleanup tool).
/// Like <see cref="CompositeSectionViewModel"/> it holds no state of its own — just references to the
/// shared sub-VMs, each resolved to its normal view by the typed DataTemplates — so the layout change
/// affects nothing about how those pages work.
/// </summary>
public sealed class SystemSettingsViewModel
{
    public SystemSettingsViewModel(object startup, object shortcut, object driverCleanup)
    {
        LeftSections = new[] { startup, shortcut };
        RightSections = new[] { driverCleanup };
    }

    /// <summary>Left column, top-to-bottom: Startup then Shortcut.</summary>
    public IReadOnlyList<object> LeftSections { get; }

    /// <summary>Right column: Driver Cleanup — the conflicting-driver warnings and the cleanup tool.</summary>
    public IReadOnlyList<object> RightSections { get; }
}
