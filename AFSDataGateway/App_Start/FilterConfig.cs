using System.Web;
using System.Web.Mvc;

namespace AFSDataGateway
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            // Same error page as before, but the exception is logged first (Services/ErrorLog.cs).
            filters.Add(new AFSDataGateway.Services.LogErrorAttribute());
        }
    }
}
