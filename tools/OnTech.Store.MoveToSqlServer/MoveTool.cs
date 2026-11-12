using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Data;

namespace OnTech.Store.MoveToSqlServer;

/// <summary>
/// Moves the store from its SQLite file to SQL Server, and backs SQL Server up. Used by the move package (tools/move-package).
///   copy   --sqlite &lt;file.db&gt; --sqlserver "&lt;connection string&gt;"   create the database with the EF migrations, copy every row (same Ids), check counts
///   grant  --sqlserver "&lt;connection string&gt;"                        let the Windows service account (NT AUTHORITY\SYSTEM) own the database
///   backup --sqlserver "&lt;connection string&gt;" --folder &lt;dir&gt; [--keep 30]   full backup to dir, keeps the newest N files
/// Exit code 0 on success, 1 on any error or count mismatch.
/// </summary>
public static class MoveTool
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var cmd = args.FirstOrDefault() ?? "";
            string Arg(string name) => Opt(args, name) ?? throw new ArgumentException($"--{name} is required");
            switch (cmd)
            {
                case "copy": await CopyAsync(Arg("sqlite"), Arg("sqlserver")); break;
                case "grant": await GrantSystemAsync(Arg("sqlserver")); break;
                case "backup": await BackupAsync(Arg("sqlserver"), Arg("folder"), int.Parse(Opt(args, "keep") ?? "30", CultureInfo.InvariantCulture)); break;
                default:
                    Console.WriteLine("Usage: copy --sqlite <file.db> --sqlserver \"<cs>\" | grant --sqlserver \"<cs>\" | backup --sqlserver \"<cs>\" --folder <dir> [--keep 30]");
                    return 1;
            }
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FAILED: " + e.Message);
            return 1;
        }
    }

    private static string? Opt(string[] args, string name)
    {
        var i = Array.IndexOf(args, "--" + name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static async Task CopyAsync(string sqliteFile, string sqlServer)
    {
        if (!File.Exists(sqliteFile)) throw new FileNotFoundException("SQLite file not found", sqliteFile);

        // 1. The schema, exactly as the server would make it (migrations and their history).
        var options = new DbContextOptionsBuilder<StoreDbContext>().UseSqlServer(sqlServer).Options;
        await using (var db = new StoreDbContext(options))
            await db.Database.MigrateAsync();

        // Tables in an order where every row's parents come first.
        var tables = InsertOrder(new StoreDbContext(options));

        await using var source = new SqliteConnection($"Data Source={sqliteFile};Mode=ReadOnly");
        await source.OpenAsync();
        await using var target = new SqlConnection(sqlServer);
        await target.OpenAsync();

        foreach (var t in tables)
            if (await CountAsync(target, t) > 0)
                throw new InvalidOperationException($"SQL Server table {t} is not empty. Copy only into a new database.");

        // 2. Every row, same Ids, in one transaction.
        await using (var tx = (SqlTransaction)await target.BeginTransactionAsync())
        {
            foreach (var t in tables)
            {
                var copied = await CopyTableAsync(source, target, tx, t);
                Console.WriteLine($"{t,-22} {copied,8} rows");
            }
            await tx.CommitAsync();
        }

        // 3. Check: same row count in every table, same total stock.
        var bad = new List<string>();
        foreach (var t in tables)
        {
            long from = await CountAsync(source, t), to = await CountAsync(target, t);
            if (from != to) bad.Add($"{t}: SQLite {from}, SQL Server {to}");
        }
        var qtyFrom = System.Convert.ToInt64(await ScalarAsync(source, "SELECT COALESCE(SUM(Quantity),0) FROM Items"), CultureInfo.InvariantCulture);
        var qtyTo = System.Convert.ToInt64(await ScalarAsync(target, "SELECT COALESCE(SUM(CAST(Quantity AS bigint)),0) FROM Items"), CultureInfo.InvariantCulture);
        if (qtyFrom != qtyTo) bad.Add($"Total stock: SQLite {qtyFrom}, SQL Server {qtyTo}");
        if (bad.Count > 0) throw new InvalidOperationException("Counts differ after the copy:\n  " + string.Join("\n  ", bad));
        Console.WriteLine($"OK: every table matches. Items total stock {qtyTo}.");
    }

    /// <summary>Entity tables ordered parents first (by the model's foreign keys).</summary>
    private static List<string> InsertOrder(StoreDbContext db)
    {
        var types = db.Model.GetEntityTypes().Where(e => e.GetTableName() is not null && !e.IsOwned()).ToList();
        var order = new List<string>();
        var done = new HashSet<string>();
        void Visit(Microsoft.EntityFrameworkCore.Metadata.IEntityType e, HashSet<string> path)
        {
            var name = e.GetTableName()!;
            if (done.Contains(name) || !path.Add(name)) return;
            foreach (var fk in e.GetForeignKeys())
                if (fk.PrincipalEntityType != e) Visit(fk.PrincipalEntityType, path);
            done.Add(name);
            order.Add(name);
        }
        foreach (var e in types) Visit(e, new HashSet<string>());
        db.Dispose();
        return order;
    }

    private static async Task<int> CopyTableAsync(SqliteConnection source, SqlConnection target, SqlTransaction tx, string table)
    {
        // The target's columns and types decide how each SQLite value is read.
        var shape = new DataTable();
        await using (var cmd = new SqlCommand($"SELECT TOP 0 * FROM [{table}]", target, tx))
        await using (var r = await cmd.ExecuteReaderAsync())
            shape.Load(r);
        foreach (DataColumn c in shape.Columns) { c.ReadOnly = false; c.AllowDBNull = true; c.AutoIncrement = false; }

        var sourceColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = source.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) sourceColumns.Add(r.GetString(1));
        }
        if (sourceColumns.Count == 0) throw new InvalidOperationException($"Table {table} is missing in the SQLite file.");
        var columns = shape.Columns.Cast<DataColumn>().Where(c => sourceColumns.Contains(c.ColumnName)).ToList();
        var missing = shape.Columns.Cast<DataColumn>().Except(columns).Select(c => c.ColumnName).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"Table {table}: columns not in the SQLite file: {string.Join(", ", missing)}");

        await using (var cmd = source.CreateCommand())
        {
            cmd.CommandText = $"SELECT {string.Join(", ", columns.Select(c => $"\"{c.ColumnName}\""))} FROM \"{table}\"";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var row = shape.NewRow();
                for (var i = 0; i < columns.Count; i++)
                    row[columns[i]] = r.IsDBNull(i) ? DBNull.Value : FromSqlite(r.GetValue(i), columns[i].DataType);
                shape.Rows.Add(row);
            }
        }

        using var bulk = new SqlBulkCopy(target, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.CheckConstraints, tx)
        {
            DestinationTableName = $"[{table}]",
            BulkCopyTimeout = 0,
        };
        foreach (var c in columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await bulk.WriteToServerAsync(shape);
        return shape.Rows.Count;
    }

    /// <summary>SQLite keeps dates as text, booleans as 0/1 and every number as 64-bit; turn them into the target's types.</summary>
    internal static object FromSqlite(object value, Type type) => type switch
    {
        _ when type == typeof(DateTime) => value is string s ? DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : System.Convert.ToDateTime(value, CultureInfo.InvariantCulture),
        _ when type == typeof(DateTimeOffset) => value is string s ? DateTimeOffset.Parse(s, CultureInfo.InvariantCulture) : (DateTimeOffset)System.Convert.ToDateTime(value, CultureInfo.InvariantCulture),
        _ when type == typeof(bool) => value is string s ? s is "1" or "true" or "True" : System.Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
        _ when type == typeof(Guid) => value is byte[] b ? new Guid(b) : Guid.Parse(value.ToString()!),
        _ when type == typeof(byte[]) => value,
        _ => System.Convert.ChangeType(value, type, CultureInfo.InvariantCulture),
    };

    private static async Task<long> CountAsync(System.Data.Common.DbConnection c, string table) =>
        System.Convert.ToInt64(await ScalarAsync(c, c is SqlConnection ? $"SELECT COUNT_BIG(*) FROM [{table}]" : $"SELECT COUNT(*) FROM \"{table}\""), CultureInfo.InvariantCulture);

    private static async Task<object?> ScalarAsync(System.Data.Common.DbConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    /// <summary>The service runs as LocalSystem with Windows authentication: make that account the database's owner.</summary>
    private static async Task GrantSystemAsync(string sqlServer)
    {
        var builder = new SqlConnectionStringBuilder(sqlServer);
        var database = builder.InitialCatalog;
        if (string.IsNullOrEmpty(database)) throw new ArgumentException("The connection string has no Database.");
        await using var c = new SqlConnection(sqlServer);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"""
            IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS;
            IF USER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM];
            ALTER ROLE db_owner ADD MEMBER [NT AUTHORITY\SYSTEM];
            """;
        await cmd.ExecuteNonQueryAsync();
        Console.WriteLine($"OK: NT AUTHORITY\\SYSTEM owns database {database}.");
    }

    private static async Task BackupAsync(string sqlServer, string folder, int keep)
    {
        var database = new SqlConnectionStringBuilder(sqlServer).InitialCatalog;
        Directory.CreateDirectory(folder);
        var file = Path.Combine(Path.GetFullPath(folder), $"{database}_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
        await using var c = new SqlConnection(sqlServer);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandTimeout = 0;
        cmd.CommandText = $"BACKUP DATABASE [{database}] TO DISK = @file WITH INIT, CHECKSUM";
        cmd.Parameters.AddWithValue("@file", file);
        await cmd.ExecuteNonQueryAsync();
        Console.WriteLine("OK: " + file);
        foreach (var old in new DirectoryInfo(folder).GetFiles($"{database}_*.bak").OrderByDescending(f => f.Name).Skip(keep))
            old.Delete();
    }
}
