# IVT Administration module: setup

The module is part of the IVT 2.0 web application (`AFSDataGateway`). It is an administration and orchestration
layer around the existing IVT engine: it calls the existing `WebIVT` procedures and SQL Agent jobs and never
reimplements vetting logic or writes to IVT tables directly.

Navigation: admin sidebar, **IVT administration** group (Engine & loads, Reference settings, Connotes & reports).
Start with **Overview & health → Run health check**, which runs a read-only check of every dependency below.

## 1. Settings file

| Item | Value |
|---|---|
| File | `AFSDataGateway\App_Data\ivt-admin.settings.json` (git-ignored) |
| Template | `App_Data\ivt-admin.settings.example.json` (placeholders only) |
| Alternative location | appSetting `IVT_ADMIN_SETTINGS_PATH` |

- The file is re-read automatically when it changes.
- If it is missing, every module page shows "not configured". Nothing falls back to another database.
- Unknown keys are rejected, so typos surface instead of being ignored.

### Connections

These are SQL Server connection strings. Integrated Security is recommended, using the application pool identity.

| Key | Database | Used for |
|---|---|---|
| `production` | IVT (AFS-SQL01) | Production settings, queries, hold/release, comments, rollback |
| `test` | IVT_Test (AFS-SQL01) | Test run preparation, test settings, test rollback |
| `msdb` | msdb on the **SQL Agent server** (AFS-SQL01-DEV\AFS_SQL01_DEV) | Job status and `sp_start_job`. This is a different server from IVT. |
| `audit` | IVT (or a separate database holding migration V012) | `IvtAdmin.AuditLog` and the application locks (`sp_getapplock`) that coordinate starts across web servers |
| `azureIvt` | Azure IVT | Optional. Only for the Azure path. |

How the environment is chosen:
- It comes only from the page route value (`env=production|test`).
- Connection strings and database names are never taken from requests.
- Every page shows a red PRODUCTION or amber TEST badge.

### Other settings

- **`jobs.*`**: SQL Agent job names. The defaults are the desktop names. Jobs are looked up by name, never by GUID.
- **`conflictingJobs`**: jobs that must be idle before preparation, kickoff, rollback or a load. The defaults include `IVT_Engine_Execs_Reports`, because `IVT_Engine` starts it and it starts the test run in turn.
- **`productionRunPreparesTest`** (default `true`): legacy behaviour. A production or silent run also runs `sp_FileloadControl` on IVT_Test, after IVT. This is shown on the Run control page.
- **`productionRunMirrorsToAzure`** (default `false`): the legacy SQL-plus-Azure mode. When enabled, each Azure stage is reported separately.
- **`businessTimeZone`**: used for the default invoice week and future-date validation.
- **`paths.*`**: landing, admin-load and output folders (UNC paths).
- **`portal.*`, `legacyReportApi.baseUrl`**: legacy URLs.
- **`testNotifier`**: Python executable and arguments for `Send_Notification_Test.py`. Disabled by default.
- **`comments.emailMap` / `comments.emailDomain`**: how the commenting operator's email address is resolved for the CC line.
- **`access.readerLogins` / `access.readerGroups`**: IvtRead users who are not IVT administrators.
- **`smtp.*`**: comment notifications.
  - The password is read from the environment variable named in `passwordEnvironmentVariable`.
  - If `host` is empty, the application's existing IVT mail transport is used. Note that its SMTP password is still hard-coded in `Controllers\Helper.cs` (existing TODO).
- **`azure.*`**: optional.
  - The client secret comes from the environment variable named in `clientSecretEnvironmentVariable`.
  - If that is empty, the host's managed identity is used.
  - Never put keys in the file.

## 2. Database objects

Apply migration **V012** (`ivt-ml/sql/migrations/V012__ivt_admin_audit.sql`) to the audit database.
- It only creates schema `IvtAdmin` and table `IvtAdmin.AuditLog`.
- It was applied to IVT on 2026-10-09.
- No existing IVT table or procedure is created or altered by the module.

Run `docs/ivt-admin/validate-contracts.sql` (read-only) to confirm procedure contracts in each database. The in-app health check does the same.

## 3. Identity and permissions

### Authorization

**IvtAdmin**
- An active IVT user (`vIVTUser`) with UserType 1 or 2.
- The real Windows/Entra identity is used; the "Login As" impersonation never applies here.
- Required for every POST: runs, rollback, settings changes, hold/release, comments, uploads, loads and notifications.

**IvtRead**
- IVT administrators, plus the logins and groups in `access`.
- Can open the query reports and export CSV.

Enforcement:
- Every action is checked on the server, with a secure default of admin.
- Every POST also requires the MVC anti-forgery token.
- ActionedBy, CreatedBy and the audit operator always come from the authenticated identity, never from the client.

### SQL permissions for the identity in the connection strings

On **IVT** and **IVT_Test**:
- `EXECUTE` on the `WebIVT` procedures listed in `validate-contracts.sql` and on `dbo.sp_Insert_ConsignmentLogBook`.
- `SELECT` on `WebIVT.IVT_Run_Control` (comment notification contacts) and `dbo.vConsignmentForReview` (release candidates).
- `VIEW DEFINITION` for the health check (optional).

The procedures themselves read and write AFSXchange/AFSXchange1 in places (for example `sp_UpdateAMPortfolio`), so ownership chaining or permissions there must already work, as they do for the desktop.

On the **audit** database:
- `SELECT`, `INSERT`, `UPDATE` on `IvtAdmin.AuditLog`.
- `EXECUTE` on `sp_getapplock` / `sp_releaseapplock`. These are public by default.

On **msdb** (Agent server):
- Membership of **SQLAgentOperatorRole**, which is needed to start jobs owned by other logins.
- `SELECT` on `sysjobs`, `sysjobsteps`, `sysjobactivity`, `sysjobhistory`, `sysjobservers` and `syssessions`.

### File shares (application pool identity)

| Folder | Access |
|---|---|
| `Admin_File_Load\Hold_Connotes`, `Release_Connotes`, `BMS_Check` | modify (upload) |
| `Admin_File_Load` | read (loader `error_*.txt` files) |
| `IVT_landing`, `Buy`, `Sell`, `REF_DATA_UPDATE` | read |

### Other identity requirements

- **Report service** (`legacyReportApi.baseUrl`): called with the application pool's Windows credentials, so that identity must be authorised there. The desktop used the operator's own credentials.
- **Test notifier**: the application pool identity must be able to run Python and read the script share. Leave it disabled until this is confirmed.

## 4. SQL Agent jobs (verified on AFS-SQL01-DEV\AFS_SQL01_DEV, 2026-10-09)

| Job | Steps |
|---|---|
| `IVT_Engine` | 1 SSIS `Prod_IVT_File_Load.dtsx`; 2 `runPy_Send_Files.bat`; 3 starts `IVT_Engine_Execs_Reports`, which starts `IVT_Engine_Test_Run` |
| `IVT_Engine_Silent` | SSIS load; admin-only report emails; step 6 starts `IVT_Engine_Test_Run` |
| `IVT_Engine_Test_Run` | SSIS `Prod_Test_IVT_File_Load.dtsx`; test reports and notifications |
| `IVT_REF_DATA_LOAD` | SSIS `Prod_REF_Data_Update.dtsx` (fuel and prefix flat files) |
| `IVT_Hold_Release_Check` | PowerShell `RunPy_JobNumberToCheck_File_Load.bat`. It processes **BMS_Check, Hold_Connotes and Release_Connotes in one run**. |

Job history (`sysjobhistory`) is currently empty on the Agent server, so status falls back to `sysjobservers` (last outcome and message).

## 5. File templates (verified from `JobNumberToCheck_File_Load.py`)

Column names are case-sensitive.

| Folder | Required CSV columns | Loader action |
|---|---|---|
| `Hold_Connotes\*.csv` | `jno` (whole number), `Connote`, `Comment` | `tmp.Bulk_Hold`, then `WebIVT.sp_Bulk_Hold`; emails CC_Contact |
| `Release_Connotes\*.csv` | `jno` (whole number), `Connote`, `Justification` | `tmp.Bulk_Release`, then `WebIVT.sp_Bulk_Release` |
| `BMS_Check\*.csv` | `jno` | `tmp.BMS_Check` (replaced each load) |
| `REF_DATA_UPDATE` | **unverified**: the SSIS flat-file names are not package parameters | upload disabled; place the file as before |

How uploads are published:
1. Each upload is validated against its template.
2. It is written as `<name>.<random>.uploading`, which the loader's `*[.csv]` pattern does not match.
3. It is then renamed atomically.
4. File names are restricted to safe characters, and existing files are never overwritten.

Loader errors appear on the File loads page; the loader writes them as `Admin_File_Load\error_*.txt`.

## 6. Verification

- `tests\IvtAdmin.Verification\run.ps1` builds the site and runs 38 checks against recording fakes. No database, job, share, mail or Azure call is made.
- The health check (Overview page) and `validate-contracts.sql` are read-only checks against the real environment.
