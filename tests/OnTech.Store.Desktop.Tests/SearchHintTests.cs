using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace OnTech.Store.Desktop.Tests;

/// <summary>The faint hint inside search boxes (Theme.xaml "SearchBox"): shown while empty, gone once the user types.</summary>
public class SearchHintTests
{
    [Fact]
    public void Hint_shows_while_the_search_box_is_empty()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var theme = (ResourceDictionary)Application.LoadComponent(new Uri("/OnTechStore;component/Themes/Theme.xaml", UriKind.Relative));
                var box = new TextBox { Style = (Style)theme["SearchBox"], Tag = "Voucher, customer or item" };
                var window = new Window { Content = box, Width = 300, Height = 80, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Resources = theme };
                window.Show();
                var hint = (TextBlock)box.Template.FindName("Hint", box);
                Assert.Equal("Voucher, customer or item", hint.Text);
                Assert.Equal(Visibility.Visible, hint.Visibility);
                box.Text = "acme";
                Assert.Equal(Visibility.Collapsed, hint.Visibility);
                window.Close();
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
