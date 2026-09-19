using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// What the active-area diagram draws after a drag ends (#815).
///
/// Committing an area is asynchronous — it mutates the profile, sends to the daemon, and the session
/// reloads before the call returns — so the bound area does not catch up for a round trip. The control
/// used to drop its drag preview the instant the pointer came up, which drew the next frame from a value
/// from before the drag: the area visibly jumped back to where it started and sat there until the commit
/// landed. Guaranteed, not a race.
///
/// No view-model test could see it. The values the editor published were correct and arrived in one step;
/// what was wrong was which of them this control chose to draw, and for how long.
/// </summary>
public class ActiveAreaDiagramTests
{
    /// <summary>A diagram in a shown headless window, large enough to drag inside.</summary>
    private static (Window, ActiveAreaDiagram) Shown(TabletAreaInfo area)
    {
        var diagram = new ActiveAreaDiagram { Area = area, Editable = true };
        var window = new Window { Width = 400, Height = 300, Content = diagram };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, diagram);
    }

    private static TabletAreaInfo Area(double w, double h, double cx, double cy) =>
        new(FullWidth: 200, FullHeight: 150, EffWidth: w, EffHeight: h, EffCenterX: cx, EffCenterY: cy,
            HasDisplay: true, DisplayNumber: 1, DisplayName: "D", DisplayWidth: 1920, DisplayHeight: 1080,
            Rotation: 0);

    /// <summary>With no drag in progress, the bound area is what is drawn.</summary>
    [AvaloniaFact]
    public void WithNoDrag_ItDrawsTheBoundArea()
    {
        var d = new ActiveAreaDiagram { Area = Area(100, 75, 100, 75) };

        Assert.Equal((100d, 75d, 100d, 75d), d.Effective);
    }

    /// <summary>Nothing to draw without an area; the control shows its own "no data" state instead.</summary>
    [AvaloniaFact]
    public void WithNoArea_ThereIsNothingToDraw()
    {
        Assert.Null(new ActiveAreaDiagram().Effective);
    }

    /// <summary>
    /// After the pointer comes up, the diagram still shows what was dragged.
    ///
    /// <b>This is the test the fix exists for.</b> The three above pass with the defect restored, because
    /// none of them drags — they only exercise the paths where no preview exists. Drop the preview on
    /// release and this one fails, showing the pre-drag values.
    /// </summary>
    [AvaloniaFact]
    public void AfterTheDragEnds_ItStillShowsWhatWasDragged()
    {
        var (window, diagram) = Shown(Area(100, 75, 100, 75));

        var committed = 0;
        diagram.AreaCommitted += (_, _) => committed++;

        var centre = new Point(200, 150);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseMove(centre + new Vector(40, 0));
        window.MouseUp(centre + new Vector(40, 0), MouseButton.Left);

        Assert.Equal(1, committed);                       // the edit was submitted
        var drawn = Assert.IsType<(double, double, double, double)>(diagram.Effective);
        Assert.NotEqual(100d, drawn.Item3);               // ...and the centre moved with the pointer

        // The commit is asynchronous. Until the model answers, what the user dragged is what they see --
        // it must not snap back to where the drag started.
        Assert.NotEqual(100d, diagram.Effective!.Value.CenterX);
    }

    /// <summary>
    /// The model landing is what retires the preview — and any change retires it, not only one matching
    /// what was dragged.
    ///
    /// Policy and clamping can legitimately alter a request, and the stored value is the truth in every
    /// case. Continuing to show the request after the answer arrived would be showing the user something
    /// that is not so.
    /// </summary>
    [AvaloniaFact]
    public void WhenTheBoundAreaChanges_ThePreviewRetires()
    {
        var d = new ActiveAreaDiagram { Area = Area(100, 75, 100, 75) };

        // What a commit produces: the stored area becomes something the control did not itself choose.
        d.Area = Area(80, 60, 120, 90);

        Assert.Equal((80d, 60d, 120d, 90d), d.Effective);
    }
}
