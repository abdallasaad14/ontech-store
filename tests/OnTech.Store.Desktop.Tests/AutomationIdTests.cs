using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;
using Xunit;
using Page = OnTech.Store.Desktop.ViewModels.Page;
using static OnTech.Store.Desktop.Tests.ValidationTests;

namespace OnTech.Store.Desktop.Tests;

/// <summary>M-9: every interactive control has a stable AutomationId (Screen_Control), inputs are linked to their labels, menu items have a name.</summary>
public class AutomationIdTests
{
    private static readonly string[] Interactive =
        ["Button", "ToggleButton", "TextBox", "PasswordBox", "ComboBox", "CheckBox", "DatePicker", "ListBox", "DataGrid", "RadioButton"];

    private static readonly XName AutomationId = "AutomationProperties.AutomationId";
    private static readonly XName LabeledBy = "AutomationProperties.LabeledBy";

    private static string Screen(string file) => Path.GetFileNameWithoutExtension(file) switch
    {
        "MainWindow" => "Shell",
        var n => Regex.Replace(n, "(View|Window)$", ""),
    };

    public static IEnumerable<object[]> Files() => XamlFiles().Select(f => new object[] { Path.GetFileName(f) });

    private static string FullPath(string file) => file == "MainWindow.xaml" ? Path.Combine(DesktopDir, file) : Path.Combine(ViewsDir, file);

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_interactive_control_has_a_unique_screen_prefixed_AutomationId(string file)
    {
        var doc = XDocument.Load(FullPath(file));
        var controls = doc.Descendants().Where(e => e.Name.Namespace == Wpf && Interactive.Contains(e.Name.LocalName)).ToList();
        var missing = controls.Where(e => string.IsNullOrWhiteSpace((string?)e.Attribute(AutomationId)))
            .Select(e => e.ToString().Split('\n')[0].Trim()).ToList();
        Assert.True(missing.Count == 0, $"{file}: no AutomationId on {string.Join(" | ", missing)}");

        var ids = controls.Select(e => (string)e.Attribute(AutomationId)!).ToList();
        var fixedIds = ids.Where(id => !id.StartsWith("{")).ToList(); // "{Binding ...}" ids are per row
        Assert.All(fixedIds, id => Assert.Matches($"^{Screen(file)}_[A-Z][A-Za-z0-9]+$", id));
        var duplicates = fixedIds.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, $"{file}: duplicate ids {string.Join(", ", duplicates)}");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Labels_point_at_existing_elements_and_every_field_label_is_used(string file)
    {
        var doc = XDocument.Load(FullPath(file));
        var names = doc.Descendants().Select(e => (string?)e.Attribute(X + "Name")).Where(n => n is not null).ToHashSet();
        foreach (var e in doc.Descendants().Where(e => e.Attribute(LabeledBy) is not null))
        {
            var target = Regex.Match((string)e.Attribute(LabeledBy)!, @"ElementName=(\w+)").Groups[1].Value;
            Assert.True(names.Contains(target), $"{file}: LabeledBy {target} does not exist");
        }
        var labelled = doc.Descendants().Select(e => (string?)e.Attribute(LabeledBy)).Where(v => v is not null)
            .Select(v => Regex.Match(v!, @"ElementName=(\w+)").Groups[1].Value).ToHashSet();
        // A field label that names an input must be linked to it (the ones above a check box or a section are not).
        var unlinked = doc.Descendants(Wpf + "TextBlock")
            .Where(t => (string?)t.Attribute("Style") == "{StaticResource FieldLabel}")
            .Where(t => t.ElementsAfterSelf().Concat(t.Parent!.ElementsAfterSelf()).Take(3).SelectMany(x => x.DescendantsAndSelf())
                .Any(x => x.Name == Wpf + "TextBox" || x.Name == Wpf + "PasswordBox" || x.Name == Wpf + "DatePicker"))
            .Where(t => (string?)t.Attribute(X + "Name") is not { } n || !labelled.Contains(n))
            .Select(t => (string?)t.Attribute("Text")).ToList();
        Assert.True(unlinked.Count == 0, $"{file}: labels not linked to their input: {string.Join(", ", unlinked)}");
    }

    [Fact]
    public void Menu_items_are_named_after_their_page()
    {
        var item = new NavItem(Page.Customers, "Customers", "x");
        Assert.Equal("Customers", item.ToString());
    }

}
