using System;
using System.IO;
using System.Web;
using System.Web.Mvc;

namespace AFSDataGateway.Services
{
    // Writes unhandled MVC errors to a daily log file so failures that the error page hides can be diagnosed.
    // Azure App Service: D:\home\LogFiles\IVT\ (Kudu > Debug console > LogFiles\IVT). On-prem: App_Data\Logs\.
    public static class ErrorLog
    {
        private static readonly object Sync = new object();

        private static string Folder
        {
            get
            {
                var home = Environment.GetEnvironmentVariable("HOME");
                if (!string.IsNullOrEmpty(home) && Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") != null)
                    return Path.Combine(home, "LogFiles", "IVT");
                return HttpRuntime.AppDomainAppPath == null ? Path.GetTempPath() : Path.Combine(HttpRuntime.AppDomainAppPath, "App_Data", "Logs");
            }
        }

        public static void Write(Exception ex, HttpContextBase context)
        {
            try
            {
                var request = context != null ? context.Request : null;
                var user = context != null && context.User != null && context.User.Identity != null ? context.User.Identity.Name : "";
                var entry = string.Format("{0:yyyy-MM-dd HH:mm:ss} UTC | {1} {2} | user={3}{4}{5}{4}{4}",
                    DateTime.UtcNow, request != null ? request.HttpMethod : "", request != null ? request.RawUrl : "", user, Environment.NewLine, ex);
                System.Diagnostics.Trace.TraceError(entry);
                lock (Sync)
                {
                    Directory.CreateDirectory(Folder);
                    File.AppendAllText(Path.Combine(Folder, "errors-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".log"), entry);
                }
            }
            catch
            {
                // Logging must never break the request.
            }
        }
    }

    public class LogErrorAttribute : HandleErrorAttribute
    {
        public override void OnException(ExceptionContext filterContext)
        {
            if (filterContext != null && filterContext.Exception != null)
                ErrorLog.Write(filterContext.Exception, filterContext.HttpContext);
            base.OnException(filterContext);
        }
    }
}
