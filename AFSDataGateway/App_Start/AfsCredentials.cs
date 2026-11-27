using System.Net;

namespace AFSDataGateway.App_Start
{
    public static class AfsCredentials
    {
        // SMTP credentials were removed: IVT email is sent through Microsoft Graph.

        // SQL credential as tuple
        public static System.Tuple<string, string> GetSqlAuth()
        {
            return System.Tuple.Create(SqlUser, SqlPass);
        }

        public const string SqlUser = "report_subscriber";
        public const string SqlPass = "P@ssw0rd";
    }
}


