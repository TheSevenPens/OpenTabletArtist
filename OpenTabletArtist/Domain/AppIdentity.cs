namespace OpenTabletArtist.Domain;

/// <summary>Identity of a foreground application, for per-app profile matching (#167). <see cref="ExePath"/>
/// is the full image path when it could be read (empty for elevated/UWP apps that deny the query);
/// <see cref="ExeName"/> is the process name (with .exe) and is the portable fallback for matching.</summary>
public record AppIdentity(string ExePath, string ExeName);
