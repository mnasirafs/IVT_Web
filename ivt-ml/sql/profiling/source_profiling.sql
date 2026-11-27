/* Read-only source profiling queries (the same checks run by `ivt-ml profile`). Safe to run in SSMS.
   Large history tables are bounded to recent weeks. */
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- Version / compatibility
SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50)) AS version, (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) AS compat;

-- Grain: lines per (week, jno) in the last 8 weeks (expected: 1)
DECLARE @since DATE = (SELECT DATEADD(WEEK, -8, MAX(CurrentWeek)) FROM dbo.Sell_Side_History);
SELECT lines_per_job, COUNT_BIG(*) AS jobs FROM (SELECT CurrentWeek, jno, COUNT(*) AS lines_per_job FROM dbo.Sell_Side_History WHERE CurrentWeek > @since GROUP BY CurrentWeek, jno) x GROUP BY lines_per_job;
SELECT lines_per_job, COUNT_BIG(*) AS jobs FROM (SELECT CurrentWeek, jno, COUNT(*) AS lines_per_job FROM dbo.Buy_Side_History WHERE CurrentWeek > @since GROUP BY CurrentWeek, jno) x GROUP BY lines_per_job;

-- Current week buy/sell matching on jno
SELECT SUM(CASE WHEN b.n IS NULL THEN 1 ELSE 0 END) AS sell_without_buy, SUM(CASE WHEN b.n = 1 THEN 1 ELSE 0 END) AS one_buy, SUM(CASE WHEN b.n > 1 THEN 1 ELSE 0 END) AS several_buy
FROM (SELECT DISTINCT jno FROM dbo.Sell_Side) s LEFT JOIN (SELECT jno, COUNT(*) n FROM dbo.Buy_Side GROUP BY jno) b ON b.jno = s.jno;

-- Reconciliation: CurrentTradingData vs raw tables (buy differs by buy-only jobs, which CurrentTradingData omits)
SELECT (SELECT SUM(Sell_Total_Nett) FROM tmp.CurrentTradingData) AS trading_sell, (SELECT SUM(TRY_CONVERT(decimal(18,2), total_nett)) FROM dbo.Sell_Side) AS sell_side,
       (SELECT SUM(Buy_Total_Nett) FROM tmp.CurrentTradingData) AS trading_buy, (SELECT SUM(TRY_CONVERT(decimal(18,2), total_nett)) FROM dbo.Buy_Side) AS buy_side;

-- Date formats
SELECT TOP 10 CASE WHEN cons_date LIKE '[0-9][0-9]-[0-9][0-9]-[0-9][0-9][0-9][0-9]' THEN 'dd-mm-yyyy' WHEN cons_date LIKE '[0-9]-[0-9][0-9]-[0-9][0-9][0-9][0-9]' THEN 'd-mm-yyyy'
       ELSE cons_date END AS fmt, COUNT_BIG(*) n FROM dbo.Sell_Side GROUP BY CASE WHEN cons_date LIKE '[0-9][0-9]-[0-9][0-9]-[0-9][0-9][0-9][0-9]' THEN 'dd-mm-yyyy'
       WHEN cons_date LIKE '[0-9]-[0-9][0-9]-[0-9][0-9][0-9][0-9]' THEN 'd-mm-yyyy' ELSE cons_date END ORDER BY n DESC;

-- Holds and releases (label sources)
SELECT IsDeleted, CASE WHEN ErrCode = '9999' THEN 'manual (AM custom)' ELSE 'rule-based' END AS hold_type, COUNT_BIG(*) AS holds FROM dbo.ConsignmentForReview GROUP BY IsDeleted, CASE WHEN ErrCode = '9999' THEN 'manual (AM custom)' ELSE 'rule-based' END;
SELECT TOP 25 LEFT(Justification, 120) AS justification, COUNT_BIG(*) AS releases FROM dbo.ReleasedConsignment GROUP BY LEFT(Justification, 120) ORDER BY releases DESC;

-- Carriers that may be NZD (candidates for ML.CurrencyMap; requires business confirmation)
SELECT carrier, COUNT_BIG(*) AS lines FROM dbo.Sell_Side WHERE carrier LIKE 'NZ%' GROUP BY carrier ORDER BY lines DESC;
