using System;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The calibration overlay's two WndProc answers (#479). Both are one-line rules whose value is entirely
/// in <em>when</em> they apply, and getting either scope wrong is invisible until someone is holding a pen
/// on a target — so the scoping is pinned here rather than left to a hardware repro.
/// </summary>
public class CalibrationOverlayCursorTests
{
    private const uint WmTabletQuerySystemGestureStatus = 0x02CC;
    private const uint WmSetCursor = 0x0020;
    private const uint WmMouseMove = 0x0200;   // stand-in for "some other message"

    /// <summary>Windows asks once what system gestures this window wants, not per contact — so the
    /// press-and-hold opt-out cannot be conditional on a hold being underway.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GestureOptOut_IsAnswered_HoldOrNot(bool holding)
    {
        var response = CalibrationOverlayWindow.WndProcResponse(WmTabletQuerySystemGestureStatus, holding);

        Assert.Equal(new IntPtr(1), response); // TABLET_DISABLE_PRESSANDHOLD
    }

    /// <summary>The fix itself: answering WM_SETCURSOR during a hold means there is no frame in which
    /// Windows draws the arrow, which is what the 30fps re-hide could not guarantee.</summary>
    [Fact]
    public void SetCursor_IsClaimed_WhileHolding()
    {
        var response = CalibrationOverlayWindow.WndProcResponse(WmSetCursor, holding: true);

        Assert.Equal(new IntPtr(1), response); // TRUE — handled, stop processing
    }

    /// <summary>The scope that matters. Claiming WM_SETCURSOR unconditionally would leave the overlay
    /// with no visible pointer anywhere — including over the panel's Undo / Clear / Cancel buttons, which
    /// the user has to click to finish or abandon a calibration.</summary>
    [Fact]
    public void SetCursor_IsLeftToWindows_WhenNotHolding()
    {
        Assert.Null(CalibrationOverlayWindow.WndProcResponse(WmSetCursor, holding: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OtherMessages_AreNotIntercepted(bool holding)
    {
        Assert.Null(CalibrationOverlayWindow.WndProcResponse(WmMouseMove, holding));
    }
}
