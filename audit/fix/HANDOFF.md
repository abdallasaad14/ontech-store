# Handoff between agents

Each agent writes ONLY in its own section: requests for files it does not own, decisions the others must know, and its final summary.
Format for a request: `- [ ] <file> — <what to change> — <why / issue id>`.

## Agent A (API config, auth) — branch `fix/a-api-config`

### Requests

- [ ] `src/OnTech.Store.Api/Controllers/DashboardController.cs` (B): add `/// <param name="ct">Cancels the request.</param>` to `Summary` (line 18). This is the only build warning left (CS1573). — L-10
- [ ] `src/OnTech.Store.Contracts/ApiContract.cs` (B): add `public const string TooManyAttempts = "TooManyAttempts";` to `ErrorCodes` (429, too many wrong passwords). The server uses the same literal in `Auth/LoginThrottle.Code`; once the constant exists, the lead can point `LoginThrottle.Code` at it. — L-2
- [ ] `tests/OnTech.Store.Api.Tests/*.csproj` and the other test projects (lead): align `Microsoft.AspNetCore.Mvc.Testing` 8.0.11 → **8.0.33** to match the API. — M-6
- [ ] `tools/OnTech.Store.DemoData` (lead): nothing to change. It got SQLitePCLRaw 2.1.6 through its `ProjectReference` to the API, so the API's new direct reference lifts it to 2.1.14 too; `dotnet list tools/OnTech.Store.DemoData package --vulnerable --include-transitive` reports no vulnerable packages. — M-7
- [ ] `tests/OnTech.Store.Api.Tests/StoreApiTests.cs` (lead, optional): `Standard_user_permissions_are_enforced_and_disabling_is_immediate` creates a user with the default `MustChangePassword = true` and uses it right away. It still passes because `StoreApiFactory.LoginAsync` now clears the flag of the user it signs in (documented on the method). If you prefer the test to say it explicitly, set `MustChangePassword = false` there. Same for `Dashboard_and_swagger_respond`: it passes because the factory sets `Swagger:Enabled=true`; to test the Testing default instead, expect 404 there. — M-2, Swagger
- [ ] `audit/OnTech.Store.Tests` and the 5081 DB (lead, Phase 3): the audit tests that sign in as admin on a test DB whose admin still has `MustChangePassword = 1` now get 403 `MustChangePassword`. Clear the flag on the test DBs. `Swagger_is_reachable_without_signing_in` must expect 404 outside Development. Audit servers started from these builds need `Jwt__SigningKey` (≥ 32 bytes, not a leaked key) unless they run in Development. — M-2, H-4, Swagger
- [ ] `docs/STATUS.md` (lead): the "Sign in" row says seed admin `admin` / `ChangeMe!123` from `appsettings.json`. Now: first admin password from `Seed__AdminPassword` (env or user-secrets), required only when the DB has no admin; server-side MustChangePassword; lockout after 5 wrong passwords; key from `Jwt__SigningKey`; Swagger only in Development. "How it runs on the owner's PC": see `audit/fix/DEPLOY_NOTES.md`. — H-4, L-5, M-2, L-2

### Notes for B / C

- **For C, Arabic needed in `Services/Arabic.cs`** (new server titles the user can see):
  - Exact: `You must change your password before you continue.` (403, code `MustChangePassword`). The client should react on the code: open the change-password window.
  - Pattern: `Too many wrong passwords. Wait {0} seconds and try again.` (429, code `TooManyAttempts`, `{0}` is a whole number of seconds, e.g. `Wait 30 seconds`). The response also has a `Retry-After: <seconds>` header. 429 is new to the client: `DefaultMessage` has no text for it, but the title is always set, so it shows.
  - Startup errors (missing `Jwt__SigningKey`, missing `Seed__AdminPassword`) are not user-facing; no Arabic needed.
- **For B:** a signed-in user's `MustChangePassword` flag is now enforced by `Auth/MustChangePassword.cs` (middleware after `UseAuthentication`). Endpoints that must stay open while flagged get `[AllowWhileMustChangePassword]`; only `GET api/auth/me` and `POST api/auth/change-password` have it. Nothing for B to do in controllers.
- **For B:** requests that match no endpoint now return 404 before authorization (they used to get the fallback policy's 401). Any route you add is an endpoint, so it is unaffected.
- **For B / C tests:** `StoreApiFactory` now has `EnvironmentName`, `Settings` (a null value removes a default setting) and `TestServices` (replace services, e.g. a fake `TimeProvider`). The test admin no longer has `MustChangePassword`. `LoginAsync` clears the flag of any user it signs in; to test the flag, sign in by hand with `CreateClient()` + `POST api/auth/login`. Login lockout is per factory (per process host): 5 wrong passwords for one username in one test class lock it for 30 s, so don't hammer `admin` with wrong passwords in a shared fixture.
- **Package versions chosen (M-6):** Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.EntityFrameworkCore.Design/.Sqlite/.SqlServer, Microsoft.Extensions.Identity.Core = **8.0.33**; Microsoft.Extensions.Hosting.WindowsServices = **8.0.1** (already the latest 8.0.x); SQLitePCLRaw.bundle_e_sqlite3 = **2.1.14** (GHSA-2m69-gcr7-jv3q covers ≤ 2.1.11; native SQLite now ≥ 3.50.2 instead of 3.41.2).
- **Owner redeploy:** `audit/fix/DEPLOY_NOTES.md`. After merge, the live server must not keep running as `dotnet run` in Development: without a key it makes a random one at each start (everyone signed out on every restart). The key change signs everyone out once.

### Summary

All on `fix/a-api-config`, based on `0865a55`. Build: `dotnet build OnTech.Store.sln -c Debug` 0 errors, 1 warning (the pre-existing CS1573, request above). `tests/OnTech.Store.Api.Tests`: **121/121 on SQLite, 121/121 on SQL Server** (87 old + 34 new `Fix_A_*`).

| Issue | Commit | Regression test | Result |
|---|---|---|---|
| H-1 InvariantGlobalization | `bb2fb06` | `Fix_A_GlobalizationTests` (reads the API's own runtimeconfig.json) | failed before, passes. Smoke: published API on 5084, Production, `.\SQLEXPRESS` DB `OnTechStore_A_Smoke`, `GET /api/health` → **200** (also re-run on the final build: 200, `/swagger` 404); process stopped, DB dropped |
| H-4 signing key out of git | `bbc9705`, `4422c30` (install-service.ps1, edited, not run) | `Fix_A_SigningKeyTests`: leaked keys → 401 (Development with no key, and Testing); Production without key / short key / leaked key does not start; no `SigningKey` in any appsettings | 6 of 9 failed before, all pass. Manual: Production without key exits with "Jwt:SigningKey is not set … Set the environment variable Jwt__SigningKey …". `audit/fix/DEPLOY_NOTES.md` written |
| M-2 MustChangePassword on the server | `9967f94` | `Fix_A_MustChangePasswordTests` (403 + code on GET/POST, me/change-password allowed, flag clears, admin reset flags again, wrong current password keeps it) | 4 failed before, pass |
| M-7 SQLitePCLRaw advisory | `3696367` | `Fix_A_SqliteVersionTests` (package ≥ 2.1.12, native SQLite ≥ 3.50.2) | failed before (3.41.2), passes. `dotnet list src/OnTech.Store.Api package --vulnerable --include-transitive`: no vulnerable packages |
| M-6 (partial) 8.0.x patch | `73f2b67` | `Fix_A_PackageVersionTests` (EF Core, Sqlite, SqlServer, JwtBearer, Identity.Core ≥ 8.0.33, major 8) | 5 failed before, pass |
| L-2 lockout | `1a04b20` | `Fix_A_LoginLockoutTests` (30 s after 5, same answer for right/wrong while locked, countdown, doubling 30→60→120→240→300→300, reset on success, per username incl. case/spaces, unknown usernames, 1000 parallel failures) | 4 HTTP tests failed before, pass |
| L-5 no default admin password | `2ee90ca`, `7142a3a` (factory drops a SQL DB left by a failed startup) | `Fix_A_SeedAdminTests` (no password → startup fails naming `Seed__AdminPassword`; seeded admin has MustChangePassword; no AdminPassword in appsettings or SeedOptions) | 2 failed before, pass |
| Swagger only in Development | `265d348` | `Fix_A_SwaggerTests` (Testing/Production → 404, Development → 200, Production + `Swagger:Enabled=true` → 200) | 2 failed before, pass |
| L-10 CS1573 | — | — | request to B above (not my file) |

Not done / notes: two throw-away DBs `OnTechStoreTest_d7ca0a03…` and `OnTechStoreTest_295fbeb7…` (created 13:53 and 13:57 on `.\SQLEXPRESS`) were left by my SQL Server runs before `7142a3a`; I did not drop them in case they belong to another agent's running tests. They are safe to drop.

## Agent B (API controllers, data, migrations) — branch `fix/b-api-data`

### Requests

### Final DTO / error changes for C

### Summary

## Agent C (WPF client) — branch `fix/c-wpf`

### Requests

### Summary
