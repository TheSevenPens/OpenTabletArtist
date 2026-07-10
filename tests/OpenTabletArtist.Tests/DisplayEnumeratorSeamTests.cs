using System.Collections.Generic;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Covers the <see cref="DisplayEnumerator"/> platform-seam facade (#140): that <c>Use</c> overrides
/// dispatch, that resetting returns to the OS default, and that the non-Windows fallback is null-safe.
/// </summary>
public class DisplayEnumeratorSeamTests
{
    private sealed class FakeEnumerator : IDisplayEnumerator
    {
        private readonly IReadOnlyList<DisplayInfo> _displays;
        public int Calls { get; private set; }
        public FakeEnumerator(params DisplayInfo[] displays) => _displays = displays;
        public IReadOnlyList<DisplayInfo> Enumerate() { Calls++; return _displays; }
    }

    private static DisplayInfo Display(int number) => new(
        Number: number, Name: $"Display {number}", Width: 1920, Height: 1080,
        X: 0, Y: 0, IsPrimary: number == 1, RefreshHz: 60, Port: "HDMI", Gpu: "Test GPU");

    [Fact]
    public void Use_RoutesEnumerateToTheInjectedImplementation()
    {
        var fake = new FakeEnumerator(Display(1), Display(2));
        try
        {
            DisplayEnumerator.Use(fake);

            var result = DisplayEnumerator.Enumerate();

            Assert.Equal(2, result.Count);
            Assert.Equal(1, fake.Calls);
            Assert.Equal("Display 1", result[0].Name);
        }
        finally
        {
            DisplayEnumerator.Use(null);
        }
    }

    [Fact]
    public void Use_Null_ResetsToTheOsDefault()
    {
        DisplayEnumerator.Use(new FakeEnumerator(Display(1)));
        DisplayEnumerator.Use(null);

        // With no injected impl the facade builds the OS default. On Windows that enumerates the real
        // monitors; off-Windows it's the Avalonia-Screens impl, which returns an empty list when there's no
        // running app/window (as in tests). Either way the call must not throw or return null.
        var result = DisplayEnumerator.Enumerate();

        Assert.NotNull(result);
    }

    // The cross-platform impl (macOS/Linux) reads Avalonia's Screens off a window. With no window/screens it
    // must degrade to an empty list, never throw — the same contract as the Windows path (#140, Phase 1).

    [Fact]
    public void AvaloniaScreens_NullProvider_ReturnsEmpty_NeverNull()
    {
        var sut = new AvaloniaScreensDisplayEnumerator(() => null);

        var result = sut.Enumerate();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void AvaloniaScreens_ThrowingProvider_ReturnsEmpty_DoesNotThrow()
    {
        var sut = new AvaloniaScreensDisplayEnumerator(
            () => throw new System.InvalidOperationException("no window yet"));

        var result = sut.Enumerate();

        Assert.Empty(result);
    }
}
