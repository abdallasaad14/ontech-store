-- ============================================================================
-- OnTech Store audit: put the databases back to how they were before the audit
-- (2026-11-08). Two databases are involved, see audit/PROGRESS.md.
-- ============================================================================

-- ---------------------------------------------------------------------------
-- 1) SQL Server  .\SQLEXPRESS  [OnTechStore]
--    Did not exist before the audit: the app's own EF migrations created it on
--    2026-11-08 (API started in Production mode on port 5081). The backup below
--    is that freshly created + seeded state (318 items, 505 customers, 1 admin).
--    Run with:  sqlcmd -S .\SQLEXPRESS -E -C -i audit\restore.sql
--    Stop any API process using OnTechStore first (port 5081 audit copy).
-- ---------------------------------------------------------------------------
USE [master];
GO
IF DB_ID(N'OnTechStore') IS NOT NULL
    ALTER DATABASE [OnTechStore] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
GO
RESTORE DATABASE [OnTechStore]
    FROM DISK = N'C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\OnTechStore_before_audit.bak'
    WITH REPLACE, RECOVERY, CHECKSUM;
GO
ALTER DATABASE [OnTechStore] SET MULTI_USER;
GO
-- To remove it completely instead (the state before the audit):
--   ALTER DATABASE [OnTechStore] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [OnTechStore];

-- ---------------------------------------------------------------------------
-- 2) SQLite  src\OnTech.Store.Api\ontech-store-dev.db   (the database the live
--    app on port 5080 actually uses today). SQL can't restore a file, so run
--    these PowerShell lines instead, with the API on port 5080 STOPPED:
--
--   $t='C:\Users\PC\source\ontech-store\audit\tools\AuditDb\bin\Release\net8.0\AuditDb.exe'
--   & $t restore 'C:\Users\PC\source\ontech-store\audit\backups\ontech-store-dev_before_audit.db' `
--                'C:\Users\PC\source\ontech-store\src\OnTech.Store.Api\ontech-store-dev.db'
--
--   Second copy of the same backup: D:\Backups\ontech-store-dev_before_audit_20261108.db
--   SHA-256 41BE4F42B86CA0461CA429D2C7C9C89D29D910B7F7710399872F1BB49E4B0640
-- ---------------------------------------------------------------------------
