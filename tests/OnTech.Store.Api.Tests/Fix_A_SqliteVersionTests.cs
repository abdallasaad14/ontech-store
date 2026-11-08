using Microsoft.Data.Sqlite;

namespace OnTech.Store.Api.Tests;

/// <summary>
/// M-7: EF Core 8 brings SQLitePCLRaw 2.1.6 (native SQLite 3.41.2). GHSA-2m69-gcr7-jv3q (High) covers SQLitePCLRaw.lib.e_sqlite3
/// up to and including 2.1.11 (NuGet vulnerability feed). The API references SQLitePCLRaw.bundle_e_sqlite3 2.1.14 directly.
/// </summary>
public class Fix_A_SqliteVersionTests
{
    private static readonly Version FirstPatchedPackage = new(2, 1, 12);
    /// <summary>SQLite 3.41.2 (2.1.6) is the vulnerable engine; the patched bundles ship 3.50.2 or later.</summary>
    private static readonly Version PatchedEngine = new(3, 50, 2);

    [Fact]
    public void SQLitePCLRaw_is_outside_the_advisory_range()
    {
        var version = typeof(SQLitePCL.raw).Assembly.GetName().Version!;
        Assert.True(new Version(version.Major, version.Minor, version.Build) >= FirstPatchedPackage,
            $"SQLitePCLRaw is {version}; GHSA-2m69-gcr7-jv3q covers versions up to 2.1.11.");
    }

    [Fact]
    public void Native_sqlite_is_a_patched_version()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select sqlite_version()";
        var version = Version.Parse((string)cmd.ExecuteScalar()!);
        Assert.True(version >= PatchedEngine, $"Native SQLite is {version}; {PatchedEngine} or later is needed (GHSA-2m69-gcr7-jv3q).");
    }
}
