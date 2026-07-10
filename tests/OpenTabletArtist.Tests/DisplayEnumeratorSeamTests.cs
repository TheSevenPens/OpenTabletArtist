using System;
using System.Collections.Generic;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Covers the platform display-enumeration seam (#140): the static <see cref="DisplayEnumerator"/> facade
/// dispatches to a swappable <see cref="IDisplayEnumerator"/>, and the cross-platform Avalonia
/// implementation degrades to an empty list (never throws) when no screens are available. The full-fidelity
/// Windows path and the real geometry read are exercised on-device / in the GUI, not here.
/// </summary>
public class DisplayEnumeratorSeamTests
{
    private sealed class FakeEnumerator : IDisplayEnumerator
    {
        private readonly IReadOnlyList<DisplayInfo> _displays;
        public FakeEnumerator(params DisplayInfo[] displays) => _displays = displays;
        public IReadOnlyList<DisplayInfo> Enumerate() => _displays;
    }

    // Restore the OS-appropriate default after a test swaps the facade's implementation, so nothing leaks.
    private static void RestoreDefault() => DisplayEnumerator.Use(
        OperatingSystem.IsWindows() ? new WindowsDisplayEnumerator() : new AvaloniaScreensDisplayEnumerator());

    [Fact]
    public void Facade_DispatchesToInstalledImplementation()
    {
        var expected = new DisplayInfo(Number: 1, Name: "Fake", Width: 1920, Height: 1080, X: 0, Y: 0, IsPrimary: true);
        try
        {
            DisplayEnumerator.Use(new FakeEnumerator(expected));

            var result = DisplayEnumerator.Enumerate();

            Assert.Single(result);
            Assert.Equal(expected, result[0]);
        }
        finally { RestoreDefault(); }
    }

    [Fact]
    public void AvaloniaScreens_NullProvider_ReturnsEmpty_DoesNotThrow()
    {
        var sut = new AvaloniaScreensDisplayEnumerator(() => null);

        var result = sut.Enumerate();

        Assert.Empty(result);
    }

    [Fact]
    public void AvaloniaScreens_ThrowingProvider_ReturnsEmpty_DoesNotThrow()
    {
        var sut = new AvaloniaScreensDisplayEnumerator(() => throw new InvalidOperationException("no window yet"));

        var result = sut.Enumerate();

        Assert.Empty(result);
    }
}
