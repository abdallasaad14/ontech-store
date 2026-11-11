# OnTech Store: rules for every change

## The desktop app must work in Arabic (RTL) and English
Every change to `src/OnTech.Store.Desktop` must look and work right in both languages, including the right-to-left Arabic layout.

- **No hard-coded UI text.** In XAML write `{l:T 'English text'}`. In C# write `L.T("English text")`, or `L.T("... {0} ...", value)` when the text has values in it. Add the Arabic for every new text to `Services/Arabic.cs`, using the same `{0}` placeholders.
- **Messages from the server.** When you add a new server error message, add its Arabic to `Arabic.cs`: as an exact-text entry, or in `Patterns` when the message contains names or numbers.
- **Layout.** Windows already set `FlowDirection` through `L.Apply(this)`, so call it in every new window's constructor. Let WPF mirror the layout. Use margins, `DockPanel.Dock` and `HorizontalAlignment`; never use absolute positions that assume left-to-right. Images and logos that must not flip get `FlowDirection="LeftToRight"`.
- **Choices in lists.** Never compare against English display text (combo boxes, filters). Use an index or an enum, and show it through `L.T` or the `Translate` converter.
- **Message boxes.** Go through `Dialogs`, or pass `L.MessageBoxOptions`.
- **XAML escaping.** A quoted `{l:T}` value that starts with `{` needs escaped braces, for example `{l:T '\{0\} available'}`.
- **Before pushing.** Build the app. Check that every new `L.T` or `{l:T}` text has an Arabic entry. Say in the PR how the change looks in Arabic.

## Read docs/STATUS.md first
`docs/STATUS.md` lists every feature, its state, and the files, endpoints, pages and tables that hold it. Read it before opening the code, and open only the files it points to. Any PR that adds or changes a feature must update `docs/STATUS.md` in the same PR.

## Server and data rules (from the November 2026 audit)
Every new feature follows what the audit fixes set up. `audit/fix/FIX_REPORT.md` explains each one.

- **Permissions.** Every endpoint has a policy. Reads use `[HasAnyPermission]` with a `ReadAccess` set; writes need the exact permission. The desktop menu and buttons show only what the user's permissions allow.
- **Two PCs at once.** An editable record has a `Version` concurrency token. The DTO carries it, the save request sends it back, and a stale save gets 409 `ConcurrencyConflict`; the app reloads the record into the open form. Something that is not an edit (a login timestamp, a counter) is written with `ExecuteUpdate`, without the version check.
- **Errors.** Throw `ApiException` (ProblemDetails) with a `code` from `Contracts/ErrorCodes` when the client must react to it. Bad input is 400 or 409, never 500. A number or code that must be unique goes through `NumberingGate` and has a unique index; a clash is 409 `Duplicate`.
- **Input.** Every string in a request DTO has `[MaxLength]`, and its TextBox has the same `MaxLength`. Lists are paged within `Contracts/Paging` limits. Numbers typed by the user go through `Client/Numbers` (Arabic-Indic digits work). Document numbers are trimmed and upper-cased.
- **Stock.** Quantities change only through a voucher in `StockService`, inside its transaction, with a `StockMovement` row. Order movement lists by `Id`, not by time.
- **Schema changes need both databases.** Add an EF migration for SQL Server and a step in `DatabaseSetup.UpgradeSqliteAsync` for the live SQLite file (it backs the file up first). Test the upgrade on a copy of the live DB.
- **Secrets.** No keys or passwords in `appsettings*.json` or in git; read them from environment variables or user-secrets.
- **Desktop.** Every new control gets an `AutomationId` named `<Screen>_<Control>`, and its label is linked with `AutomationProperties.LabeledBy`.
- **Tests.** Every fix or feature comes with a test that fails without it. Run `tests/OnTech.Store.Api.Tests` on SQLite and on SQL Server (set `ONTECH_TEST_SQLSERVER` to `Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True`), plus `tests/OnTech.Store.Desktop.Tests`. The build has 0 warnings.

## Working on the owner's PC
- The live server (port 5080) runs from `src\OnTech.Store.Api\bin\Release` in the main folder, on `ontech-store-dev.db`. Never stop it, never build Release in the main folder, and never switch branches there: work in a git worktree.
- Never write to the live database without the owner's OK and a backup first. Test servers use other ports (5081 SQL Server, 5082 SQLite copy, see `audit/fix/tools/servers.ps1`); stop the ones you start.
- Never merge to `main` without the owner's OK.
