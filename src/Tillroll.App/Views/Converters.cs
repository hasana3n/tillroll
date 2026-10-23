using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Tillroll.App.Transports;
using Tillroll.Controls;

namespace Tillroll.App.Views;

/// <summary>Looks up an application resource by key, or null when the app's theme isn't loaded (tests, previewer).</summary>
internal static class ThemeResources
{
    public static object? Find(string key)
        => Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value) ? value : null;
}

/// <summary>The icon geometry for a <see cref="TransportKind"/>.</summary>
public sealed class TransportIconConverter : IValueConverter
{
    public static readonly TransportIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TransportKind.Bluetooth => ThemeResources.Find("Icon.Bluetooth"),
        TransportKind.Cable => ThemeResources.Find("Icon.Cable"),
        TransportKind.Browser => ThemeResources.Find("Icon.Globe"),
        _ => ThemeResources.Find("Icon.Network"),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>The state colour for a <see cref="PrinterHealth"/>: green ready, amber warning, red blocked.</summary>
public sealed class HealthBrushConverter : IValueConverter
{
    public static readonly HealthBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PrinterHealth.Blocked => ThemeResources.Find("NegativeBrush"),
        PrinterHealth.Warning => ThemeResources.Find("WarningBrush"),
        _ => ThemeResources.Find("PositiveBrush"),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
