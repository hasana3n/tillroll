using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace CrossEscPos.Controls;

/// <summary>
/// Binds one radio button of a group to one value of an enum: checked when the bound value equals the
/// parameter (an enum member name), and checking it writes that member back. Unchecking writes nothing, so the
/// newly checked button in the group decides the value.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Enum e && parameter is string name && e.ToString() == name;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is not string name)
            return BindingOperations.DoNothing;
        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return Enum.Parse(enumType, name);
    }
}
