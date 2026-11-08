using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using OnTech.Store.Client;

namespace OnTech.Store.Desktop.Services;

/// <summary>
/// The app's language. Text is written in English everywhere and looked up in <see cref="Arabic"/> when Arabic is on;
/// anything without a translation falls back to the English text. Windows read the language when they open,
/// so changing it reopens the open window (see App).
/// </summary>
public static class L
{
    public const string English = "en";
    public const string ArabicCode = "ar";

    private static readonly CultureInfo StartupCulture = CultureInfo.CurrentCulture;
    private static readonly CultureInfo StartupUiCulture = CultureInfo.CurrentUICulture;
    // Egyptian Arabic uses the Gregorian calendar, so dates read the same as in English.
    private static readonly CultureInfo ArabicCulture = CultureInfo.GetCultureInfo("ar-EG");

    public static bool IsArabic { get; private set; }
    public static string Code => IsArabic ? ArabicCode : English;
    public static FlowDirection FlowDirection => IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    public static CultureInfo Culture => IsArabic ? ArabicCulture : StartupCulture;

    /// <summary>Name of the other language, for the switch button.</summary>
    public static string OtherLanguageName => IsArabic ? "English" : "العربية";

    public static void Set(string? code)
    {
        IsArabic = code == ArabicCode;
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = IsArabic ? ArabicCulture : StartupUiCulture;
    }

    /// <summary>Switches to the other language and remembers it on this PC.</summary>
    public static void Switch(AppSettings settings)
    {
        Set(IsArabic ? English : ArabicCode);
        settings.Language = Code;
        settings.Save();
    }

    /// <summary>The text in the current language.</summary>
    public static string T(string english) =>
        IsArabic && Arabic.Text.TryGetValue(english, out var ar) ? ar : english;

    /// <summary>Translates <paramref name="englishFormat"/>, then fills in {0}, {1}, ...</summary>
    public static string T(string englishFormat, params object?[] args) => string.Format(Culture, T(englishFormat), args);

    /// <summary>
    /// The text to show for a failed server call. Validation errors are shown per field with the field's label
    /// ("Company name can be at most 200 characters."), in Arabic when Arabic is on; anything else goes through <see cref="Message"/>.
    /// </summary>
    public static string Error(ApiError error) =>
        error.FieldErrors.Count == 0
            ? Message(error.Message)
            : string.Join(Environment.NewLine, error.FieldErrors.Select(FieldText).Distinct());

    public static string FieldText(FieldError e) =>
        e.Kind == FieldErrorKind.Other || e.Label.Length == 0
            ? Message(e.Raw)
            : T(FieldError.Template(e.Kind), T(e.Label), e.Limit, e.Limit2);

    /// <summary>Translates a message from the server or the API client, line by line (some carry names or numbers).</summary>
    public static string Message(string message)
    {
        if (!IsArabic || string.IsNullOrEmpty(message)) return message;
        return string.Join(Environment.NewLine, message.Split(["\r\n", "\n"], StringSplitOptions.None).Select(TranslateLine));
    }

    private static string TranslateLine(string line)
    {
        if (Arabic.Text.TryGetValue(line, out var ar)) return ar;
        foreach (var (pattern, replacement) in Arabic.Patterns)
        {
            var m = pattern.Match(line);
            if (m.Success) return m.Result(replacement);
        }
        return line;
    }

    /// <summary>Sets a window's reading direction and language (dates, month names). Call before it is shown.</summary>
    public static void Apply(FrameworkElement element)
    {
        element.FlowDirection = FlowDirection;
        element.Language = XmlLanguage.GetLanguage(Culture.IetfLanguageTag);
    }

    /// <summary>Right-to-left reading for message boxes when Arabic is on.</summary>
    public static MessageBoxOptions MessageBoxOptions =>
        IsArabic ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None;
}

/// <summary>XAML text in the current language: <c>Text="{l:T Sign in}"</c>, or <c>{l:T 'Text, with a comma'}</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}
