using System;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

public enum RecordingPhase
{
    /// <summary>Not recording. A finished recording's file name is shown here until the next one starts.</summary>
    Idle,

    /// <summary>Collecting every pen report, drawn or hovering.</summary>
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
/// The tablet and driver are fixed when recording starts, not when it is saved: a recording that changed what it
/// was recorded on half way through would describe neither. The reports stop being collected at Stop, so what the
/// review panel shows is exactly what will be written.
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
    private readonly Func<string> _folder;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<string> _reveal;

    private StrokeRecordingSession? _session;
    private StrokeRecordingContext? _recordedOn;
    private DateTimeOffset _startedAt;

    public StrokeRecordingViewModel(
        Func<StrokeRecordingContext?> context,
        Func<string>? folder = null,
        Func<DateTimeOffset>? now = null,
        Action<string>? reveal = null)
    {
        _context = context;
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

    /// <summary>One line: the live clock while recording, or why recording could not start.</summary>
    [ObservableProperty] private string _statusText = "";

    /// <summary>What the stopped recording holds, with every report accounted for.</summary>
    [ObservableProperty] private string _ledgerText = "";

    /// <summary>The last problem, if any: no tablet to record, or the file could not be written.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))] private string _errorText = "";
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Where the last recording was saved, until the next one starts.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSaved))] private string _savedPath = "";
    public bool HasSaved => SavedPath.Length > 0;

    [ObservableProperty] private bool _keepAirborne;

    // Typed in by the person, as in the other recorder: the driver can't say which firmware the tablet runs, and
    // a recording says who made it. They stay filled between recordings; they are the same person on the same desk.
    [ObservableProperty] private string _firmware = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _notes = "";

    partial void OnKeepAirborneChanged(bool value) => Summarize();

    /// <summary>Per pen report, from the page's sample path. Does nothing unless recording.</summary>
    public void Add(PenSample s)
    {
        if (Phase == RecordingPhase.Recording) _session!.Add(s);
    }

    /// <summary>On the page's refresh timer: updates the live clock, and stops a recording that hit its limit.</summary>
    public void Tick()
    {
        if (Phase != RecordingPhase.Recording || _session is null) return;

        if (_session.Truncated)
        {
            Stop();
            return;
        }

        var strokes = _session.StrokeCount;
        StatusText = $"Recording · {_session.Seconds:0.0} s · {strokes:N0} {(strokes == 1 ? "stroke" : "strokes")} · {_session.Count:N0} reports";
    }

    [RelayCommand]
    private void Start()
    {
        ErrorText = "";

        if (_context() is not { } context)
        {
            ErrorText = "No tablet to record. Plug one in, and make sure its specifications are available.";
            return;
        }

        _recordedOn = context;
        _session = new StrokeRecordingSession(context.FullScalePressure);
        _startedAt = _now();
        SavedPath = "";
        LedgerText = "";
        StatusText = "Recording · waiting for the pen";
        Phase = RecordingPhase.Recording;
    }

    [RelayCommand]
    private void Stop()
    {
        if (Phase != RecordingPhase.Recording || _session is null) return;

        _session.Stop();
        Phase = RecordingPhase.Review;
        StatusText = "";
        Summarize();
    }

    [RelayCommand]
    private void Discard()
    {
        _session = null;
        _recordedOn = null;
        LedgerText = "";
        StatusText = "";
        ErrorText = "";
        Phase = RecordingPhase.Idle;
    }

    [RelayCommand]
    private void Save()
    {
        if (_session is null || _recordedOn is not { } on) return;

        var take = _session.Segment(KeepAirborne);

        if (take.Strokes.Count == 0 && !KeepAirborne)
        {
            ErrorText = "Nothing was drawn. Tick \"Keep every airborne reading\" to save the pen's hovering, or discard it.";
            return;
        }

        var notes = Notes.Trim();
        if (_session.Truncated)
        {
            notes = (notes + " Recording reached the " + StrokeRecordingSession.MaxReadings.ToString("N0", CultureInfo.InvariantCulture)
                     + "-report limit and later reports were dropped.").Trim();
        }

        var description = new TakeDescription(
            Id: "",
            Gesture: Gesture,
            Intent: Intent,
            Username: Username.Trim(),
            Notes: notes,
            RecordedAt: _startedAt,
            Device: new TakeDevice(
                on.Tablet, on.Driver, Firmware.Trim(), "OpenTabletDriver DeviceReport", on.FullScalePressure,
                StrokeRecordingSession.Conventions),
            Space: on.Space,
            Channels: _session.Channels);

        try
        {
            var path = StrokeTakeWriter.Write(
                take, description, _folder(), StrokeTakeWriter.Suggest(Gesture, on.Tablet, _startedAt));

            Discard();
            SavedPath = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText = $"Couldn't save the recording: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RevealFolder()
    {
        if (HasSaved) _reveal(Path.GetDirectoryName(SavedPath)!);
    }

    private void Summarize()
    {
        if (_session is null || Phase != RecordingPhase.Review) return;

        var take = _session.Segment(KeepAirborne);
        var l = take.Ledger;
        var strokes = take.Strokes.Count;

        // Interpolation formats with the current culture, which is right for a number read off the screen.
        LedgerText =
            $"{strokes:N0} {(strokes == 1 ? "stroke" : "strokes")} over {_session.Seconds:0.0} s. "
            + $"{l.Routed:N0} reports: {l.Contact:N0} in strokes · {l.KeptAlongside:N0} hovering kept with a stroke · "
            + $"{l.RetainedAirborne:N0} hovering kept as the airborne record · {l.ExcludedAirborne:N0} hovering left out · "
            + $"{l.AfterTheStop:N0} after the stop"
            + (l.Balances ? "." : $" — DOES NOT BALANCE ({l.Accounted:N0} accounted for).");
    }
}
