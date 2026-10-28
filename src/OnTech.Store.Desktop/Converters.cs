using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OnTech.Store.Desktop;

/// <summary>true → Visible. Pass ConverterParameter=Invert to flip.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter as string == "Invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Non-null and non-empty → Visible. Pass ConverterParameter=Invert to flip.</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value is string s ? !string.IsNullOrWhiteSpace(s) : value is int n ? n != 0 : value is not null;
        if (parameter as string == "Invert") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Shows a server time (UTC offset) in this PC's local time.</summary>
public sealed class LocalTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DateTimeOffset d => d.ToLocalTime().ToString(parameter as string ?? "dd MMM yyyy HH:mm", culture),
        _ => null,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A user's access in a few words, for the users table.</summary>
public sealed class UserAccessConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Contracts.UserDto u ? ViewModels.UsersViewModel.Describe(u) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>[CanCancel (bool), Status] → Visible when the user may cancel and the order isn't cancelled yet.</summary>
public sealed class CanCancelOrderConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is true && values[1] is Contracts.OrderStatus.Completed ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Shows an enum value (e.g. an order status) or a fixed English word in the current language.</summary>
public sealed class TranslateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? null : Services.L.T(value.ToString()!);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A stock movement type in plain words, in the current language (for the item card).</summary>
public sealed class MovementTypeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Contracts.StockMovementType t ? ViewModels.ItemCardViewModel.Describe(t) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Shows 0 as an empty cell, so an in/out column only shows the side that moved.</summary>
public sealed class ZeroAsBlankConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n != 0 ? n.ToString(culture) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
