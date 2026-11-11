using System.Net;

namespace OnTech.Store.Api.Tests;

/// <summary>Swagger only in Development; Swagger:Enabled can still force it on.</summary>
public class Fix_A_SwaggerTests
{
    [Theory]
    [InlineData("Testing", null, HttpStatusCode.NotFound)]
    [InlineData("Production", null, HttpStatusCode.NotFound)]
    [InlineData("Development", null, HttpStatusCode.OK)]
    [InlineData("Production", "true", HttpStatusCode.OK)]
    public async Task Swagger_is_only_on_in_development_unless_forced(string environment, string? enabled, HttpStatusCode expected)
    {
        using var f = new StoreApiFactory { EnvironmentName = environment, Settings = { ["Swagger:Enabled"] = enabled } };
        var res = await f.CreateClient().GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(expected, res.StatusCode);
    }
}
