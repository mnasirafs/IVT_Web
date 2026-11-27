"""Read-only source profiling (milestone A). Produces a Markdown data-contract evidence report.

All queries are SELECT-only with NOLOCK and are bounded to recent weeks where tables are large.
"""
from __future__ import annotations

import datetime as dt
from typing import Callable, List, Tuple

import pandas as pd

from .db import Database

SOURCES = [
    "tmp.IVT_ResultHistory", "tmp.Held_Exception_History", "dbo.Buy_Side_History", "dbo.Buy_Side",
    "dbo.Sell_Side_History", "dbo.Sell_Side", "tmp.Moveit_Consignment", "tmp.CurrentTradingData",
    "tmp.CurrentIVTResult", "dbo.ConsignmentForReview", "dbo.ReleasedConsignment", "tmp.IVT_TradingDataHistory",
]

# (title, sql, params). Large history tables are bounded to the last N weeks via a parameter.
def checks(recent_weeks: int) -> List[Tuple[str, str, tuple]]:
    since = "(SELECT DATEADD(WEEK, -?, MAX(CurrentWeek)) FROM {t} WITH (NOLOCK))"
    q: List[Tuple[str, str, tuple]] = []
    q.append(("SQL Server version", "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50)) AS version, CAST(SERVERPROPERTY('Edition') AS nvarchar(100)) AS edition, (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) AS compatibility_level", ()))
    q.append(("Login privileges (security review)", "SELECT SUSER_SNAME() AS login_name, USER_NAME() AS db_user, IS_ROLEMEMBER('db_owner') AS is_db_owner, IS_ROLEMEMBER('db_datareader') AS is_reader", ()))
    for t in ["dbo.Sell_Side_History", "dbo.Buy_Side_History", "tmp.IVT_ResultHistory"]:
        q.append((f"{t}: coverage", f"SELECT MIN(CurrentWeek) AS first_week, MAX(CurrentWeek) AS last_week, COUNT(DISTINCT CurrentWeek) AS weeks, COUNT_BIG(*) AS row_count FROM {t} WITH (NOLOCK)", ()))
        q.append((f"{t}: rows per week (last 8)", f"SELECT TOP 8 CurrentWeek, COUNT_BIG(*) AS row_count FROM {t} WITH (NOLOCK) GROUP BY CurrentWeek ORDER BY CurrentWeek DESC", ()))
    for t, extra in [("dbo.Sell_Side_History", ""), ("dbo.Buy_Side_History", "")]:
        q.append((f"{t}: lines per (week, jno) - last {recent_weeks} weeks",
                  f"SELECT lines_per_job, COUNT_BIG(*) AS jobs FROM (SELECT CurrentWeek, jno, CASE WHEN COUNT(*) > 4 THEN 5 ELSE COUNT(*) END AS lines_per_job FROM {t} WITH (NOLOCK) WHERE CurrentWeek > {since.format(t=t)} GROUP BY CurrentWeek, jno) x GROUP BY lines_per_job ORDER BY lines_per_job", (recent_weeks,)))
        q.append((f"{t}: customers/carriers per (week, jno) - last {recent_weeks} weeks",
                  f"SELECT SUM(CASE WHEN c > 1 THEN 1 ELSE 0 END) AS jobs_with_multiple_customer_carrier, COUNT_BIG(*) AS jobs FROM (SELECT CurrentWeek, jno, COUNT(DISTINCT customer + '|' + ISNULL(carrier, '')) AS c FROM {t} WITH (NOLOCK) WHERE CurrentWeek > {since.format(t=t)} GROUP BY CurrentWeek, jno) x", (recent_weeks,)))
        q.append((f"{t}: same jno in several weeks - last {recent_weeks} weeks",
                  f"SELECT weeks_seen, COUNT_BIG(*) AS jobs FROM (SELECT jno, CASE WHEN COUNT(DISTINCT CurrentWeek) > 3 THEN 4 ELSE COUNT(DISTINCT CurrentWeek) END AS weeks_seen FROM {t} WITH (NOLOCK) WHERE CurrentWeek > {since.format(t=t)} GROUP BY jno) x GROUP BY weeks_seen ORDER BY weeks_seen", (recent_weeks,)))
        q.append((f"{t}: amount parse quality - last {recent_weeks} weeks",
                  f"""SELECT COUNT_BIG(*) AS row_count,
                     SUM(CASE WHEN total_nett IS NULL OR LTRIM(total_nett) = '' THEN 1 ELSE 0 END) AS nett_missing,
                     SUM(CASE WHEN total_nett IS NOT NULL AND LTRIM(total_nett) <> '' AND TRY_CONVERT(decimal(18, 4), total_nett) IS NULL THEN 1 ELSE 0 END) AS nett_unparseable,
                     SUM(CASE WHEN TRY_CONVERT(decimal(18, 4), total_nett) = 0 THEN 1 ELSE 0 END) AS nett_zero,
                     SUM(CASE WHEN TRY_CONVERT(decimal(18, 4), total_nett) < 0 THEN 1 ELSE 0 END) AS nett_negative,
                     MAX(TRY_CONVERT(decimal(18, 4), total_nett)) AS nett_max,
                     SUM(CASE WHEN weight IS NOT NULL AND LTRIM(weight) <> '' AND TRY_CONVERT(decimal(18, 4), weight) IS NULL THEN 1 ELSE 0 END) AS weight_unparseable,
                     SUM(CASE WHEN chg_kg IS NOT NULL AND LTRIM(chg_kg) <> '' AND TRY_CONVERT(decimal(18, 4), chg_kg) IS NULL THEN 1 ELSE 0 END) AS chgkg_unparseable,
                     SUM(CASE WHEN cubic IS NOT NULL AND LTRIM(cubic) <> '' AND TRY_CONVERT(decimal(18, 6), cubic) IS NULL THEN 1 ELSE 0 END) AS cubic_unparseable
                     FROM {t} WITH (NOLOCK) WHERE CurrentWeek > {since.format(t=t)}""", (recent_weeks,)))
        q.append((f"{t}: cons_date / invoice_date formats - last {recent_weeks} weeks",
                  f"""SELECT TOP 12 fmt, COUNT_BIG(*) AS row_count FROM (
                     SELECT CASE WHEN cons_date IS NULL OR LTRIM(cons_date) = '' THEN 'cons_date:blank'
                                 WHEN cons_date LIKE '[0-9][0-9]/[0-9][0-9]/[0-9][0-9][0-9][0-9]%' THEN 'cons_date:dd/mm/yyyy'
                                 WHEN cons_date LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]%' THEN 'cons_date:yyyy-mm-dd'
                                 WHEN cons_date LIKE '[0-9][0-9]-[0-9][0-9]-[0-9][0-9][0-9][0-9]%' THEN 'cons_date:dd-mm-yyyy'
                                 WHEN cons_date LIKE '%/  /%' OR LTRIM(cons_date) LIKE '/%' THEN 'cons_date:placeholder'
                                 ELSE 'cons_date:other' END AS fmt FROM {t} WITH (NOLOCK) WHERE CurrentWeek > {since.format(t=t)}
                     UNION ALL
                     SELECT CASE WHEN invoice_date IS NULL OR LTRIM(invoice_date) = '' THEN 'invoice_date:blank'
                                 WHEN invoice_date LIKE '%/  /%' OR LTRIM(invoice_date) LIKE '/%' THEN 'invoice_date:placeholder'
                                 WHEN invoice_date LIKE '[0-9][0-9]/[0-9][0-9]/[0-9][0-9][0-9][0-9]%' THEN 'invoice_date:dd/mm/yyyy'
                                 WHEN invoice_date LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]%' THEN 'invoice_date:yyyy-mm-dd'
                                 ELSE 'invoice_date:other' END FROM {t} WITH (NOLOCK) WHERE CurrentWeek > {since.format(t=t)}) x
                     GROUP BY fmt ORDER BY COUNT_BIG(*) DESC""", (recent_weeks, recent_weeks)))
    q.append(("Identifier patterns: leading zeros / suffixes (Sell_Side current week)",
              """SELECT SUM(CASE WHEN con_note LIKE '0%' THEN 1 ELSE 0 END) AS connote_leading_zero,
                        SUM(CASE WHEN col_post LIKE '0%' OR del_post LIKE '0%' THEN 1 ELSE 0 END) AS postcode_leading_zero,
                        SUM(CASE WHEN con_note LIKE '%[^0-9A-Za-z]%' THEN 1 ELSE 0 END) AS connote_with_separator,
                        SUM(CASE WHEN jno <> jno_trimmed THEN 1 ELSE 0 END) AS jno_differs_from_trimmed,
                        SUM(CASE WHEN con_note <> LTRIM(RTRIM(con_note)) THEN 1 ELSE 0 END) AS connote_padded,
                        COUNT_BIG(*) AS row_count FROM dbo.Sell_Side WITH (NOLOCK)""", ()))
    q.append(("jno vs jno_trimmed examples", "SELECT TOP 8 jno, jno_trimmed, con_note FROM dbo.Sell_Side WITH (NOLOCK) WHERE jno <> jno_trimmed", ()))
    q.append(("Current buy/sell job matching (dbo.Sell_Side vs dbo.Buy_Side on jno)",
              """SELECT SUM(CASE WHEN b.n IS NULL THEN 1 ELSE 0 END) AS sell_jobs_without_buy,
                        SUM(CASE WHEN b.n = 1 THEN 1 ELSE 0 END) AS sell_jobs_with_one_buy_line,
                        SUM(CASE WHEN b.n > 1 THEN 1 ELSE 0 END) AS sell_jobs_with_multiple_buy_lines,
                        SUM(CASE WHEN b.n IS NOT NULL AND (b.customer <> s.customer OR b.carrier <> s.carrier) THEN 1 ELSE 0 END) AS customer_or_carrier_differs,
                        (SELECT COUNT(DISTINCT jno) FROM dbo.Buy_Side WITH (NOLOCK) WHERE jno NOT IN (SELECT jno FROM dbo.Sell_Side WITH (NOLOCK) WHERE jno IS NOT NULL)) AS buy_jobs_without_sell
                 FROM (SELECT jno, MIN(customer) customer, MIN(carrier) carrier FROM dbo.Sell_Side WITH (NOLOCK) GROUP BY jno) s
                 LEFT JOIN (SELECT jno, COUNT(*) n, MIN(customer) customer, MIN(carrier) carrier FROM dbo.Buy_Side WITH (NOLOCK) GROUP BY jno) b ON b.jno = s.jno""", ()))
    q.append(("Carriers (current week) - candidates for currency mapping (NZ?)",
              "SELECT TOP 40 carrier, COUNT_BIG(*) AS row_count, SUM(TRY_CONVERT(decimal(18,2), total_nett)) AS sell_nett FROM dbo.Sell_Side WITH (NOLOCK) GROUP BY carrier ORDER BY COUNT_BIG(*) DESC", ()))
    q.append(("CurrentTradingData grain", "SELECT COUNT_BIG(*) AS row_count, COUNT(DISTINCT JobNumber) AS jobs, COUNT(DISTINCT PrimaryKeyField) AS primary_keys, SUM(CASE WHEN Buy_Total_Nett IS NULL THEN 1 ELSE 0 END) AS missing_buy, SUM(CASE WHEN Moveit_TotWeightKg IS NULL THEN 1 ELSE 0 END) AS missing_moveit, SUM(CASE WHEN CustomerGroup IS NULL THEN 1 ELSE 0 END) AS unmapped_customer_group, SUM(CASE WHEN CarrierGroup IS NULL THEN 1 ELSE 0 END) AS unmapped_carrier_group, SUM(CASE WHEN AccountManager IS NULL THEN 1 ELSE 0 END) AS no_account_manager FROM tmp.CurrentTradingData WITH (NOLOCK)", ()))
    q.append(("CurrentTradingData duplicate jobs", "SELECT TOP 10 JobNumber, COUNT(*) AS row_count, COUNT(DISTINCT PrimaryKeyField) AS pks, COUNT(DISTINCT ISNULL(CAST(Buy_Total_Nett AS varchar(30)), 'null')) AS buy_values FROM tmp.CurrentTradingData WITH (NOLOCK) GROUP BY JobNumber HAVING COUNT(*) > 1", ()))
    q.append(("CurrentTradingData vs Sell_Side reconciliation", "SELECT (SELECT SUM(Sell_Total_Nett) FROM tmp.CurrentTradingData WITH (NOLOCK)) AS trading_sell_nett, (SELECT SUM(TRY_CONVERT(decimal(18,2), total_nett)) FROM dbo.Sell_Side WITH (NOLOCK)) AS sell_side_nett, (SELECT SUM(Buy_Total_Nett) FROM tmp.CurrentTradingData WITH (NOLOCK)) AS trading_buy_nett, (SELECT SUM(TRY_CONVERT(decimal(18,2), total_nett)) FROM dbo.Buy_Side WITH (NOLOCK)) AS buy_side_nett", ()))
    q.append(("Moveit coverage", "SELECT MIN(ActualDespatchDate) AS first_despatch, MAX(ActualDespatchDate) AS last_despatch, MIN(ActionTime) AS first_loaded, MAX(ActionTime) AS last_loaded, COUNT_BIG(*) AS row_count, COUNT(DISTINCT ConsignmentNo) AS consignments FROM tmp.Moveit_Consignment WITH (NOLOCK)", ()))
    q.append(("Rule history multiplicity (last weeks)", "SELECT TOP 8 CurrentWeek, COUNT_BIG(*) AS rows_, COUNT(DISTINCT jno) AS jobs, COUNT(DISTINCT ErrCode) AS rules FROM tmp.IVT_ResultHistory WITH (NOLOCK) GROUP BY CurrentWeek ORDER BY CurrentWeek DESC", ()))
    q.append(("Holds (ConsignmentForReview) by state", "SELECT IsDeleted, CASE WHEN ErrCode = '9999' THEN 'AM custom (manual)' ELSE 'rule-based' END AS hold_type, COUNT_BIG(*) AS holds, MIN(ActionTime) AS first_at, MAX(ActionTime) AS last_at FROM dbo.ConsignmentForReview WITH (NOLOCK) GROUP BY IsDeleted, CASE WHEN ErrCode = '9999' THEN 'AM custom (manual)' ELSE 'rule-based' END", ()))
    q.append(("Releases with justification categories", """SELECT TOP 15 CASE
               WHEN Justification IS NULL OR LTRIM(Justification) = '' THEN '(blank)'
               WHEN Justification LIKE '%credit%' THEN 'mentions credit'
               WHEN Justification LIKE '%rate%' AND Justification LIKE '%not an exception%' THEN 'rate change - not an exception'
               WHEN Justification LIKE '%advisory%' THEN 'advisory only'
               WHEN Justification LIKE '%as per AM%' OR Justification LIKE '%bulk%' THEN 'bulk / as per AM'
               ELSE 'other free text' END AS category, COUNT_BIG(*) AS releases FROM dbo.ReleasedConsignment WITH (NOLOCK)
             GROUP BY CASE
               WHEN Justification IS NULL OR LTRIM(Justification) = '' THEN '(blank)'
               WHEN Justification LIKE '%credit%' THEN 'mentions credit'
               WHEN Justification LIKE '%rate%' AND Justification LIKE '%not an exception%' THEN 'rate change - not an exception'
               WHEN Justification LIKE '%advisory%' THEN 'advisory only'
               WHEN Justification LIKE '%as per AM%' OR Justification LIKE '%bulk%' THEN 'bulk / as per AM'
               ELSE 'other free text' END ORDER BY COUNT_BIG(*) DESC""", ()))
    q.append(("Hold weekly volume (last 10 invoice weeks)", "SELECT TOP 10 InvoiceWeek, COUNT_BIG(*) AS holds, COUNT(DISTINCT jno) AS jobs FROM dbo.ConsignmentForReview WITH (NOLOCK) GROUP BY InvoiceWeek ORDER BY InvoiceWeek DESC", ()))
    return q


def column_metadata(db: Database) -> pd.DataFrame:
    frames = []
    for t in SOURCES:
        df = db.read_df(
            """SELECT ? AS source_table, c.column_id, c.name AS column_name, TYPE_NAME(c.user_type_id) AS data_type,
                      c.max_length, c.precision, c.scale, c.is_nullable
               FROM sys.columns c WHERE c.object_id = OBJECT_ID(?) ORDER BY c.column_id""", (t, t))
        frames.append(df)
    return pd.concat(frames, ignore_index=True)


def run_profile(db: Database, recent_weeks: int = 8, log: Callable[[str], None] = print) -> str:
    lines = [f"# IVT source profiling report (ACTUAL IVT DATA)", "",
             f"Generated {dt.datetime.now():%Y-%m-%d %H:%M}. Read-only queries; large history tables bounded to the last {recent_weeks} weeks unless stated.", ""]
    for title, sql, params in checks(recent_weeks):
        log(f"profiling: {title}")
        try:
            df = db.read_df(sql, params)
            lines += [f"## {title}", "", _md(df), ""]
        except Exception as exc:  # report and continue: profiling must not stop on one failing check
            lines += [f"## {title}", "", f"FAILED: `{str(exc)[:300]}`", ""]
    log("profiling: column metadata")
    meta = column_metadata(db)
    lines += ["## Column metadata (sys.columns)", ""]
    for table, grp in meta.groupby("source_table", sort=False):
        lines += [f"### {table}", "", _md(grp.drop(columns=["source_table"])), ""]
    return "\n".join(lines)


def _md(df: pd.DataFrame) -> str:
    if df.empty:
        return "_(no rows)_"
    cols = list(df.columns)
    out = ["| " + " | ".join(map(str, cols)) + " |", "|" + "---|" * len(cols)]
    for _, r in df.iterrows():
        out.append("| " + " | ".join("" if pd.isna(v) else str(v).replace("|", "\\|").replace("\n", " ")[:120] for v in r) + " |")
    return "\n".join(out)
