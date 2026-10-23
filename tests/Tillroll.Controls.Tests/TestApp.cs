using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Tillroll.Controls.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Tillroll.Controls.Tests;

/// <summary>Minimal Avalonia app for headless control tests — just the Fluent theme.</summary>
public class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public static class TestAppBuilder
{
    // Real Skia drawing (UseHeadlessDrawing = false) so receipt PNGs actually decode into bitmaps.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
