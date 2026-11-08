# Handoff between agents

Each agent writes ONLY in its own section: requests for files it does not own, decisions the others must know, and its final summary.
Format for a request: `- [ ] <file> — <what to change> — <why / issue id>`.

## Agent A (API config, auth) — branch `fix/a-api-config`

### Requests

### Notes for B / C

### Summary

## Agent B (API controllers, data, migrations) — branch `fix/b-api-data`

### Requests

### Final DTO / error changes for C

### Summary

## Agent C (WPF client) — branch `fix/c-wpf`

### Requests

- [ ] `docs/STATUS.md` — add the desktop changes below (menu rules, item card needs ViewStock, log folder, AutomationId naming, field validation messages, concurrency reload) — CLAUDE.md rule, lead owns the file.
- [ ] Server (B) — the client relies on CONTRACTS.md as written: `code` on ProblemDetails, `Version` on the 4 DTOs/requests, GET `api/customers/{id}`, `api/suppliers/{id}`, `api/users/{id}`, `api/items/{id}` (used to reload a record after a 409), and the ReadAccess policy on the GETs. Without B's ReadAccess change, users with only CreateOrders/AddStock/ManageCatalog still get 403 from the server on the catalog lists (the menu is right, the server is not yet).
- [ ] No sln / Contracts / API changes needed from C.
- Package bumps (#10): Desktop and Client reference no Microsoft.* packages (only CommunityToolkit.Mvvm 8.2.2), nothing to bump. Logging is hand-written (no Serilog), so no new packages.

### Summary

| Issue | Commit | Regression tests (tests/OnTech.Store.Desktop.Tests) |
|---|---|---|
| H-3 Arabic digits | `98b6748` | `NumbersTests` (٥, ۱۲, 1٢, spaces, RTL mark, invalid, decimals ٫ ٬, server address, NewOrder/StockIn quantity, customer/supplier number, low stock, import sheet) |
| M-1 Version + 409 reload | `e653a08` | `ConcurrencyTests` (ApiError.Code, customer/supplier/item/user send Version, reload into form, form stays open, Arabic message, no Version on create) |
| MustChangePassword | `1f4f840` | `MustChangePasswordTests` (page raises Session.PasswordChangeRequired, no 403 banner; plain 403 unchanged; gate opens once, continues/signs out) |
| H-2 menu by ReadAccess | `25d41d8` | `MenuPermissionTests` (each single permission: every menu page loads with no 403 against a fake server enforcing ReadAccess; expected menu per permission; admin; Home without ViewStock; item card needs ViewStock; edit buttons need Manage*) |
| L-8 stale lists | `b8bc5c8` | `StaleResponseTests` (Home, Catalog items, Catalog categories, Orders; late answers with and without cancellation) |
| M-3 MaxLength + validation text | `449eaf9` | `ValidationTests` (every TextBox/PasswordBox has MaxLength; limits equal the DTO [MaxLength]; DataAnnotations messages parsed per field; English and Arabic per-field text; email checked before sending; every label/template translated) |
| M-5 log file | `0398157` | `LogTests` (daily file, keep 14, unobserved-task/domain/dispatcher handlers log full exception and SetObserved, API errors logged, failed sign-in logged without password, unwritable folder does not throw) |
| M-9 AutomationIds | `4569086` | `AutomationIdTests` (every interactive control in every XAML has a `<Screen>_<Control>` id, unique per file; LabeledBy targets exist and every field label is linked; NavItem name) |
| Translations | `ea25bfe` | `TranslationTests` (every `L.T("…")` and `{l:T '…'}` literal has Arabic, no duplicate keys, placeholders match, new server messages translated) |
| UI test locators | `a0f22ba` | `audit/OnTech.Store.UITests` builds; locators use AutomationIds |

Results: `dotnet build OnTech.Store.sln -c Debug` 0 errors, the only warning is the existing CS1573 in `DashboardController.cs`. Desktop.Tests 150/150 passed. Api.Tests 87/87 passed (SQLite; C changed no server code). UI tests: built, **not run** (lead: 5081 stopped).

Notes for the lead / A / B:
- `ApiError` now has `Code` (ProblemDetails `code`) and `FieldErrors` (parsed `errors` dictionary). `OnTech.Store.Client.Numbers` is the one digit/number helper (Client, so the import sheet and server address use it too). Voucher, receipt and invoice numbers are sent with Western digits.
- Log: `%LOCALAPPDATA%\OnTechStore\logs\OnTechStore-yyyyMMdd.log`, newest 14 kept. Tests write to a temp folder (module initializer), not the user's folder. The user's settings.json was not touched.
- A cancelled list load ends silently; `IsBusy` stays on until the last overlapping call ends.
- AutomationIds: `Login_UsernameBox`, `Login_PasswordBox`, `Login_SignInButton`, `Shell_Nav_<Page>` (Page enum: Home, NewOrder, StockIn, Customers, Suppliers, Orders, StockIns, Catalog, Users), `NewOrder_CategoryCombo/ItemCombo/QuantityBox/CustomerSearchBox/CustomerCombo/IssueVoucherNumberBox/SaveButton`, `Customers_StartNewButton/CompanyNameBox/…/SaveButton`, `Users_Permission_<Permission>` (bound, one per check box), etc. The x:Name-based ids the old UI tests used (`PasswordBox`) are replaced by `Login_PasswordBox`.
- Not done: no run-time (STA window) test of the ids. A draft hung waiting for the dispatcher and was dropped; the static XAML tests cover the ids.

How it looks in Arabic (RTL):
- No layout changes: everything still mirrors through `L.Apply`. MaxLength, AutomationIds and LabeledBy are not visible.
- Errors appear in the existing red banner, in Arabic, one line per field with the Arabic form label, e.g. `اسم الشركة: الحد الأقصى 200 حرفًا.`, `البريد الإلكتروني: عنوان البريد الإلكتروني غير صحيح.`, `أدخل بريدًا إلكترونيًا صحيحًا، أو اترك الحقل فارغًا.`
- A concurrency conflict shows `تم تعديل هذا السجل من جهاز آخر، وتمت إعادة تحميله. راجِع البيانات ثم احفظ مرة أخرى.` and the form stays open with the saved values.
- MustChangePassword opens the existing (RTL) change-password window, not a banner. Lockout shows `تم إدخال كلمة مرور خاطئة عدة مرات. انتظر 30 ثانية ثم حاول مرة أخرى.`
- Arabic-keyboard digits (٥, ۱۲) work in every number field. The menu only lists pages the user can open, so it is shorter for limited users, and Home shows only their action cards.
- Screen readers get Arabic names for menu items (page title) and fields (their Arabic label).
