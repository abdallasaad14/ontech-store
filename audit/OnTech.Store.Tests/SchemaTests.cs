using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnTech.Store.Api.Data;
using Xunit.Abstractions;

namespace OnTech.Store.Tests;

/// <summary>Phase 3: the code's EF model against the real schemas.</summary>
public class SchemaTests(ITestOutputHelper out_)
{
    private static StoreDbContext SqlServerContext() =>
        new(new DbContextOptionsBuilder<StoreDbContext>().UseSqlServer(Server.SqlServer.SqlServerDb).Options);

    /// <summary>Builds the DbContext exactly like the API does (DatabaseSetup.AddStoreDatabase) from appsettings.json.</summary>
    [Fact]
    public void Connects_with_the_apps_own_connection_logic()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(Paths.Audit, "..", "src", "OnTech.Store.Api", "appsettings.json"))
            .Build();
        var services = new ServiceCollection();
        services.AddStoreDatabase(config);
        using var sp = services.BuildServiceProvider();
        using var db = sp.GetRequiredService<StoreDbContext>();
        var cs = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        out_.WriteLine($"DataSource={cs.DataSource} Db={cs.InitialCatalog} IntegratedSecurity={cs.IntegratedSecurity} Encrypt={cs.Encrypt} " +
                       $"TrustServerCertificate={cs.TrustServerCertificate} ConnectTimeout={cs.ConnectTimeout} CommandTimeout={db.Database.GetCommandTimeout()}");
        Evidence.Log("schema", $"connection: {cs.DataSource}/{cs.InitialCatalog} integrated={cs.IntegratedSecurity} encrypt={cs.Encrypt} trustCert={cs.TrustServerCertificate} connectTimeout={cs.ConnectTimeout}s commandTimeout={db.Database.GetCommandTimeout()}s");
        Assert.True(db.Database.CanConnect());
    }

    [Fact]
    public void SqlServer_has_no_pending_migrations()
    {
        using var db = SqlServerContext();
        var pending = db.Database.GetPendingMigrations().ToList();
        Evidence.Log("schema", $"applied={string.Join(",", db.Database.GetAppliedMigrations())} pending={string.Join(",", pending)}");
        Assert.Empty(pending);
    }

    [Fact]
    public void Model_snapshot_matches_the_current_model()
    {
        using var db = SqlServerContext();
        var differ = db.GetService<IMigrationsModelDiffer>();
        IModel snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        if (snapshot is IMutableModel mutable) snapshot = mutable.FinalizeModel();
        var finalized = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot);
        var diffs = differ.GetDifferences(finalized.GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        foreach (var d in diffs) out_.WriteLine(d.ToString());
        Evidence.Log("schema", $"model-vs-snapshot differences: {diffs.Count}");
        Assert.Empty(diffs);
    }

    /// <summary>Every column EF maps must exist in SQL Server with the same nullability and max length.</summary>
    [Fact]
    public void SqlServer_columns_match_the_model()
    {
        using var db = SqlServerContext();
        var actual = new Dictionary<string, (bool Nullable, int MaxLen, string Type)>(StringComparer.OrdinalIgnoreCase);
        using (var c = new SqlConnection(Server.SqlServer.SqlServerDb))
        {
            c.Open();
            using var cmd = new SqlCommand("SELECT TABLE_NAME, COLUMN_NAME, IS_NULLABLE, ISNULL(CHARACTER_MAXIMUM_LENGTH,0), DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS", c);
            using var r = cmd.ExecuteReader();
            while (r.Read()) actual[$"{r.GetString(0)}.{r.GetString(1)}"] = (r.GetString(2) == "YES", r.GetInt32(3), r.GetString(4));
        }
        var problems = new List<string>();
        foreach (var e in db.Model.GetEntityTypes())
        {
            var table = e.GetTableName()!;
            var store = StoreObjectIdentifier.Table(table, null);
            foreach (var p in e.GetProperties())
            {
                var col = p.GetColumnName(store)!;
                if (!actual.TryGetValue($"{table}.{col}", out var a)) { problems.Add($"MISSING {table}.{col}"); continue; }
                if (a.Nullable != p.IsColumnNullable(store)) problems.Add($"NULLABILITY {table}.{col}: db={a.Nullable} model={p.IsColumnNullable(store)}");
                if (p.GetMaxLength() is { } max && a.MaxLen != max) problems.Add($"LENGTH {table}.{col}: db={a.MaxLen} model={max}");
            }
        }
        foreach (var p in problems) out_.WriteLine(p);
        Evidence.Log("schema", $"sqlserver column problems: {problems.Count} {string.Join("; ", problems)}");
        Assert.Empty(problems);
    }

    /// <summary>The live database is a SQLite file made by EnsureCreated and patched by hand (UpgradeSqliteAsync). Same check against it.</summary>
    [Fact]
    public void Live_sqlite_copy_has_every_model_column()
    {
        using var db = SqlServerContext(); // only for the model
        var file = Path.Combine(Paths.Audit, "backups", "ontech-store-dev_before_audit.db");
        using var c = new SqliteConnection($"Data Source={file};Mode=ReadOnly"); c.Open();
        var problems = new List<string>();
        foreach (var e in db.Model.GetEntityTypes())
        {
            var table = e.GetTableName()!;
            var store = StoreObjectIdentifier.Table(table, null);
            var cols = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            using var cmd = c.CreateCommand(); cmd.CommandText = $"SELECT name, \"notnull\" FROM pragma_table_info('{table}')";
            using (var r = cmd.ExecuteReader()) while (r.Read()) cols[r.GetString(0)] = r.GetInt64(1) == 0;
            if (cols.Count == 0) { problems.Add($"MISSING TABLE {table}"); continue; }
            foreach (var p in e.GetProperties())
            {
                var col = p.GetColumnName(store)!;
                if (!cols.TryGetValue(col, out var nullable)) problems.Add($"MISSING {table}.{col}");
                else if (nullable != p.IsColumnNullable(store)) problems.Add($"NULLABILITY {table}.{col}: db={nullable} model={p.IsColumnNullable(store)}");
            }
        }
        foreach (var p in problems) out_.WriteLine(p);
        Evidence.Log("schema", $"sqlite column problems: {problems.Count} {string.Join("; ", problems)}");
        Assert.Empty(problems);
    }

    /// <summary>Columns that hold a user id but have no foreign key (orphans possible).</summary>
    [Fact]
    public void User_id_columns_have_foreign_keys()
    {
        var missing = new List<string>();
        foreach (var (t, col) in new[] { ("Customers", "CreatedByUserId"), ("Suppliers", "CreatedByUserId") })
        {
            var n = Convert.ToInt32(Server.SqlServer.Scalar(
                "SELECT COUNT(*) FROM sys.foreign_key_columns fc WHERE OBJECT_NAME(fc.parent_object_id)=@t AND COL_NAME(fc.parent_object_id, fc.parent_column_id)=@c",
                ("@t", t), ("@c", col)));
            if (n == 0) missing.Add($"{t}.{col}");
        }
        Evidence.Log("schema", $"user-id columns without FK: {string.Join(", ", missing)}");
        Assert.Empty(missing);
    }
}
