# Baseline before the fixes (2026-11-08)

Code: `fix/audit-main` @ `73c1f94` (= `pre-fix` `d8e7ba7` + the audit folder + shared contract types, no behaviour change).
Build: `dotnet build OnTech.Store.sln -c Debug`: 0 errors, 1 warning (CS1573 `DashboardController.cs:18`, issue L-10).
Servers: 5081 = `audit\build\api-sqlserver` (Production, `.\SQLEXPRESS\OnTechStore`), 5082 = `audit\build\api-sqlite` (Development, SQLite copy). Both built from `pre-fix`.
Raw output: `audit/fix/results/base_*.txt` (+ `.trx`, git-ignored).

| Suite | Result | Matches the audit? |
|---|---|---|
| `tests/OnTech.Store.Api.Tests`, SQLite | **87 / 87 passed** (36 s) | yes |
| `tests/OnTech.Store.Api.Tests`, SQL Server | **87 / 87 passed** (21 s) | yes |
| `audit/OnTech.Store.Tests` | **63 passed / 22 failed of 85** (5 m 15 s) | yes: the audit's 63/21 of 84 plus the rewritten double-save test (85 now); the extra failure below is the 50k performance test |
| `audit/OnTech.Store.UITests` (FlaUI) | **9 passed / 4 failed / 1 skipped** (2 m 21 s) | yes |

## Audit tests failing at baseline (22)

| Test | Issue |
|---|---|
| `A_page_shown_in_the_menu_can_load_its_data` ×4 | H-2 |
| `A_token_forged_from_the_repository_signing_key_is_refused` ×2 | H-4 |
| `Arabic_Indic_digits_are_accepted_as_a_quantity` | H-3 |
| `Two_PCs_editing_the_same_customer_do_not_silently_lose_an_edit` ×2 | M-1 |
| `User_who_must_change_password_cannot_use_the_api_before_changing_it` ×2 | M-2 |
| `Voucher_uniqueness_ignores_letter_case_on_both_databases` (sqlite) | M-4 |
| `Parallel_customer_number_changes_end_in_409_not_500` ×2 | M-8 |
| `Paging_and_search_limits_return_400_not_500` (sqlserver) | L-1 |
| `Repeated_wrong_passwords_lock_or_slow_down_the_login` ×2 | L-2 |
| `A_user_with_no_permissions_cannot_read_customers_or_suppliers` ×2 | L-3 |
| `User_id_columns_have_foreign_keys` | L-6 |
| `Customers_page_does_not_load_every_customer_at_once` | L-9 |
| `Screens_stay_fast_with_50k_customers_and_orders` (sqlserver) | L-9 (50k rows) |

## UI tests at baseline

- Passed (9): sign-in errors ×3, open all 9 pages, add + search customer, New Order invalid input, New Order save, Orders log search, Arabic sign-in.
- Failed (4): `Customer_with_invalid_email…` (M-3, English text), `Stock_in_saves_and_adds_stock` and `Item_card_opens_and_exports` (locators, M-9), `Rapid_double_click_on_save_creates_one_order` (locator timing; it passed in the audit's run and `New_order_saves…` failed instead, so these two locator tests are flaky until M-9).
- Skipped (1): 50k-row screen timing (owner: data is small).

## Harness notes found while taking the baseline

- `BindingTraceTests` fails with "Cannot create more than one System.Windows.Application" when it runs in the same process as the ViewModel tests. Run alone it passes (1/1). It is a test-isolation issue, not an app bug; the lead runs it in its own `dotnet test --filter` in Phase 3.
- `Live_sqlite_copy_has_every_model_column` needs `audit\backups\ontech-store-dev_before_audit.db` inside the worktree (git-ignored). After copying it there it passes.
- `PerformanceTests` seeds 50k customers into `OnTechStore` on 5081, which then makes the UI tests time out. The lead restores `OnTechStore` from `OnTechStore_before_audit.bak` (`audit\restore.sql`) before every UI run.
- The admin on the 5081 test DB still has `MustChangePassword = 1`. Once M-2 is enforced, the audit and UI tests that sign in as admin need that flag cleared on the **test** DBs (lead, Phase 3).
- Some audit tests encode the old API shape and must be adjusted by the lead when the fixes land (documented in FIX_REPORT.md): the Arabic-digit test calls `int.TryParse` directly (it must call the new helper), the concurrency test sends no `Version` (it must send the loaded `Version` as the real client does), and `Swagger_is_reachable_without_signing_in` must expect 404 on the Production server.
