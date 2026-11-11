// Audit helper. Usage:
//   AuditDb backup  <sqliteFile> <destFile>      online, consistent copy (SQLite backup API) while the server runs
//   AuditDb restore <backupFile> <sqliteFile>    copies a backup over the live file (stop the server first)
//   AuditDb sql     <sqliteFile> "<sql>"         runs SQL, prints rows tab-separated
//   AuditDb sqlfile <sqliteFile> <file.sql>      runs each ;-separated statement
//   AuditDb mssql   "<connStr>" "<sql>"          same for SQL Server
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;

if (args.Length < 2) { Console.Error.WriteLine("see Program.cs for usage"); return 2; }
try
{
    switch (args[0])
    {
        case "backup":
        {
            using var src = new SqliteConnection($"Data Source={args[1]};Mode=ReadOnly");
            using var dst = new SqliteConnection($"Data Source={args[2]};Pooling=False");
            src.Open(); dst.Open();
            src.BackupDatabase(dst);
            dst.Close(); SqliteConnection.ClearAllPools();
            using var check = new SqliteConnection($"Data Source={args[2]};Mode=ReadOnly;Pooling=False");
            check.Open();
            Print(check, "PRAGMA integrity_check");
            Print(check, "SELECT (SELECT COUNT(*) FROM Items) items,(SELECT COUNT(*) FROM Customers) customers,(SELECT COUNT(*) FROM Orders) orders,(SELECT COUNT(*) FROM StockMovements) movements");
            return 0;
        }
        case "restore":
            File.Copy(args[1], args[2], overwrite: true);
            foreach (var ext in new[] { "-wal", "-shm" }) if (File.Exists(args[2] + ext)) File.Delete(args[2] + ext);
            Console.WriteLine($"Restored {args[1]} -> {args[2]}");
            return 0;
        case "sql":
        {
            using var c = new SqliteConnection($"Data Source={args[1]}"); c.Open();
            Print(c, args[2]); return 0;
        }
        case "sqlfile":
        {
            using var c = new SqliteConnection($"Data Source={args[1]}"); c.Open();
            foreach (var stmt in File.ReadAllText(args[2]).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            { Console.WriteLine($"> {stmt}"); try { Print(c, stmt); } catch (Exception e) { Console.WriteLine($"ERROR: {e.Message}"); } }
            return 0;
        }
        case "mssql":
        {
            using var c = new SqlConnection(args[1]); c.Open();
            Print(c, args[2]); return 0;
        }
    }
    Console.Error.WriteLine("unknown command"); return 2;
}
catch (Exception e) { Console.Error.WriteLine($"ERROR {e.GetType().Name}: {e.Message}"); return 1; }

static void Print(DbConnection c, string sql)
{
    using var cmd = c.CreateCommand(); cmd.CommandText = sql;
    using var r = cmd.ExecuteReader();
    do
    {
        if (r.FieldCount == 0) continue;
        Console.WriteLine(string.Join('\t', Enumerable.Range(0, r.FieldCount).Select(r.GetName)));
        while (r.Read()) Console.WriteLine(string.Join('\t', Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "NULL" : Convert.ToString(r.GetValue(i)))));
    } while (r.NextResult());
    if (r.RecordsAffected > 0) Console.WriteLine($"({r.RecordsAffected} rows affected)");
}
