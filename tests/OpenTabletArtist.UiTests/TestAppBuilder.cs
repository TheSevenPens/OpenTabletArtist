using Avalonia;
using Avalonia.Headless;
using OpenTabletArtist;

[assembly: AvaloniaTestApplication(typeof(OpenTabletArtist.UiTests.TestAppBuilder))]

namespace OpenTabletArtist.UiTests;

/// <summary>
/// Builds the real <see cref="App"/> on a headless platform, so these tests exercise the application's
/// own styles, resources and templates rather than a stand-in. Without the real App, a view that depends
/// on a StaticResource the app defines would "pass" here and break in the product (#741).
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .WithInterFont();
}
