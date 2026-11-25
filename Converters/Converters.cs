// Converters.cs — XAML Value Converters
using System.Globalization;

namespace Burudani.Converters;

public class BoolToColorConverter : IValueConverter
{
    public Color TrueColor { get; set; } = Color.FromArgb("#ea580c");
    public Color FalseColor { get; set; } = Color.FromArgb("#71717a");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueColor : FalseColor;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class InvertedBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : false;
}

public class NullOrEmptyToBoolConverter : IValueConverter
{
    public bool Invert { get; set; } = false;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isEmpty = value == null || (value is string s && string.IsNullOrWhiteSpace(s));
        return Invert ? isEmpty : !isEmpty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class PlayPauseIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Helpers.MaterialIcon.Pause : Helpers.MaterialIcon.Play;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class EqualToColorConverter : IValueConverter
{
    public Color TrueColor { get; set; } = Color.FromArgb("#ea580c");
    public Color FalseColorDark { get; set; } = Color.FromArgb("#18181b");
    public Color FalseColorLight { get; set; } = Color.FromArgb("#e2e8f0");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool eq = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        if (eq) return TrueColor;
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Dark;
        return theme == AppTheme.Light ? FalseColorLight : FalseColorDark;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class EqualToTextColorConverter : IValueConverter
{
    public Color TrueColor { get; set; } = Color.FromArgb("#ffffff");
    public Color FalseColorDark { get; set; } = Color.FromArgb("#a1a1aa");
    public Color FalseColorLight { get; set; } = Color.FromArgb("#475569");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool eq = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        if (eq) return TrueColor;
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Dark;
        return theme == AppTheme.Light ? FalseColorLight : FalseColorDark;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "Completed" => Color.FromArgb("#22c55e"),
            "Downloading" => Color.FromArgb("#ea580c"),
            "Converting" => Color.FromArgb("#f59e0b"),
            "Failed" => Color.FromArgb("#ef4444"),
            "Cancelled" => Color.FromArgb("#71717a"),
            _ => Color.FromArgb("#94a3b8")
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class EqualToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null && parameter == null) return true;
        if (value == null || parameter == null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class IntEqualToColorConverter : IValueConverter
{
    public Color TrueColor { get; set; } = Color.FromArgb("#ea580c");
    public Color FalseColor { get; set; } = Colors.Transparent;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int vi && int.TryParse(parameter?.ToString(), out int pi))
            return vi == pi ? TrueColor : FalseColor;
        return FalseColor;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class IntEqualToTextColorConverter : IValueConverter
{
    public Color TrueColor { get; set; } = Colors.White;
    public Color FalseColorDark { get; set; } = Color.FromArgb("#94a3b8");
    public Color FalseColorLight { get; set; } = Color.FromArgb("#475569");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int vi && int.TryParse(parameter?.ToString(), out int pi))
        {
            if (vi == pi) return TrueColor;
        }
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Dark;
        return theme == AppTheme.Light ? FalseColorLight : FalseColorDark;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class RepeatModeIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Burudani.Services.RepeatMode rm && rm == Burudani.Services.RepeatMode.One)
            return Burudani.Helpers.MaterialIcon.RepeatOnce;
        return Burudani.Helpers.MaterialIcon.Repeat;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class RepeatModeColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Burudani.Services.RepeatMode rm && rm != Burudani.Services.RepeatMode.Off)
            return Color.FromArgb("#ea580c");

        var theme = Application.Current?.RequestedTheme ?? AppTheme.Dark;
        return theme == AppTheme.Light ? Color.FromArgb("#64748b") : Color.FromArgb("#94a3b8");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class GenreBgConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true) return Color.FromArgb("#ea580c");
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Dark;
        return theme == AppTheme.Light ? Color.FromArgb("#f1f5f9") : Color.FromArgb("#1e1e24");
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class GenreStrokeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true) return Color.FromArgb("#ea580c");
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Dark;
        return theme == AppTheme.Light ? Color.FromArgb("#cbd5e1") : Color.FromArgb("#2e2e38");
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class GenreTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true) return Colors.White;
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Dark;
        return theme == AppTheme.Light ? Color.FromArgb("#475569") : Color.FromArgb("#94a3b8");
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class VolumeIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isMuted && isMuted) return Helpers.MaterialIcon.VolumeOff;
        if (value is double vol && vol <= 0.001) return Helpers.MaterialIcon.VolumeOff;
        return Helpers.MaterialIcon.VolumeHigh;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
