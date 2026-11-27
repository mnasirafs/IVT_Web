using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace AFSDataGateway
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            // Entra ID redirect URI registered in the app registration.
            routes.MapRoute(name: "EntraSignIn", url: "signin-oidc", defaults: new { controller = "Entra", action = "Callback" });

            routes.MapRoute(
                name: "Index",
                url: "{controller}/{action}/{message}/{updatestatus}",
                defaults: new { message = UrlParameter.Optional, updatestatus = UrlParameter.Optional }
            );

            routes.MapRoute(
  name: "Default",
   url: "{controller}/{action}/{message}/{updatestatus}",
  defaults: new { controller = "Home", action = "Dashboard", message = UrlParameter.Optional, updatestatus = UrlParameter.Optional }
  );

            routes.MapRoute(
               name: "IVT",
                url: "{controller}/{action}/{message}/{updatestatus}",
               defaults: new { controller = "IVT", action = "Dashboard", message = UrlParameter.Optional, updatestatus = UrlParameter.Optional }
           );


            routes.MapRoute(
            name: "Integrity",
            url: "{controller}/{action}/{message}/{updatestatus}",
            defaults: new { controller = "IntegrityConsignment", action = "SearchConsignments", message = UrlParameter.Optional, updatestatus = UrlParameter.Optional }
        );

      

        }
    }
}
