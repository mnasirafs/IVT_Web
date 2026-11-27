using AFSDataGateway.Models;
using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;

namespace AFSDataGateway.Services.Chat
{
    // Admin on/off switch for Vera, stored in ML.AppSetting (migration V013) so it survives redeploys and applies to every server.
    // Read on every page render, so the value is cached briefly; Set() refreshes the cache immediately on this server.
    public static class AssistantSwitch
    {
        private const string Key = "ASSISTANT_ENABLED";
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
        private static readonly object Gate = new object();
        private static bool cached = true;
        private static DateTime cachedUntil = DateTime.MinValue;

        public sealed class State
        {
            public bool On;
            public string UpdatedBy;
            public DateTime? UpdatedAtUtc;
            public bool TableMissing;
        }

        public static bool IsOn
        {
            get
            {
                lock (Gate) { if (DateTime.UtcNow < cachedUntil) return cached; }
                bool value;
                try { value = Read().On; }
                catch (Exception ex) { Trace.TraceError("Assistant switch read failed: {0}", ex.GetType().Name); value = cached; }
                lock (Gate) { cached = value; cachedUntil = DateTime.UtcNow.Add(CacheFor); }
                return value;
            }
        }

        // Missing table or row means on: the switch only ever turns Vera off on purpose.
        public static State Read()
        {
            using (var db = new IVTEntities())
            {
                db.Database.CommandTimeout = 10;
                if (db.Database.SqlQuery<int>("SELECT CASE WHEN OBJECT_ID(N'ML.AppSetting', N'U') IS NULL THEN 0 ELSE 1 END").First() == 0)
                    return new State { On = true, TableMissing = true };
                var row = db.Database.SqlQuery<Row>(
                    "SELECT SettingValue, UpdatedBy, UpdatedAt FROM ML.AppSetting WHERE SettingKey = @key",
                    new SqlParameter("@key", Key)).FirstOrDefault();
                return row == null
                    ? new State { On = true }
                    : new State { On = !string.Equals(row.SettingValue, "false", StringComparison.OrdinalIgnoreCase), UpdatedBy = row.UpdatedBy, UpdatedAtUtc = row.UpdatedAt };
            }
        }

        public static void Set(bool on, string actor)
        {
            using (var db = new IVTEntities())
            {
                db.Database.ExecuteSqlCommand(
                    @"IF OBJECT_ID(N'ML.AppSetting', N'U') IS NULL
                          THROW 50001, N'ML.AppSetting is missing. Run migration V013 first.', 1;
                      MERGE ML.AppSetting AS t
                      USING (SELECT @key AS SettingKey) AS s ON t.SettingKey = s.SettingKey
                      WHEN MATCHED THEN UPDATE SET SettingValue = @value, UpdatedBy = @actor, UpdatedAt = SYSUTCDATETIME()
                      WHEN NOT MATCHED THEN INSERT (SettingKey, SettingValue, UpdatedBy) VALUES (@key, @value, @actor);",
                    new SqlParameter("@key", Key), new SqlParameter("@value", on ? "true" : "false"), new SqlParameter("@actor", actor));
            }
            lock (Gate) { cached = on; cachedUntil = DateTime.UtcNow.Add(CacheFor); }
        }

        public sealed class Row
        {
            public string SettingValue { get; set; }
            public string UpdatedBy { get; set; }
            public DateTime? UpdatedAt { get; set; }
        }
    }
}
