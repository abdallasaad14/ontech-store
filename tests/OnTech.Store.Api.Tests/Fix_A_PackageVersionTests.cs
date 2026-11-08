using System.Reflection;

namespace OnTech.Store.Api.Tests;

/// <summary>M-6 (partial): the API's Microsoft packages are on the latest 8.0.x patch (8.0.33 when this was written), not 9 or 10.</summary>
public class Fix_A_PackageVersionTests
{
    private static readonly Version Latest8 = new(8, 0, 33);

    public static TheoryData<string> Assemblies => new()
    {
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Microsoft.AspNetCore.Authentication.JwtBearer",
        "Microsoft.Extensions.Identity.Core",
    };

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Package_is_on_the_latest_8_0_patch(string name)
    {
        var asm = Assembly.Load(name);
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var version = Version.Parse(info.Split('+', '-')[0]);
        Assert.Equal(8, version.Major);
        Assert.True(version >= Latest8, $"{name} is {version}; expected {Latest8} or later 8.0.x.");
    }
}
