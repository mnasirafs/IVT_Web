using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace AFSDataGateway
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
        }

        // Errors outside MVC (or not handled by the MVC filter) are logged as well.
        protected void Application_Error(object sender, EventArgs e)
        {
            var ex = Server.GetLastError();
            if (ex != null) AFSDataGateway.Services.ErrorLog.Write(ex, new HttpContextWrapper(Context));
        }

        // Entra ID users carry an IVT.Entra cookie instead of a Windows identity.
        protected void Application_PostAuthenticateRequest(object sender, EventArgs e)
        {
            AFSDataGateway.Services.Entra.EntraAuth.ApplyPrincipal(Context);
            AFSDataGateway.Services.Entra.EntraAuth.EnforceSignIn(Context);
        }
    }
}
