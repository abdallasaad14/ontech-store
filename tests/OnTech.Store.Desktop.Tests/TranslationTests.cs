using System.Text.RegularExpressions;
using OnTech.Store.Desktop.Services;
using Xunit;
using static OnTech.Store.Desktop.Tests.ValidationTests;

namespace OnTech.Store.Desktop.Tests;

/// <summary>CLAUDE.md: every L.T / {l:T} text and every server message the app can show has an Arabic entry.</summary>
public class TranslationTests
{
    private static IEnumerable<string> Sources(string pattern) =>
        Directory.GetFiles(DesktopDir, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    public static IEnumerable<string> CodeTexts() =>
        Sources("*.cs").Where(f => !f.EndsWith("Arabic.cs"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"L\.T\(\s*""((?:[^""\\]|\\.)*)""\s*[,)]").Select(m => Regex.Unescape(m.Groups[1].Value)));

    public static IEnumerable<string> XamlTexts() =>
        Sources("*.xaml").SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\{l:T\s+(?:'((?:[^'\\]|\\.)*)'|([^}']+))\}")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value.Replace(@"\{", "{").Replace(@"\}", "}").Replace(@"\'", "'") : m.Groups[2].Value.Trim()))
            .Select(t => System.Net.WebUtility.HtmlDecode(t)!);

    [Fact]
    public void Every_LT_text_in_code_has_Arabic()
    {
        var texts = CodeTexts().Distinct().ToList();
        Assert.True(texts.Count > 100, "scanner found too few texts");
        var missing = texts.Where(t => !Arabic.Text.ContainsKey(t)).ToList();
        Assert.True(missing.Count == 0, "No Arabic for: " + string.Join(" | ", missing));
    }

    [Fact]
    public void Every_lT_text_in_XAML_has_Arabic()
    {
        var texts = XamlTexts().Distinct().ToList();
        Assert.True(texts.Count > 100, "scanner found too few texts");
        var missing = texts.Where(t => !Arabic.Text.ContainsKey(t)).ToList();
        Assert.True(missing.Count == 0, "No Arabic for: " + string.Join(" | ", missing));
    }

    [Fact]
    public void Arabic_has_no_duplicate_keys()
    {
        var keys = Regex.Matches(File.ReadAllText(Path.Combine(DesktopDir, "Services", "Arabic.cs")), @"^\s*\[""((?:[^""\\]|\\.)*)""\]\s*=", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();
        var dup = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dup.Count == 0, "Duplicate keys: " + string.Join(" | ", dup));
    }

    [Fact]
    public void Arabic_placeholders_match_the_English()
    {
        var bad = Arabic.Text.Where(kv => Placeholders(kv.Key) != Placeholders(kv.Value)).Select(kv => kv.Key).ToList();
        Assert.True(bad.Count == 0, "Placeholders differ: " + string.Join(" | ", bad));
    }

    private static string Placeholders(string s) => string.Join(",", Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Order());

    /// <summary>Messages from CONTRACTS.md section 3 and the ones agents A and B added on the server.</summary>
    [Theory]
    [InlineData("This record was changed on another device. It has been reloaded; check it and save again.")]
    [InlineData("You must change your password before you continue.")]
    [InlineData("Too many wrong passwords. Wait 30 seconds and try again.")]
    [InlineData("This number or code is already used. Reload and try again.")]
    [InlineData("Customer number 12 is already used.")]
    public void New_server_messages_are_translated(string english)
    {
        using var _ = new Language("ar");
        var arabic = L.Message(english);
        Assert.NotEqual(english, arabic);
        Assert.DoesNotMatch("[A-Za-z]", arabic);
    }
}
