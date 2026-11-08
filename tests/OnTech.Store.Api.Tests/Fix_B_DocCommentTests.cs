using System.Xml.Linq;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>L-10: the dashboard action documents every parameter, so the build has no CS1573 warning.</summary>
public class Fix_B_DocCommentTests
{
    [Fact]
    public void Dashboard_summary_documents_all_its_parameters()
    {
        var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "OnTech.Store.Api.xml"));
        var member = xml.Descendants("member").Single(m =>
            (string?)m.Attribute("name") == "M:OnTech.Store.Api.Controllers.DashboardController.Summary(System.Int32,System.Threading.CancellationToken)");
        var documented = member.Elements("param").Select(p => (string?)p.Attribute("name")).ToList();
        Assert.Contains("utcOffsetMinutes", documented);
        Assert.Contains("ct", documented);
    }
}
