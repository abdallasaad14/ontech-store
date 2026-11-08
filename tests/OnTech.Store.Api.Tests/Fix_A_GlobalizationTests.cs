using System.Text.Json;

namespace OnTech.Store.Api.Tests;

/// <summary>
/// H-1: with InvariantGlobalization the published API crashes at startup on SQL Server ("en-us is an invalid culture identifier").
/// The test host runs with the test project's own runtimeconfig, so check the API's runtimeconfig instead.
/// </summary>
public class Fix_A_GlobalizationTests
{
    [Fact]
    public void Api_runtimeconfig_does_not_turn_on_invariant_globalization()
    {
        var dir = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        var path = Path.Combine(dir, "OnTech.Store.Api.runtimeconfig.json");
        Assert.True(File.Exists(path), $"{path} was not found next to the API assembly.");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var invariant = doc.RootElement.GetProperty("runtimeOptions").TryGetProperty("configProperties", out var props)
            && props.TryGetProperty("System.Globalization.Invariant", out var v) && v.ValueKind == JsonValueKind.True;
        Assert.False(invariant, "System.Globalization.Invariant is true in the API's runtimeconfig; Microsoft.Data.SqlClient needs cultures.");
    }
}
