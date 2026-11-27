using System;
using System.IO;
using System.Web;
using System.Web.Hosting;

[assembly: PreApplicationStartMethod(typeof(AFSDataGateway.App_Start.ErrorLogger), "Init")]

namespace AFSDataGateway.App_Start
{
    public static class ErrorLogger
    {
        public static void Init()
        {
            // Register HTTP module to capture unhandled exceptions
            HttpApplication.RegisterModule(typeof(ErrorModule));
        }
    }

    public class ErrorModule : IHttpModule
    {
        public void Init(HttpApplication context)
        {
            context.Error += Context_Error;
        }

        private void Context_Error(object sender, EventArgs e)
        {
            try
            {
                var app = (HttpApplication)sender;
                var ex = app.Server.GetLastError();
                var user = app.Context?.User?.Identity?.Name ?? "anonymous";
                var path = app.Context?.Request?.RawUrl ?? "unknown";
                var msg = $"{DateTime.UtcNow:O} | User:{user} | Path:{path}{Environment.NewLine}{ex}{Environment.NewLine}--------------------{Environment.NewLine}";
                var logPath = app.Server.MapPath("~/App_Data/errors.log");

                // Ensure directory exists
                var dir = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.AppendAllText(logPath, msg);
            }
            catch
            {
                // Swallow to avoid recursive errors
            }
        }

        public void Dispose() { }
    }
}
