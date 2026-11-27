# IVT Administration: desktop parity, corrections and open dependencies

The functional reference is `DWH-Manager\WHFOperations` (WinForms) with `DAL\Adhoc.cs`. Contracts were verified against
`sys.procedures` / `sys.parameters` on AFS-SQL01 (IVT, IVT_Test) and msdb on AFS-SQL01-DEV\AFS_SQL01_DEV on 2026-10-09.

## Parity matrix

The web paths below are relative to the site. Services live in `Services/IvtAdmin`.

### Run control and file loads

| Desktop form / action | Web page (controller action) | Service / procedure / job | Status |
|---|---|---|---|
| Fin_IVT_Run: Run IVT | Run control (`IvtAdmin/RunControl?env=production`) → `StartRun kind=Production` | `RunControlService`: `sp_FileloadControl` (IVT, then IVT_Test) → `IVT_Engine` | Done |
| Fin_IVT_Run: Silent run | same page, `kind=Silent` | same → `IVT_Engine_Silent` | Done |
| Fin_IVT_Run: Azure run | same page, Azure card (`StartAzureRun`) | IVT, IVT_Test, Azure IVT prep → upload Buy/Sell → `PL_Run_IVT_Engine` | Done, disabled by default |
| Fin_IVT_Run: Refresh / job status / run grid | `RunStatus` (auto-refresh while running) | msdb by job name; `sp_Get_IVT_Run_Time` | Done |
| Fin_IVT_Run: Rollback | Rollback card (typed confirmation) | `Rollback_Invoice_Week_Load` on the selected env | Done |
| Fin_IVT_Run: open LZ folder / portal link | paths and links shown on the page | settings | Done (web: paths shown, not opened on the server) |
| Fin_IVT_Run: Email reports / Notify AMs / Notify execs | Notifications & reports (`Notify`, POST) | `LegacyReportApi` GET `attachfiles` / `notifyam` / `notifyexecs` | Done |
| Fin_IVT_Run: Mail control button | Reference settings → Email control | | Done |
| Fin_IVT_Test_Run: run / refresh / history / rollback / LZ / output | Test / UAT run (`RunControl?env=test`) | `sp_FileloadControl` on IVT_Test → `IVT_Engine_Test_Run` | Done |
| Fin_IVT_Test_Run: test notifications (Python) | Notifications → Send test notifications | `ProcessRunner` (exit code, output, timeout) | Done, disabled until configured |
| Fin_IVT_Fuel_Surcharge: REF load (LZ + `IVT_REF_DATA_LOAD`) | File loads → Reference data | `FileLoadService` → `IVT_REF_DATA_LOAD` | Done (template unverified, upload disabled) |
| Fin_IVT_Bulk_Hold | File loads → Bulk hold (validate & place file, start, status, errors) | `IVT_Hold_Release_Check` | Done |
| Fin_IVT_Bulk_Release | File loads → Bulk release | `IVT_Hold_Release_Check` | Done |
| Fin_IVT_BMS_Check: file landing / load | File loads → BMS check file | `IVT_Hold_Release_Check` | Done |

### Reference settings

| Desktop form / action | Web page (controller action) | Service / procedure / job | Status |
|---|---|---|---|
| Fin_IVT_AccountManager (+ _Test) | Reference settings → Account manager portfolios (prod/test) | `GetAccountManagerDetails` / `sp_UpdateAMPortfolio` / `sp_NewAMPortfolio` | Done |
| Fin_IVT_Rules (+ _Test, Fin_IVT_Test_Rules) | Rules (prod/test) | `GetRuleDetails` / `sp_UpdateRule` / `sp_NewRule` | Done (Test uses `@VCode`, see below) |
| Fin_IVT_RuleOption (+ _Test) | Rule options | `sp_getRuleOption` / `sp_UpdateRuleOption` / `sp_NewRuleOption` | Done |
| IVT_Fin_RuleExclusion (+ _Test) | Rule exclusions | `sp_getRuleExclusion` / `sp_UpdateRuleExclusion` / `sp_NewRuleExclusion` (`@IsDeleted = 0`) | Done |
| Fin_IVT_CustomerCarrier_Setting | Customer / carrier settings (all 26 fields, 8 sections) | `sp_getPrefixes` / `sp_UpdatePrefix` / `sp_NewPrefix` | Done. **Production table missing**, see below. |
| Fin_IVT_Fuel_Surcharge: grid | Fuel surcharges | `sp_getFuel` / `sp_UpdateFuel` / `sp_NewFuel` | Done. **Production table missing.** |
| Fin_IVT_ConsolidationCheck | Consolidation checks | `sp_getConsolodition` / `sp_UpdateConsolidation` / `sp_NewConsolidation` | Done. **Production procedures missing.** |
| Fin_IVT_Mail_Control (+ _Test) | Email control (toggle) + Shared email contacts | `GetEmailControlDetails` / `sp_UpdateEmailControl` / `sp_NewEmailControl`; `Get_CC_EmailContactDetails` / `sp_Update_CC_EmailContactDetails` | Done |

Every reference screen has search (blank means all), create, single-row save, save-all-displayed, soft delete/reactivate, and the toggle actions.

### Connotes, comments and reports

| Desktop form / action | Web page (controller action) | Service / procedure / job | Status |
|---|---|---|---|
| Fin_IVT_Hold_Connote (single / all) | Connotes → Manual hold / release, Hold mode | `sp_getIVTResultDetail` → `sp_Hold_Connote` | Done |
| Fin_IVT_Release_Connote (single / all) | Manual hold / release, Release mode | `dbo.vConsignmentForReview` → `sp_Release_Connote` | Done (source corrected, see below) |
| Fin_IVT_Query_Connote_For_Review | Reports → Connotes for review (+ CSV) | `IVT_GetConnoteFor_Review` (`@searchtxt`, `@From_Date`, `@To_Date`) | Done |
| Fin_IVT_Query_Accepted_Connote | Accepted connotes | `IVT_Get_Accepted_Connote` | Done |
| Fin_IVT_Query_Released_Connote | Released connote history | `IVT_Get_Released_Connote` | Done |
| Fin_IVT_BMS_Check: 4 reports | Held/Released not in BMS (filters), Held/Released not in IVT (no filters) | `IVT_Get_*_Not_IN_*` | Done |
| Fin_IVT_Add_Comments | Connotes → Comments | `sp_GetConsignmentLog`; `dbo.sp_Insert_ConsignmentLogBook`; email | Done |
| Fin_IVT_AM_Activity | Account manager activity (+ CSV) | `sp_getAM_Activity_List`, `sp_getAM_List`, `IVT_Get_AM_Activity` | Done (default activity ALL) |
| `Admin.IsIVT_Admin` menus | `[IvtAccess]` on every action + anti-forgery on every POST | `IvtAdminBaseController` | Done |

## Legacy behaviour preserved

**Run control**
- Procedure and parameter names are kept exactly, including `sp_getConsolodition`, `@Check_Reciever` and `@searchtxt`.
- A production or silent run prepares IVT, then IVT_Test (`productionRunPreparesTest`, default on), then starts the job.
- A test run prepares IVT_Test only.
- `sp_FileloadControl` is called with `@Type = 'All'`.

**Manual hold and release**
- `@ReleasedBy` = the row's HeldBy, `@ActionedBy` = the operator, `@InvoiceWeek` = the row's CurrentWeek.
- Hold one keeps the row's sub-reason.
- Hold all uses sub-reason `Custom Hold By IVT Admin` with the entered comment.
- Release one uses `Custom Release by IVT Admin`; release all uses `Custom Release By IVT Admin`. The desktop's differing capitalisation is kept.

**Comments**
- Saved with LogType `Hold`.
- The email goes to ADMIN_Contact, CC Finance_Contact and the operator.
- It has the same subject and table layout as the desktop.

**Other**
- Azure uploads overwrite blobs: top-level Buy and Sell files go to `Sapphire/Buy/` and `Sapphire/Sell/`.
- Search keyword semantics are verified per procedure:
  - Query procedures treat `'ALL'` as everything, and blank input is sent as `'ALL'`.
  - Settings procedures treat blank as everything.

## Intentional corrections

### Run control

| Topic | Desktop | Web |
|---|---|---|
| Default invoice week | Can pick a Friday two weeks back (Saturday to Tuesday) | Most recent Friday strictly before today, in the business time zone. Missing or future dates are rejected; a non-Friday date gives a warning, because IVT has used Wednesday weeks (e.g. 2026-09-30). |
| Missing job | `Jobs[...]?.Start()` silently does nothing | Checked before any preparation; reported as an error |
| Job GUIDs | Hard-coded | Jobs found by configured name |
| Start outcome | "Running…" shown as soon as Start was called | Outcome is SUBMITTED, with the job observed executing when possible. Never shown as completion. A lost response is AMBIGUOUS, with a "do not start again" warning. |
| Concurrency | None | `sp_getapplock` across web servers, conflicting-job check (including the chained jobs), and a block on a second start while a recent submission is not yet visible |
| Azure | Ran automatically with an empty catch | Off unless configured; each stage reported; run id captured; status lookup; ambiguous start not retried |
| Rollback | No confirmation | Typed confirmation (`ROLLBACK PRODUCTION` / `ROLLBACK TEST`); refused while jobs run; audited |

### Manual hold and release

| Topic | Desktop | Web |
|---|---|---|
| Release source | Read `ErrCode`, `HeldBy`, `CurrentWeek`, `Justification` etc. from `IVT_GetConnoteFor_Review`, which does not return them (verified), so it fails at run time | Candidates read from `dbo.vConsignmentForReview`, the view that procedure reads, which has every field `sp_Release_Connote` needs. Justification is entered by the operator. |
| Hold comment | Read a `Comment` column that `sp_getIVTResultDetail` does not return | Single hold records the operator's optional comment |
| Trusted values | Values from editable grid cells | Targets and all procedure values are re-read from the database for the same search. "All displayed" is refused if the result set changed. |
| Results | "All held successfully" even after failures | Results are reported per row (SUCCEEDED / PARTIAL / FAILED) |

### Reference settings

| Topic | Desktop | Web |
|---|---|---|
| Update all | Wrote every grid row; type errors replaced with `"0"` | Re-reads the displayed set and refuses if it changed. Writes changed rows only; unchanged rows are reported. Strict parsing per SQL type (bigint, int, bit, float, money, decimal(18,6)), with no silent fallback. NULL stays NULL. |
| Types | `int` vs `long` for rules | The real parameter types are sent: `sp_UpdateRule @OnOff int / @Mandatory bit`, `sp_NewRule @OnOff bit / @Mandatory int`, `@ErrCode bigint`; AM `@IsDeleted int` |

### Comments, notifications and exports

| Topic | Desktop | Web |
|---|---|---|
| Comment email | Failure after save shown as a generic error; values not HTML-encoded | "Comment saved; notification failed", with a retry that never re-inserts. Values are HTML-encoded. Operator email comes from the configured map or domain. |
| Notifications | GET from the UI; no duplicate guard | POST endpoints; a repeat within 15 minutes needs confirmation; audited. Test notifier: success only on exit code 0 within the timeout. |
| Exports | Written to a share folder and opened with Explorer | Browser CSV download with filters in the preamble. Quotes, commas and newlines are escaped; formulas are neutralised; NULL is empty. On-screen tables show the first 2,000 rows, while the export has all rows. |

## Verified environment differences (2026-10-09)

1. **Production IVT has no `dbo.REF_PREFIX`, `dbo.REF_FUELSURCHARGE` or `dbo.REF_ConsolidationCheck`, and no `sp_getConsolodition` / `sp_UpdateConsolidation`.**
   - The prefix, fuel and consolidation procedures in IVT fail with SQL error 208 or 2812, so the desktop screens fail too.
   - The web screens show this error. They are not rerouted to IVT_Test.
   - IVT_Test has the tables and procedures, and those screens work there.
   - **Action: decide where these settings live for production** (restore the tables or procedures in IVT, or confirm the engine reads them elsewhere).
2. **IVT_Test `sp_UpdateRule` / `sp_NewRule` take `@VCode` instead of `@RuleType`** (same `varchar(20)`). The module sends `@VCode` on Test only.
3. `sp_GetConsignmentLog` does not exist in IVT_Test, so comments are production-only, as in the desktop.
4. `IVT_Run_Control.Finance_Contact` ≠ `CC_Contact`; they hold different values.
   - Comment notifications CC **Finance_Contact**, as the desktop does.
   - The bulk hold/release loader emails **CC_Contact**.
   - The contacts screen maintains CC, EXEC and ADMIN only. `Finance_Contact` has no maintenance procedure.
5. `sp_Run_IVT` (`@Task`, `@InvoiceWeek`, `@MessageCode OUT`) exists but is **not** used. Kickoff is the SQL Agent job, as in the desktop.

## Unverified external dependencies and how to validate them

| Dependency | Why unverified | How to validate |
|---|---|---|
| REF data file name/format | `Prod_REF_Data_Update.dtsx` flat-file paths are not package parameters | Open the package in SSDT (Flat File connection managers "FUEL" and "Prefix"), then set `paths.referenceDataFilePattern` and add a template |
| Report service `afs-web01:84` with the app pool identity | It was called with operator credentials before | From the web server, run `Invoke-WebRequest http://afs-web01:84/attachfiles -UseDefaultCredentials` as the pool identity, at a time when sending is acceptable |
| Test notifier from the web server | Requires Python and share access for the pool identity | Enable `testNotifier` on the TEST site, run once, and check exit code 0 and the output |
| SQLAgentOperatorRole for the pool identity | Not granted yet | Run section 3 of `validate-contracts.sql` as that login |
| Azure (storage, ADF, Azure IVT) | Disabled; no credential configured | Grant a managed identity or service principal (Storage Blob Data Contributor on the container, Data Factory Contributor on the factory), set `azure.*`, then use "Check Azure run" |
| Real job starts, rollback, notifications | Deliberately **not** executed during development | On the TEST site, run a test run and test rollback first, then a production run in the normal weekly window |

## Security findings in the reference sources (rotate)

- `WHFOperations\Fin_IVT_Run.cs` contains an Azure Storage **account key** for `afsdwhlanding` and an Entra **client secret** for the ADF service principal. Rotate both; the web module does not use them.
- `JobNumberToCheck_File_Load.py` on `\\AFS-SQL01-DEV\AFS-Collab` contains a hard-coded SQL password (`report_subscriber`).
- `AFSDataGateway\Controllers\Helper.cs` still contains the SMTP password (existing TODO). Use `smtp.*` with an environment variable instead.
