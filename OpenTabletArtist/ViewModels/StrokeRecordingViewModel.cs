using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

public enum RecordingPhase
{
    /// <summary>Not recording. A finished recording's file name is shown here until the next one starts.</summary>
    Idle,

    /// <summary>Collecting every pen report from the tablet that was chosen when Record was pressed.</summary>
    Recording,

    /// <summary>Stopped. Showing what the recording holds and asking who made it before it is saved.</summary>
    Review,
}

/// <summary>
/// The Scribble page's Record mode: record the pen reports, look at what they came to, and save them as a
/// Stroke Corpus recording. Pure state and commands, so it can be tested without a window.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Scribble page shows only Record, Stop and the live clock.</b> Stopping opens a dialog over this view model
/// (<see cref="IDialogService.ShowRecordingReviewAsync"/>) with the results, the form and the saved file name, and
/// when that dialog is closed everything is dismissed and the page is as it was before Record.
/// </para>
/// <para>
/// <b>The recording is fed from the daemon's receive thread</b>, not from the page's sample path. Record attaches a
/// <see cref="StrokeReportAdmission"/> to the stream and Stop detaches it, so the boundary of a recording is the
/// moment a report arrived: one that was received before Stop is in the take even if the UI had not drawn it yet, and
/// one from another tablet never gets in.
/// </para>
/// <para>
/// The tablet and driver are fixed when recording starts, not when it is saved: a recording that changed what it
/// was recorded on half way through would describe neither. If the tablet's own specifications change under it, the
/// recording ends there. What the review panel shows is exactly what will be written.
/// </para>
/// <para>
/// "Keep every airborne reading" is chosen at review, not before, because the reports are kept whole and the
/// capture policy is applied afterwards. Nothing the person wanted is lost by having left it off while drawing.
/// </para>
/// </remarks>
public partial class StrokeRecordingViewModel : ObservableObject
{
    private const string Gesture = "freeform";
    private const string Intent = "Freeform drawing on OpenTabletArtist's Scribble page.";

    private readonly Func<StrokeRecordingContext?> _context;
    private readonly Action<Action<JObject, PenSample>?> _setTap;
    private readonly Func<StrokeRecordingViewModel, Task>? _showReview;
    private readonly Func<string, Task>? _showProblem;
    private readonly Func<string> _folder;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<string> _reveal;
    private readonly Func<string, string?>? _rememberedFirmware;
    private readonly Action<string, string?>? _rememberFirmware;
    private readonly Func<string, string?>? _rememberedPen;
    private readonly Action<string, string?>? _rememberPen;

    private StrokeRecordingSession? _session;
    private StrokeReportAdmission? _admission;
    private StrokeRecordingContext? _recordedOn;
    private DateTimeOffset _startedAt;

    /// <param name="context">The tablet to record and what it says about itself, or null if there isn't one.</param>
    /// <param name="setTap">Attaches (or, given null, detaches) the recording to the daemon's report stream.</param>
    /// <param name="showReview">Shows the review-and-save dialog for this recording and completes when it is closed.
    /// Without it the recording simply waits at review.</param>
    /// <param name="showProblem">Tells the person why recording could not start, away from the Scribble page.</param>
    /// <param name="rememberedFirmware">The firmware last saved for a tablet, by its name, or null if none. Without it (and
    /// without <paramref name="rememberFirmware"/>) nothing is remembered between recordings beyond what is typed.</param>
    /// <param name="rememberFirmware">Stores the firmware for a tablet after a recording of it is saved; null clears it.</param>
    /// <param name="rememberedPen">The pen last saved with a tablet, by its name, or null if none: like the firmware, so it
    /// is never another tablet's.</param>
    /// <param name="rememberPen">Stores the pen for a tablet after a recording of it is saved; null clears it.</param>
    public StrokeRecordingViewModel(
        Func<StrokeRecordingContext?> context,
        Action<Action<JObject, PenSample>?> setTap,
        Func<StrokeRecordingViewModel, Task>? showReview = null,
        Func<string, Task>? showProblem = null,
        Func<string>? folder = null,
        Func<DateTimeOffset>? now = null,
        Action<string>? reveal = null,
        Func<string>? accountName = null,
        Func<string, string?>? rememberedFirmware = null,
        Action<string, string?>? rememberFirmware = null,
        Func<string, string?>? rememberedPen = null,
        Action<string, string?>? rememberPen = null)
    {
        _rememberedFirmware = rememberedFirmware;
        _rememberFirmware = rememberFirmware;
        _rememberedPen = rememberedPen;
        _rememberPen = rememberPen;
        _username = (accountName ?? AccountName)().Trim();
        _context = context;
        _setTap = setTap;
        _showReview = showReview;
        _showProblem = showProblem;
        _folder = folder ?? DefaultFolder;
        _now = now ?? (() => DateTimeOffset.Now);
        _reveal = reveal ?? PlatformShell.RevealInFileManager;
    }

    /// <summary>Where recordings go unless told otherwise: Documents/OpenTabletArtist/Recordings.</summary>
    public static string DefaultFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OpenTabletArtist", "Recordings");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsRecording), nameof(IsReview))]
    private RecordingPhase _phase = RecordingPhase.Idle;

    public bool IsIdle => Phase == RecordingPhase.Idle;
    public bool IsRecording => Phase == RecordingPhase.Recording;
    public bool IsReview => Phase == RecordingPhase.Review;

    /// <summary>The live clock while recording. The only thing of this recording the Scribble page shows.</summary>
    [ObservableProperty] private string _statusText = "";

    /// <summary>What the stopped recording holds, with every report accounted for. Read in the review dialog.</summary>
    [ObservableProperty] private string _ledgerText = "";

    /// <summary>The last problem the review dialog should show: the recording ended early, or the file could not be
    /// written. Cleared when a recording starts or is dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = "";
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Where the recording was saved, for the dialog's closing screen. Cleared when the dialog is dismissed;
    /// the Scribble page never shows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaved))]
    private string _savedPath = "";
    public bool HasSaved => SavedPath.Length > 0;

    [ObservableProperty] private bool _keepAirborne;

    /// <summary>
    /// Which tablet this recording is of, as the file will name it. Read-only in the dialog: it is fixed when Record is
    /// pressed, so that what the person sees is what is written, and a recording cannot be relabelled as another tablet.
    /// </summary>
    [ObservableProperty] private string _tablet = "";

    // Typed in by the person, as in the other recorder: the driver can't say which firmware the tablet runs, and
    // a recording says who made it. They stay filled between recordings; they are the same person on the same desk.
    // The name starts as the account name of whoever is signed in (the short name, not their full name) and is theirs
    // to change: the file is something they may publish, so what it says about them is their decision.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirmwareNote), nameof(HasFirmwareNote))]
    private string _firmware = "";
    [ObservableProperty] private string _username;
    [ObservableProperty] private string _notes = "";

    // What the person calls this recording, optional. Unlike the firmware and the notes it is not kept between
    // recordings: a name belongs to one take, and the next one would otherwise open with this one's.
    [ObservableProperty] private string _name = "";

    // Which pen it was made with, typed by the person (the driver says nothing about the pen). Remembered per tablet,
    // as the firmware is, because it is usually the pen that came with that tablet. Optional: an empty box is simply
    // absent from the file, so unlike the firmware it has no "not recorded" nag.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PenNote), nameof(HasPenNote))]
    private string _pen = "";

    /// <summary>
    /// Said under the Your name box, where the question of what a recording gives away about you is asked. The name
    /// starts as the account name on this computer, which for many people is their real name or a work login, and
    /// everything typed on this form goes into the file. This does not say the file is public, because OpenTabletArtist
    /// keeps it on this computer: it says what happens if the person contributes it to the Stroke Corpus.
    /// </summary>
    public string PublicationNotice =>
        "Your name, the recording's name, the firmware, the pen and your notes are all saved in the recording. "
        + "If you contribute it to the Stroke Corpus, it is published with all of them, under CC BY 4.0.";

    /// <summary>The signed-in account's name. Anything that goes wrong reading it leaves the field empty to type in.</summary>
    private static string AccountName()
    {
        try { return Environment.UserName ?? ""; }
        catch (Exception) { return ""; }
    }

    // The firmware the dialog opened with because it was remembered for this tablet, so the note can say where it came from
    // for as long as it is still what is in the box.
    private string _fromMemory = "";

    /// <summary>
    /// What to say under the firmware box. A recording is evidence about one firmware, and a remembered value can be
    /// stale (flash the tablet and it is wrong) while looking exactly like a value typed just now. So the note says where it
    /// came from, and says plainly when there is none.
    /// </summary>
    public string FirmwareNote
    {
        get
        {
            var typed = Firmware.Trim();

            if (typed.Length == 0)
            {
                return "Not recorded. Say which firmware this tablet is running: recordings from different firmware can't be compared fairly without it.";
            }

            return typed == _fromMemory
                ? "Remembered from your last recording of this tablet. Change it if the firmware has changed."
                : "";
        }
    }

    public bool HasFirmwareNote => FirmwareNote.Length > 0;

    // The pen the dialog opened with because it was remembered for this tablet, so the note can say where it came from
    // for as long as it is still what is in the box.
    private string _penFromMemory = "";

    /// <summary>Under the Pen box: where a remembered pen came from, since a different pen is easy to forget to change.
    /// Empty for a pen typed just now, and for no pen at all (it is optional).</summary>
    public string PenNote =>
        Pen.Trim() is { Length: > 0 } typed && typed == _penFromMemory
            ? "Remembered from your last recording of this tablet. Change it if you are using a different pen."
            : "";

    public bool HasPenNote => PenNote.Length > 0;

    partial void OnKeepAirborneChanged(bool value) => Summarize();

    /// <summary>On the page's refresh timer: updates the live clock, and ends a recording that cannot go on.</summary>
    public void Tick()
    {
        if (Phase != RecordingPhase.Recording || _session is null || _admission is null) return;

        if (_admission.SpecificationsChanged)
        {
            ErrorText = "Recording stopped: the tablet's specifications changed, so what was recorded so far "
                        + "and what came after it would not be in the same units.";
            Stop();
            return;
        }

        if (_session.Truncated)
        {
            Stop();
            return;
        }

        var strokes = _session.StrokeCount;
        StatusText = $"Recording · {_session.ElapsedSeconds:0.0} s · {strokes:N0} {(strokes == 1 ? "stroke" : "strokes")} · {_session.Count:N0} reports";
    }

    [RelayCommand]
    private void Start()
    {
        ErrorText = "";

        if (_context() is not { } context)
        {
            ErrorText = "No tablet to record. Plug one in, and make sure its specifications are available.";

            // Said in a message of its own, not on the Scribble page, which has nowhere to put it.
            if (_showProblem is not null) _ = _showProblem(ErrorText);
            return;
        }

        _recordedOn = context;
        Tablet = context.Tablet;
        LoadRememberedFirmware(context.Tablet);
        LoadRememberedPen(context.Tablet);
        _session = new StrokeRecordingSession(context.FullScalePressure);
        _admission = new StrokeReportAdmission(context, _session);
        _startedAt = _now();
        SavedPath = "";
        LedgerText = "";
        StatusText = "Recording · waiting for the pen";
        Phase = RecordingPhase.Recording;

        // Last, so the first report a recording sees is one that arrived after the person pressed Record.
        _setTap(_admission.Offer);
    }

    [RelayCommand]
    private void Stop()
    {
        if (Phase != RecordingPhase.Recording || _session is null) return;

        // Detach first, then stop: a report in flight on the receive thread is either in the take or counted after
        // the stop, never lost between the two (the session's lock decides which).
        _setTap(null);
        _session.Stop();
        Phase = RecordingPhase.Review;
        StatusText = "";
        Summarize();

        _ = ReviewAsync();
    }

    /// <summary>
    /// Shows the review dialog and, when it is closed, returns everything to how it was before Record. The Scribble
    /// page keeps nothing of a finished recording: not the results, not the file name.
    /// </summary>
    private async Task ReviewAsync()
    {
        if (_showReview is null) return;

        try
        {
            await _showReview(this);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Dismiss();
            if (_showProblem is not null) await _showProblem($"The recording couldn't be shown for saving, so it was discarded: {ex.Message}");
            return;
        }

        Dismiss();
    }

    /// <summary>
    /// Back to idle with nothing left over: a recording still waiting at review is discarded (the dialog has asked
    /// first), and the saved path and any message are cleared. Safe to call at any time.
    /// </summary>
    public void Dismiss()
    {
        if (Phase == RecordingPhase.Review) Discard();

        SavedPath = "";
        ErrorText = "";
    }

    [RelayCommand]
    private void Discard()
    {
        _setTap(null);
        _session = null;
        _admission = null;
        _recordedOn = null;
        Tablet = "";
        Name = "";   // one take's name, not the next one's
        LedgerText = "";
        StatusText = "";
        ErrorText = "";
        Phase = RecordingPhase.Idle;
    }

    [RelayCommand]
    private void Save()
    {
        if (_session is null || _admission is null || _recordedOn is not { } on) return;

        var take = _session.Segment(KeepAirborne);

        if (take.Strokes.Count == 0 && !KeepAirborne)
        {
            ErrorText = "Nothing was drawn. Tick \"Keep every airborne reading\" to save the pen's hovering, or discard it.";
            return;
        }

        var description = new TakeDescription(
            Id: "",
            Gesture: Gesture,
            Intent: Intent,
            Username: Username.Trim(),
            Notes: NotesWithWhatWasTurnedAway(),
            RecordedAt: _startedAt,
            Device: new TakeDevice(
                on.Tablet, on.Driver, Firmware.Trim(), "OpenTabletDriver DeviceReport", on.FullScalePressure,
                StrokeRecordingSession.Conventions,
                Pen: Pen.Trim().Length > 0 ? Pen.Trim() : null),
            Space: on.Space,
            Channels: _session.Channels,
            Name: Name.Trim().Length > 0 ? Name.Trim() : null);

        try
        {
            var path = StrokeTakeWriter.Write(
                take, description, _folder(), StrokeTakeWriter.Suggest(Gesture, on.Tablet, _startedAt));

            RememberFirmwareFor(on.Tablet);
            RememberPenFor(on.Tablet);

            Discard();
            SavedPath = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or ArgumentException or NotSupportedException)
        {
            // Nothing is kept of a failed save (the writer cleans up after itself), and the recording is still here to try again.
            ErrorText = $"Couldn't save the recording: {ex.Message}";
        }
    }

    /// <summary>Opens the dialog with the firmware this tablet was last saved with, or empty, so it is never another
    /// tablet's. With no memory configured the box is left as it was.</summary>
    private void LoadRememberedFirmware(string tablet)
    {
        if (_rememberedFirmware is null) return;

        string? remembered = null;
        try { remembered = _rememberedFirmware(tablet); }
        catch (Exception) { /* a settings file that cannot be read leaves the box empty to type in */ }

        _fromMemory = remembered?.Trim() ?? "";
        Firmware = _fromMemory;
        OnPropertyChanged(nameof(FirmwareNote));
        OnPropertyChanged(nameof(HasFirmwareNote));
    }

    /// <summary>Remembers what the recording was saved with, or forgets it if the box was cleared. Never stops a save.</summary>
    private void RememberFirmwareFor(string tablet)
    {
        if (_rememberFirmware is null) return;

        var typed = Firmware.Trim();
        try { _rememberFirmware(tablet, typed.Length == 0 ? null : typed); }
        catch (Exception) { /* the recording is saved; failing to remember is not worth losing the dialog over */ }
    }

    /// <summary>Opens the dialog with the pen this tablet was last saved with, or empty, so it is never another tablet's.
    /// With no memory configured the box is left as it was.</summary>
    private void LoadRememberedPen(string tablet)
    {
        if (_rememberedPen is null) return;

        string? remembered = null;
        try { remembered = _rememberedPen(tablet); }
        catch (Exception) { /* a settings file that cannot be read leaves the box empty to type in */ }

        _penFromMemory = remembered?.Trim() ?? "";
        Pen = _penFromMemory;
        OnPropertyChanged(nameof(PenNote));
        OnPropertyChanged(nameof(HasPenNote));
    }

    /// <summary>Remembers the pen the recording was saved with, or forgets it if the box was cleared. Never stops a save.</summary>
    private void RememberPenFor(string tablet)
    {
        if (_rememberPen is null) return;

        var typed = Pen.Trim();
        try { _rememberPen(tablet, typed.Length == 0 ? null : typed); }
        catch (Exception) { /* the recording is saved; failing to remember is not worth losing the dialog over */ }
    }

    [RelayCommand]
    private void RevealFolder()
    {
        if (HasSaved) _reveal(Path.GetDirectoryName(SavedPath)!);
    }

    /// <summary>
    /// The person's notes, followed by anything the recording turned away. A file that left reports out should say
    /// so itself: the ledger counts what was recorded, and these are the reports that never reached it.
    /// </summary>
    private string NotesWithWhatWasTurnedAway()
    {
        var parts = new List<string>();
        if (Notes.Trim() is { Length: > 0 } typed) parts.Add(typed);

        var inv = CultureInfo.InvariantCulture;

        if (_session!.Dropped > 0)
        {
            parts.Add($"Recording reached the {StrokeRecordingSession.MaxReadings.ToString("N0", inv)}-report limit and "
                      + $"{_session.Dropped.ToString("N0", inv)} later reports were dropped.");
        }

        if (_admission!.IgnoredOtherTablet > 0)
        {
            parts.Add($"{_admission.IgnoredOtherTablet.ToString("N0", inv)} reports from other devices were ignored.");
        }

        if (_admission.IgnoredNotPen > 0)
        {
            parts.Add($"{_admission.IgnoredNotPen.ToString("N0", inv)} reports from this tablet were not complete pen "
                      + "measurements and were ignored.");
        }

        if (_admission.SpecificationsChanged)
        {
            parts.Add("Recording ended because the tablet's specifications changed.");
        }

        return string.Join(" ", parts);
    }

    private void Summarize()
    {
        if (_session is null || _admission is null || Phase != RecordingPhase.Review) return;

        var take = _session.Segment(KeepAirborne);
        var l = take.Ledger;
        var strokes = take.Strokes.Count;

        // One line per place a report can end up, numbers lined up, so the totals can be checked by eye. The
        // dialog shows this in a monospace font. Interpolation formats with the current culture, which is right for
        // a number read off the screen.
        var width = Math.Max(l.Routed, _session.Dropped).ToString("N0").Length;
        string Row(long n, string what) => $"{n.ToString("N0").PadLeft(width)}  {what}";

        var lines = new List<string>
        {
            $"{strokes:N0} {(strokes == 1 ? "stroke" : "strokes")} over {_session.Seconds:0.0} s",
            "",
            Row(l.Routed, "reports in total"),
            Row(l.Contact, "  in strokes"),
            Row(l.KeptAlongside, "  hovering, kept with a stroke"),
            Row(l.RetainedAirborne, "  hovering, kept as the airborne record"),
            Row(l.ExcludedAirborne, "  hovering, left out"),
            Row(l.AfterTheStop, "  after the stop"),
        };

        if (!l.Balances) lines.Add($"DOES NOT BALANCE: {l.Accounted:N0} of {l.Routed:N0} accounted for");

        var turnedAway = new List<string>();
        if (_session.Dropped > 0) turnedAway.Add(Row(_session.Dropped, "dropped at the limit"));
        if (_admission.IgnoredOtherTablet > 0) turnedAway.Add(Row(_admission.IgnoredOtherTablet, "from other devices"));
        if (_admission.IgnoredNotPen > 0) turnedAway.Add(Row(_admission.IgnoredNotPen, "not pen measurements"));

        if (turnedAway.Count > 0)
        {
            lines.Add("");
            lines.Add("Not recorded:");
            lines.AddRange(turnedAway);
        }

        LedgerText = string.Join("\n", lines);
    }
}
