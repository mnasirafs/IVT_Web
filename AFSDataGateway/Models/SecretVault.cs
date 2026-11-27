using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;

namespace AFSDataGateway.Models
{
    public static class SecretVault
    {
        private const string SqlUser = "report_subscriber";
        private const string SqlPass = "P@ssw0rd";

        // Retained for compatibility with existing application startup code.
        public static void Init(string configPath)
        {
        }

        public static void Reload()
        {
        }

        public static string Get(
            string key,
            DataProtectionScope scope = DataProtectionScope.CurrentUser)
        {
            if (string.Equals(key, "sql_user", StringComparison.OrdinalIgnoreCase))
                return SqlUser;

            if (string.Equals(key, "sql_pass", StringComparison.OrdinalIgnoreCase))
                return SqlPass;

            throw new KeyNotFoundException("Credential '" + key + "' was not found.");
        }

        // SMTP credentials were removed: IVT email is sent through Microsoft Graph (Services/Entra/GraphMail).

        public static Tuple<string, string> GetSqlAuth()
        {
            return Tuple.Create(SqlUser, SqlPass);
        }
    }
}
