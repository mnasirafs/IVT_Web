
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AFSDataGateway.Controllers
{
    public  static class Helper
    {
        public static bool IsAdmin(string roleName)
        {
            if ("admin".Equals(roleName))
            {
                return true;
            }

            return false;
        }
    }
}