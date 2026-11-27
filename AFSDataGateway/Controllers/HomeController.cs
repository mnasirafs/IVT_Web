using AFSDataGateway.Services;
using AFSDataGateway.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using System.DirectoryServices;
using System.DirectoryServices.Protocols;
using System.DirectoryServices.AccountManagement;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using static AFSDataGateway.Controllers.Helper;
using System.Data.Entity;

namespace AFSDataGateway.Controllers
{
    public class HomeController : Controller
    {

        public ActionResult SearchConsignments()
        {
            return View();
        }


        public ActionResult Profile()
        {
            return View();
        }


        public bool IsAuthenticatedUser(string srvr, string usr, string password)
        {
            bool authenticated = false;
            try
            {
                using (var entry = new DirectoryEntry(srvr, usr, password))
                {
                    object nativeObject = entry.NativeObject;
                    authenticated = true;
                }
            }
            catch (Exception ex)
            {
                string exx = ex.Message;
                return false;
            }
            return authenticated;
        }



        public ActionResult Help()
        {


            int utype = (int)Session["Utype"];

            if (utype == 2)
            {
                ViewBag.DisplayForSuperAdmin = "visible";
            }
            else if (MyGlobalVariables.loggedas == 1)
            {
                ViewBag.DisplayForSuperAdmin = "visible";
            }
            else
            {
                ViewBag.DisplayForSuperAdmin = "hidden";
            }



            return View();

        }


        public ActionResult Authenticate()
        {
            AFSXchange1Entities Obj = new AFSXchange1Entities();
            ViewBag.DisplayLinkStatus = "hidden";
            ViewBag.DisplayForAdmin = "hidden";
            ViewBag.DisplayForSuperAdmin = "hidden";
            string authinticated_user = (string)TempData["LoginName"];


            //  ViewBag.LoginList =
            //new SelectList(Obj.vIVTUser.Where(x => x.UserType == 0 && x.IsDeleted == 0 && x.IsActive == 1).ToList().
            //Select(x => new { Id = x.LoginName, DWHEditor = x.DWHEditor }), "Id", "DWHEditor ", "acamassa"); // Viewbag
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public ActionResult Authenticate(User user)
        {
            ViewBag.DisplayForAdmin = "hidden";
            ViewBag.DisplayLinkStatus = "hidden";
            ViewBag.DisplayForSuperAdmin = "hidden";
            string submittedIdentity = (user.Username ?? "").Trim();
            string loggeduser = UserIdentity.NormalizeLogin(submittedIdentity);
            IVTEntities usersEntities = new IVTEntities();
            string message = string.Empty;
            if (!string.IsNullOrEmpty(loggeduser) && !string.IsNullOrEmpty(user.Password) &&
                UserIdentity.LdapPaths.Any(path => IsAuthenticatedUser(path,
                    submittedIdentity.Contains("@") || submittedIdentity.Contains("\\") ? submittedIdentity : "AFS\\" + submittedIdentity,
                    user.Password)))
            {
                var userinfo = usersEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
                ViewBag.UserName = loggeduser;
                TempData["LoggedAs"] = null;
                Session["LoggedAs"] = null;


                if (userinfo != null && userinfo.IsActive == 1 && userinfo.IsDeleted == 0)
                {
                    //if (userinfo.IsFirstLogin == true)
                    //{
                    //    ViewBag.DisplayLinkStatus = "visible";
                    //    ViewBag.DisplayForAdmin = "hidden";
                    //    ViewBag.DisplayForSuperAdmin = "hidden";
                    //    TempData["UserName"] = userinfo.UserName;
                    //    Session["Uname"] = userinfo.UserName;
                    //    TempData["Utype"] = userinfo.UserType;
                    //    TempData["LoginName"] = user.Username.Trim();
                    //    Session["LoginName"] = user.Username.Trim();
                    //    // return RedirectToAction("ChangePassword");
                    //    return RedirectToAction("SearchConsignments", "IntegrityConsignment");

                    //}
                    //else
                    if (userinfo.UserType == 0 && userinfo.IsFirstLogin != true && userinfo.IsActive == 1)
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        FormsAuthentication.SetAuthCookie(loggeduser, user.RememberMe);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser;
                        Session["LoginName"] = loggeduser;
                        return RedirectToAction("Dashboard", "IVT", new { loginname = loggeduser });
                    }
                    else if ((userinfo.UserType == 1) && userinfo.IsFirstLogin != true)
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "visible";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        FormsAuthentication.SetAuthCookie(loggeduser, user.RememberMe);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser;
                        Session["LoginName"] = loggeduser;

                        return RedirectToAction("UserList", "Home", new { loginname = loggeduser });

                    }
                    else if ((userinfo.UserType == 2) && userinfo.IsFirstLogin != true)
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "visible";
                        ViewBag.DisplayForSuperAdmin = "visible";
                        FormsAuthentication.SetAuthCookie(loggeduser, user.RememberMe);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser;
                        Session["LoginName"] = loggeduser;
                        // ----Admin Home Credientials-------------------
                        TempData["LoginName_SuperAdmin"] = loggeduser;
                        Session["LoginName__SuperAdmin"] = loggeduser;

                        return RedirectToAction("Dashboard", "Home", new { loginname = loggeduser });

                    }
                    if (userinfo != null && userinfo.IsActive == 0)
                    {
                        ViewBag.DisplayLinkStatus = "hidden";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        TempData["Utype"] = 0;
                        message = "Your User Has Disabled!, Please Contact Business Admin";
                    }

                }
            }


            else
            {
                // var checkdisable = usersEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser && a.Password == user.Password.Trim()).FirstOrDefault();


                ViewBag.DisplayLinkStatus = "hidden";
                ViewBag.DisplayForAdmin = "hidden";
                ViewBag.DisplayForSuperAdmin = "hidden";
                TempData["Utype"] = 0;
                message = "Username is incorrect - Please Use Correct Windows Login Credentials";



            }


            ViewBag.Message = message;
            return View(user);
        }



        public ActionResult Dashboard()
        {
            try
            {
                ViewBag.DisplayForAdmin = "hidden";
                ViewBag.DisplayForSuperAdmin = "hidden";
                ViewBag.DisplayLinkStatus = "hidden";

                string loggeduser = "";
                if (TempData["LoggedAs"] == null && Session["LoggedAs"] == null)
                {

                    loggeduser = UserIdentity.CurrentLogin();

                    Session["Uname"] = loggeduser;
                }
                else if (Session["LoggedAs"] != null)
                {

                    loggeduser = (string)Session["LoggedAs"];
                }

                else if (TempData["LoggedAs"] != null)
                {

                    loggeduser = (string)TempData["LoggedAs"]; ;
                }
                else
                {
                    loggeduser = UserIdentity.CurrentLogin(); // "sallan";

                }





                IVTEntities usersEntities = new IVTEntities();
                var userinfo = usersEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
                ViewBag.UserName = loggeduser;

                string loginmessage = string.Empty;

                if (userinfo != null && userinfo.IsActive == 1)
                {
                    ViewBag.DisplayLinkStatus = "visible";

                    if (userinfo.UserType == 0) // Original AM Login
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();



                        return RedirectToAction("Dashboard", "IVT");
                    }
                }
                if ((userinfo.UserType == 0 || userinfo.UserType == 2) && userinfo.IsFirstLogin != true && userinfo.IsActive == 1)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "visible";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.UserName;
                    Session["Uname"] = userinfo.UserName;
                    Session["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim().ToUpper();
                    Session["LoginName"] = loggeduser.Trim().ToUpper();
                }
                if ((userinfo.UserType == 1))
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.UserName;
                    Session["Uname"] = userinfo.UserName;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim().ToUpper();
                    Session["LoginName"] = loggeduser.Trim().ToUpper();



                }


                IVTEntities Obj = new IVTEntities();
                //var commonMeasureData = Obj.Dash_CommonMeasure.OrderByDescending(x => x.TotCount)).Take(10).ToList();
                var top10CarrierData = Obj.Dash_Top10Carrier
               .GroupBy(x => x.carrier)
               .Select(group => new
               {
                   Carrier = group.Key,
                   TotCount = group.Sum(x => x.TotCount)
               })
               .OrderByDescending(x => x.TotCount)
               .Take(10)
               .AsEnumerable()  // Switch to LINQ to Objects
               .Select(x => new Dash_Top10Carrier
               {
                   carrier = x.Carrier,
                   TotCount = x.TotCount
               })
               .ToList();

                //-------------------------------------------------
                var CustomerCount = from data in Obj.vDash_CustomerCounts
                                    group data by new { data.FinanceGroup, data.LoginName } into groupedData
                                    select new CustomerCountResult
                                    {
                                        FinanceGroup = groupedData.Key.FinanceGroup,
                                        LoginName = groupedData.Key.LoginName,
                                        TotalCurrentWeekCount = groupedData.Sum(x => x.CurrentWeekCount) ?? 0,
                                        TotalCurrentWeekTradingCount = groupedData.Sum(x => x.CurrentWeekTradingCount) ?? 0,
                                        AverageCurrentWeekTrading = groupedData.Average(x => x.CurrentWeekTradingCount) ?? 0,
                                        TotalLastWeekCount = groupedData.Sum(x => x.LastWeekCount) ?? 0,
                                        TotalLastWeekTradingCount = groupedData.Sum(x => x.LastWeekTradingCount) ?? 0,
                                        AverageLastWeekTrading = groupedData.Average(x => x.LastWeekTradingCount) ?? 0,
                                        AverageLast13WeeksCount = groupedData.Average(x => x.Last13WeeksAvgCount) ?? 0,
                                        TotalLast13WeeksTradingCount = groupedData.Sum(x => x.Last13WeeksTradingCount) ?? 0,
                                        AverageLast13WeeksTrading = groupedData.Average(x => x.Last13WeeksTradingCountAVG) ?? 0,
                                    };


                // To get the final result as a list
                var CustomerCountList = CustomerCount
                    .OrderBy(x => x.TotalCurrentWeekCount)
                    .ToList();

                // Add a row for grand total
                var grandTotal = new CustomerCountResult
                {
                    FinanceGroup = "Grand Total",
                    // Assuming these properties are numeric, set them to the sum of the entire dataset
                    TotalCurrentWeekCount = CustomerCount.Sum(x => x.TotalCurrentWeekCount),
                    TotalCurrentWeekTradingCount = CustomerCount.Sum(x => x.TotalCurrentWeekTradingCount),
                    TotalLastWeekCount = CustomerCount.Sum(x => x.TotalLastWeekCount),
                    TotalLastWeekTradingCount = CustomerCount.Sum(x => x.TotalLastWeekTradingCount),
                    TotalLast13WeeksTradingCount = CustomerCount.Sum(x => x.TotalLast13WeeksTradingCount),
                    AverageLast13WeeksCount = CustomerCount.Sum(x => x.AverageLast13WeeksCount),
                    AverageLast13WeeksTrading = CustomerCount.Sum(x => x.AverageLast13WeeksTrading),
                };

                CustomerCountList.Add(grandTotal);



                var AM_Count = from groupedData in Obj.vDash_AM_Counts
                               select new AMCountResult
                               {
                                   AccountManager = groupedData.AccountManager,
                                   TotalExceptions = groupedData.TotalExceptions ?? 0,
                                   TotalCurrentWeekTradingCount = groupedData.TotalAllConnotes ?? 0,  // Removed Sum function
                                   TotalLastWeekCount = groupedData.TotalLastWeekCount ?? 0,
                                   TotalLastWeekTradingCount = groupedData.TotalLastWeekTradingCount ?? 0,
                                   AverageLast13WeeksAvgCount = groupedData.AverageLast13WeeksAvgCount ?? 0,
                                   AverageLast13WeeksTradingCountAVG = groupedData.AverageLast13WeeksTradingCountAVG ?? 0,
                                   TotalLast13WeeksTradingCount = groupedData.TotalLast13WeeksTradingCount ?? 0,
                                   TotalCustomer = groupedData.TotalCustomer ?? 0,
                                   TotalCarrier = groupedData.TotalCarrier ?? 0,
                                   SellTotalNett = groupedData.SellTotalNett ?? 0,
                                   BuyTotalNett = groupedData.BuyTotalNett ?? 0,
                                   TotalWeight = groupedData.TotalWeight ?? 0,
                                   TotalCubic = groupedData.TotalCubic ?? 0,
                                   TotalQuantity = groupedData.TotalQuantity ?? 0,
                                   HeldCount = groupedData.HeldCount ?? 0,
                                   TotalAncillaries = groupedData.TotalAncillaries ?? 0,
                                   ExceptionRate = groupedData.ExceptionRate ?? 0,
                                   AncillariesRate = groupedData.AncillariesRate ?? 0,
                               };

                // To get the final result as a list
                var AM_CountList = AM_Count
                                   .OrderBy(x => x.AccountManager)
                                   .ToList();

                // Add a row for grand total
                var grandTotalAM = new AMCountResult
                {
                    AccountManager = "Grand Total",
                    // Assuming these properties are numeric, set them to the sum of the entire dataset
                    TotalExceptions = AM_CountList.Sum(x => x.TotalExceptions),
                    TotalCurrentWeekTradingCount = AM_CountList.Sum(x => x.TotalCurrentWeekTradingCount),
                    TotalLastWeekCount = AM_CountList.Sum(x => x.TotalLastWeekCount),
                    TotalLastWeekTradingCount = AM_CountList.Sum(x => x.TotalLastWeekTradingCount),
                    AverageLast13WeeksAvgCount = AM_CountList.Average(x => x.AverageLast13WeeksAvgCount),
                    AverageLast13WeeksTradingCountAVG = AM_CountList.Average(x => x.AverageLast13WeeksTradingCountAVG),
                    TotalLast13WeeksTradingCount = AM_CountList.Sum(x => x.TotalLast13WeeksTradingCount),
                    TotalCustomer = AM_CountList.Sum(x => x.TotalCustomer),
                    TotalCarrier = AM_CountList.Sum(x => x.TotalCarrier),
                    SellTotalNett = AM_CountList.Sum(x => x.SellTotalNett),
                    BuyTotalNett = AM_CountList.Sum(x => x.BuyTotalNett),
                    TotalWeight = AM_CountList.Sum(x => x.TotalWeight),
                    TotalCubic = AM_CountList.Sum(x => x.TotalCubic),
                    TotalQuantity = AM_CountList.Sum(x => x.TotalQuantity),
                    HeldCount = AM_CountList.Sum(x => x.HeldCount),
                    TotalAncillaries = AM_CountList.Sum(x => x.TotalAncillaries),
                    ExceptionRate = 0,
                    AncillariesRate = 0,
                };

                AM_CountList.Add(grandTotalAM);


                //------Exception Summary


                var exception_summary_list = Obj.vDash_ExceptionSummary_Admin
           .Select(x => new DashExceptionSummary
           {
               ErrDesc = x.Exception ?? "",
               RuleCategory = x.ErrPriority ?? "",
               RuleCode = x.RuleCode ,
               ExceptionType = x.ExceptionType ?? "",
               ThresholdValue = x.Threshold_Value, // Assuming ThresholdValue should be numeric
               CurrentWeekCount = x.CurrentWeekExceptionCountTotal ?? 0,
               LastWeekCount = x.LastWeekExceptionCount ?? 0,
               Last13WeeksAvgCount = x.Last13WeeksExceptionCount ?? 0
               // Include other properties as needed
           })
           .OrderBy(x => x.RuleCode)
           .ToList();

                var grandTotal_exceptionsummary = new DashExceptionSummary
                {
                    ErrDesc = "",
                    RuleCategory = "Grand Total",
                    RuleCode = 0,
                    ExceptionType = "",
                    ThresholdValue = "", // Assuming ThresholdValue is numeric
                    CurrentWeekCount = exception_summary_list.Sum(x => x.CurrentWeekCount),
                    LastWeekCount = exception_summary_list.Sum(x => x.LastWeekCount),
                    Last13WeeksAvgCount = exception_summary_list.Sum(x => x.Last13WeeksAvgCount)
                    // Include other properties as needed
                };

                exception_summary_list.Add(grandTotal_exceptionsummary);


                ///--------------------------

                var top10CustomerData = Obj.Dash_Top10Customer
         .GroupBy(x => x.FinanceGroup)
         .Select(group => new
         {
             FinanceGroup = group.Key,
             TotCount = group.Sum(x => x.TotCount)
         })
         .OrderByDescending(x => x.TotCount)
         .Take(10)
         .AsEnumerable()  // Switch to LINQ to Objects
         .Select(x => new Dash_Top10Customer
         {
             FinanceGroup = x.FinanceGroup,
             TotCount = x.TotCount
         })
         .ToList();

                var top10ExceptionData = Obj.Dash_Top10Exception
                    .GroupBy(x => x.ErrDesc)
                    .Select(group => new
                    {
                        ErrDesc = group.Key,
                        TotCount = group.Sum(x => x.TotCount)
                    })
                    .OrderByDescending(x => x.TotCount)
                    .Take(10)
                    .AsEnumerable()  // Switch to LINQ to Objects
                    .Select(x => new Dash_Top10Exception
                    {
                        ErrDesc = x.ErrDesc,
                        TotCount = x.TotCount
                    })
                    .ToList();



                var filteredData = Obj.vDash_TotalMeasure_Admin
            .GroupBy(x => 1) // Group by a constant to perform aggregate functions
            .Select(g => new
            {
                TotalExceptions = g.Sum(x => (int?)x.TotalExceptions) ?? 0,
                TotalCustomer = g.Sum(x => (int?)x.TotalCustomer) ?? 0,
                TotalCarrier = g.Sum(x => (int?)x.TotalCarrier) ?? 0,
                total_nett = g.Sum(x => (decimal?)x.total_nett) ?? 0,
                buy_total_nett = g.Sum(x => (decimal?)x.buy_total_nett) ?? 0,
                TotalWeight = g.Sum(x => (decimal?)x.TotalWeight) ?? 0,
                Cubic = g.Sum(x => (decimal?)x.Cubic) ?? 0,
                HeldCount = g.Sum(x => (int?)x.HeldCount) ?? 0,
                TotalAllConnotes = g.Sum(x => (int?)x.TotalAllConnotes) ?? 0,
                TotalManualEntry = g.Sum(x => (int?)x.TotalManualEntry) ?? 0,
                TotalNotInMoveit = g.Sum(x => (int?)x.TotalNotInMoveit) ?? 0,
                // Ensure the divisor is not 0
                ExceptionRate = g.Any() && g.Sum(x => x.TotalAllConnotes) > 0 ? g.Sum(x => (int?)x.TotalExceptions) * 100.0 / g.Sum(x => x.TotalAllConnotes) : 0,
                TotalAncillaries = g.Sum(x => (int?)x.TotalAncillaries) ?? 0,
                AncillariesRate = g.Any() && g.Sum(x => x.TotalAllConnotes) > 0 ? g.Sum(x => (int?)x.TotalAncillaries) * 100.0 / g.Sum(x => x.TotalAllConnotes) : 0,
                ManualEntryRate = g.Any() && g.Sum(x => x.TotalManualEntry) > 0 ? g.Sum(x => (int?)x.TotalManualEntry) * 100.0 / g.Sum(x => x.TotalAllConnotes) : 0,
                NotInMoveitRate = g.Any() && g.Sum(x => x.TotalNotInMoveit) > 0 ? g.Sum(x => (int?)x.TotalNotInMoveit) * 100.0 / g.Sum(x => x.TotalAllConnotes) : 0,

                // Repeat the pattern for other properties
                L13WExceptionRate = g.Any() && g.Sum(x => x.Last13WeeksTradingCount) > 0 ? g.Sum(x => (int?)x.Last13WeeksCount) * 100.0 / g.Sum(x => x.Last13WeeksTradingCount) : 0,
                Last13WeeksCount = g.Sum(x => (int?)x.Last13WeeksCount) ?? 0,
                Last13WeeksTradingCount = g.Sum(x => (int?)x.Last13WeeksTradingCount) ?? 0,
                Last13WeeksTotalAncillaries = g.Sum(x => (int?)x.Last13WeeksTotalAncillaries) ?? 0,
                L13WAncillariesRate = g.Any() && g.Sum(x => x.Last13WeeksTradingCount) > 0 ? g.Sum(x => (int?)x.Last13WeeksTotalAncillaries) * 100.0 / g.Sum(x => x.Last13WeeksTradingCount) : 0,
                Last13WeeksManualEntryCount = g.Sum(x => (int?)x.Last13WeekTotalManualEntry) ?? 0,
                L13WManualEntryRate = g.Any() && g.Sum(x => x.Last13WeekTotalManualEntry) > 0 ? g.Sum(x => (int?)x.Last13WeekTotalManualEntry) * 100.0 / g.Sum(x => x.Last13WeeksTradingCount) : 0,
                Last13WeeksNotInMoveitCount = g.Sum(x => (int?)x.Last13WeekTotalNotInMoveit) ?? 0,
                L13WNotInMoveitRate = g.Any() && g.Sum(x => x.Last13WeekTotalNotInMoveit) > 0 ? g.Sum(x => (int?)x.Last13WeekTotalNotInMoveit) * 100.0 / g.Sum(x => x.Last13WeeksTradingCount) : 0,

                LWExceptionRate = g.Any() && g.Sum(x => x.LastWeekTradingCount) > 0 ? g.Sum(x => (int?)x.LastWeekCount) * 100.0 / g.Sum(x => x.LastWeekTradingCount) : 0,
                LastWeeksCount = g.Sum(x => (int?)x.LastWeekCount) ?? 0,
                LastWeeksTradingCount = g.Sum(x => (int?)x.LastWeekTradingCount) ?? 0,
                LastWeeksTotalAncillaries = g.Sum(x => (int?)x.LastWeekTotalAncillaries) ?? 0,
                LWAncillariesRate = g.Any() && g.Sum(x => x.LastWeekTradingCount) > 0 ? g.Sum(x => (int?)x.LastWeekTotalAncillaries) * 100.0 / g.Sum(x => x.LastWeekTradingCount) : 0,
                LastWeekWeeksManualEntryCount = g.Sum(x => (int?)x.LastWeekTotalManualEntry) ?? 0,
                LastWeekManualEntryRate = g.Any() && g.Sum(x => x.LastWeekTotalManualEntry) > 0 ? g.Sum(x => (int?)x.LastWeekTotalManualEntry) * 100.0 / g.Sum(x => x.LastWeekTradingCount) : 0,
                LastWeeksNotInMoveitCount = g.Sum(x => (int?)x.LastWeekTotalNotInMoveit) ?? 0,
                LastWeekNotInMoveitRate = g.Any() && g.Sum(x => x.LastWeekTotalNotInMoveit) > 0 ? g.Sum(x => (int?)x.LastWeekTotalNotInMoveit) * 100.0 / g.Sum(x => x.LastWeekTradingCount) : 0,
            })
            .ToList();



                var totalMeasuresData = new List<Custom_Dash_TotalMeasures_Admin>
{
    new Custom_Dash_TotalMeasures_Admin
    {
        TotalExceptions = filteredData?.Sum(x => x.TotalExceptions) ?? 0,
        TotalCustomer = filteredData?.Sum(x => x.TotalCustomer) ?? 0,
        TotalCarrier = filteredData?.Sum(x => x.TotalCarrier) ?? 0,
        total_nett = filteredData?.Sum(x => x.total_nett) ?? 0,
        buy_total_nett = filteredData?.Sum(x => x.buy_total_nett) ?? 0,

        TotalWeight = filteredData?.Sum(x => x.TotalWeight) ?? 0,
        Cubic = filteredData?.Sum(x => x.Cubic) ?? 0,
        HeldCount = filteredData?.Sum(x => x.HeldCount) ?? 0,
        TotalManualEntry = filteredData?.Sum(x => x.TotalManualEntry) ?? 0,
        TotalNotInMoveit = filteredData?.Sum(x => x.TotalNotInMoveit) ?? 0,
        ExceptionRate = Convert.ToDecimal(filteredData?.Sum(x => x.ExceptionRate)),
        AncillaryRate = Convert.ToDecimal(filteredData?.Sum(x => x.AncillariesRate)),
         ManualEntryRate = Convert.ToDecimal(filteredData?.Sum(x => x.ManualEntryRate)),
         NotInMoveitRate = Convert.ToDecimal(filteredData?.Sum(x => x.NotInMoveitRate)),
      TotalAllConnotes = filteredData?.Sum(x => x.TotalAllConnotes) ?? 0,
        TotalAncillaries = filteredData?.Sum(x => x.TotalAncillaries) ?? 0,
        Last13WeeksCount = filteredData?.Sum(x => x.Last13WeeksCount) ?? 0,
        Last13WeeksTradingCount = filteredData?.Sum(x => x.Last13WeeksTradingCount) ?? 0,
        Last13WeeksTotalAncillaries = filteredData?.Sum(x => x.Last13WeeksTotalAncillaries) ?? 0,
          L13WManualEntryRate = Convert.ToDecimal(filteredData?.Sum(x => x.L13WManualEntryRate)),
          L13WNotInMoveitRate = Convert.ToDecimal(filteredData?.Sum(x => x.L13WNotInMoveitRate)),
          L13WManualEntryCount = filteredData?.Sum(x => x.Last13WeeksManualEntryCount) ?? 0,
          L13WNotInMoveitCount = filteredData?.Sum(x => x.Last13WeeksNotInMoveitCount) ?? 0,


        L13WAncillariesRate = Convert.ToDecimal(filteredData?.Sum(x => x.L13WAncillariesRate)),
        L13WExceptionRate=Convert.ToDecimal(filteredData?.Sum(x => x.L13WExceptionRate)),

        LastWeekCount = filteredData?.Sum(x => x.LastWeeksCount) ?? 0,
        LastWeekTradingCount = filteredData?.Sum(x => x.LastWeeksTradingCount) ?? 0,
        LastWeekManualEntryCount = filteredData?.Sum(x => x.LastWeekWeeksManualEntryCount) ?? 0,
        LastWeekNotInMoveitCount = filteredData?.Sum(x => x.LastWeeksNotInMoveitCount) ?? 0,
         LastWeekManualEntryRate = Convert.ToDecimal(filteredData?.Sum(x => x.LastWeekManualEntryRate)),
          LastWeekNotInMoveitRate = Convert.ToDecimal(filteredData?.Sum(x => x.LastWeekNotInMoveitRate)),

        LastWeekTotalAncillaries = filteredData.Sum(x => x.LastWeeksTotalAncillaries),
        LWAncillariesRate = Convert.ToDecimal(filteredData?.Sum(x => x.LWAncillariesRate)),
        LWExceptionRate=Convert.ToDecimal(filteredData?.Sum(x => x.LWExceptionRate)),
    }
};

                var LastIVTRunData = Obj.Dash_LastIVTRun
           .GroupBy(x => x.CurrentWeek)
           .Select(group => new
           {
               CurrentWeek = group.Key,
               TotCount = group.Sum(x => x.TotCount)
           })
            .OrderByDescending(x => x.CurrentWeek)
           .Take(5)
           .AsEnumerable() // Switch to LINQ to Objects
           .Select(item => new Dash_LastIVTRun
           {
               CurrentWeek = item.CurrentWeek,
               TotCount = item.TotCount
           })
           .ToList();




                var currentWeek = Obj.FileLoadControl.FirstOrDefault()?.InvoiceWeek.ToString("dd/MM/yyyy");


                // Assuming DashExceptionSummary and vDash_Exception_Summary have similar properties
                //var exception_summary_list = Obj.vDash_Exception_Summary
                //    .Select(x => new DashExceptionSummary
                //    {
                //        ErrDesc = x.ErrDesc,
                //        CurrentWeekCount = x.CurrentWeekCount,
                //        LastWeekCount = x.LastWeekCount,
                //        Last13WeeksAvgCount = x.Last13WeeksAvgCount
                //        // Include other properties as needed
                //    })
                //    .ToList();


                var viewModel = new DashboardViewModel
                {
                    // CommonMeasureData = commonMeasureData,
                    Top10CarrierData = top10CarrierData,
                    Top10CustomerData = top10CustomerData,
                    Top10ExceptionData = top10ExceptionData,
                    TotalMeasuresData_Admin = totalMeasuresData,
                    LastIVTRunData = LastIVTRunData,
                    CurrentWeek = currentWeek,
                    Exception_summary_list = exception_summary_list,
                    Customer_Count_list = CustomerCountList,

                    AM_Count_list = AM_CountList
                };


                return View(viewModel);

            }
            catch (Exception ex)
            {
                try
                {
                    string logPath = Server.MapPath("~/App_Data/home-dashboard-error.log");
                    System.IO.File.AppendAllText(
                        logPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                        + Environment.NewLine
                        + ex
                        + Environment.NewLine
                        + new string('-', 100)
                        + Environment.NewLine);
                }
                catch
                {
                    System.Diagnostics.Trace.TraceError("Home dashboard error: {0}", ex);
                }
                return RedirectToAction("TimeOut", "Home");
            }


        }


        public ActionResult ExecReport()
        {
            try
            {
                ViewBag.DisplayForAdmin = "hidden";
                ViewBag.DisplayForSuperAdmin = "hidden";
                ViewBag.DisplayLinkStatus = "hidden";

                string loggeduser = "";
                if (TempData["LoggedAs"] == null && Session["LoggedAs"] == null)
                {

                    loggeduser = UserIdentity.CurrentLogin();

                    Session["Uname"] = loggeduser;
                }
                else if (Session["LoggedAs"] != null)
                {

                    loggeduser = (string)Session["LoggedAs"];
                }

                else if (TempData["LoggedAs"] != null)
                {

                    loggeduser = (string)TempData["LoggedAs"]; ;
                }
                else
                {
                    loggeduser = UserIdentity.CurrentLogin(); // "sallan";

                }





                IVTEntities usersEntities = new IVTEntities();
                var userinfo = usersEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
                ViewBag.UserName = loggeduser;

                string loginmessage = string.Empty;

                if (userinfo != null && userinfo.IsActive == 1)
                {
                    ViewBag.DisplayLinkStatus = "visible";

                    if (userinfo.UserType == 0) // Original AM Login
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();



                        return RedirectToAction("Dashboard", "IVT");
                    }
                }
                if ((userinfo.UserType == 0 || userinfo.UserType == 2) && userinfo.IsFirstLogin != true && userinfo.IsActive == 1 && MyGlobalVariables.loggedas == 1)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "visible";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.UserName;
                    Session["Uname"] = userinfo.UserName;
                    Session["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim().ToUpper();
                    Session["LoginName"] = loggeduser.Trim().ToUpper();
                }
                if ((userinfo.UserType == 1))
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.UserName;
                    Session["Uname"] = userinfo.UserName;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim().ToUpper();
                    Session["LoginName"] = loggeduser.Trim().ToUpper();



                }


                IVTEntities Obj = new IVTEntities();
                //var commonMeasureData = Obj.Dash_CommonMeasure.OrderByDescending(x => x.TotCount)).Take(10).ToList();
                var top10CarrierData = Obj.Dash_Top10Carrier
               .GroupBy(x => x.carrier)
               .Select(group => new
               {
                   Carrier = group.Key,
                   TotCount = group.Sum(x => x.TotCount)
               })
               .OrderByDescending(x => x.TotCount)
               .Take(10)
               .AsEnumerable()  // Switch to LINQ to Objects
               .Select(x => new Dash_Top10Carrier
               {
                   carrier = x.Carrier,
                   TotCount = x.TotCount
               })
               .ToList();

                //-------------------------------------------------
                var CustomerCount = from data in Obj.vDash_CustomerCounts
                                    group data by new { data.FinanceGroup, data.LoginName } into groupedData
                                    select new CustomerCountResult
                                    {
                                        FinanceGroup = groupedData.Key.FinanceGroup,
                                        LoginName = groupedData.Key.LoginName,
                                        TotalCurrentWeekCount = groupedData.Sum(x => x.CurrentWeekCount) ?? 0,
                                        TotalCurrentWeekTradingCount = groupedData.Sum(x => x.CurrentWeekTradingCount) ?? 0,
                                        AverageCurrentWeekTrading = groupedData.Average(x => x.CurrentWeekTradingCount) ?? 0,
                                        TotalLastWeekCount = groupedData.Sum(x => x.LastWeekCount) ?? 0,
                                        TotalLastWeekTradingCount = groupedData.Sum(x => x.LastWeekTradingCount) ?? 0,
                                        AverageLastWeekTrading = groupedData.Average(x => x.LastWeekTradingCount) ?? 0,
                                        AverageLast13WeeksCount = groupedData.Average(x => x.Last13WeeksAvgCount) ?? 0,
                                        TotalLast13WeeksTradingCount = groupedData.Sum(x => x.Last13WeeksTradingCount) ?? 0,
                                        AverageLast13WeeksTrading = groupedData.Average(x => x.Last13WeeksTradingCountAVG) ?? 0,
                                    };


                // To get the final result as a list
                var CustomerCountList = CustomerCount
                    .OrderByDescending(x => x.TotalCurrentWeekCount)
                    .ToList();

                // Add a row for grand total
                var grandTotal = new CustomerCountResult
                {
                    FinanceGroup = "Grand Total",
                    // Assuming these properties are numeric, set them to the sum of the entire dataset
                    TotalCurrentWeekCount = CustomerCount.Sum(x => x.TotalCurrentWeekCount),
                    TotalCurrentWeekTradingCount = CustomerCount.Sum(x => x.TotalCurrentWeekTradingCount),
                    TotalLastWeekCount = CustomerCount.Sum(x => x.TotalLastWeekCount),
                    TotalLastWeekTradingCount = CustomerCount.Sum(x => x.TotalLastWeekTradingCount),
                    TotalLast13WeeksTradingCount = CustomerCount.Sum(x => x.TotalLast13WeeksTradingCount),
                    AverageLast13WeeksCount = CustomerCount.Sum(x => x.AverageLast13WeeksCount),
                    AverageLast13WeeksTrading = CustomerCount.Sum(x => x.AverageLast13WeeksTrading),
                };

                CustomerCountList.Add(grandTotal);



                var AM_Count = from groupedData in Obj.vDash_AM_Counts
                               select new AMCountResult
                               {
                                   AccountManager = groupedData.AccountManager,
                                   TotalExceptions = groupedData.TotalExceptions ?? 0,
                                   TotalCurrentWeekTradingCount = groupedData.TotalAllConnotes ?? 0,  // Removed Sum function
                                   TotalLastWeekCount = groupedData.TotalLastWeekCount ?? 0,
                                   TotalLastWeekTradingCount = groupedData.TotalLastWeekTradingCount ?? 0,
                                   AverageLast13WeeksAvgCount = groupedData.AverageLast13WeeksAvgCount ?? 0,
                                   AverageLast13WeeksTradingCountAVG = groupedData.AverageLast13WeeksTradingCountAVG ?? 0,
                                   TotalLast13WeeksTradingCount = groupedData.TotalLast13WeeksTradingCount ?? 0,
                                   TotalCustomer = groupedData.TotalCustomer ?? 0,
                                   TotalCarrier = groupedData.TotalCarrier ?? 0,
                                   SellTotalNett = groupedData.SellTotalNett ?? 0,
                                   BuyTotalNett = groupedData.BuyTotalNett ?? 0,
                                   TotalWeight = groupedData.TotalWeight ?? 0,
                                   TotalCubic = groupedData.TotalCubic ?? 0,
                                   TotalQuantity = groupedData.TotalQuantity ?? 0,
                                   HeldCount = groupedData.HeldCount ?? 0,
                                   TotalAncillaries = groupedData.TotalAncillaries ?? 0,
                                   ExceptionRate = groupedData.ExceptionRate ?? 0,
                                   AncillariesRate = groupedData.AncillariesRate ?? 0,
                               };

                // To get the final result as a list
                var AM_CountList = AM_Count
                                   .OrderByDescending(x => x.TotalExceptions)
                                   .ToList();

                // Add a row for grand total
                var grandTotalAM = new AMCountResult
                {
                    AccountManager = "Grand Total",
                    // Assuming these properties are numeric, set them to the sum of the entire dataset
                    TotalExceptions = AM_CountList.Sum(x => x.TotalExceptions),
                    TotalCurrentWeekTradingCount = AM_CountList.Sum(x => x.TotalCurrentWeekTradingCount),
                    TotalLastWeekCount = AM_CountList.Sum(x => x.TotalLastWeekCount),
                    TotalLastWeekTradingCount = AM_CountList.Sum(x => x.TotalLastWeekTradingCount),
                    AverageLast13WeeksAvgCount = AM_CountList.Average(x => x.AverageLast13WeeksAvgCount),
                    AverageLast13WeeksTradingCountAVG = AM_CountList.Average(x => x.AverageLast13WeeksTradingCountAVG),
                    TotalLast13WeeksTradingCount = AM_CountList.Sum(x => x.TotalLast13WeeksTradingCount),
                    TotalCustomer = AM_CountList.Sum(x => x.TotalCustomer),
                    TotalCarrier = AM_CountList.Sum(x => x.TotalCarrier),
                    SellTotalNett = AM_CountList.Sum(x => x.SellTotalNett),
                    BuyTotalNett = AM_CountList.Sum(x => x.BuyTotalNett),
                    TotalWeight = AM_CountList.Sum(x => x.TotalWeight),
                    TotalCubic = AM_CountList.Sum(x => x.TotalCubic),
                    TotalQuantity = AM_CountList.Sum(x => x.TotalQuantity),
                    HeldCount = AM_CountList.Sum(x => x.HeldCount),
                    TotalAncillaries = AM_CountList.Sum(x => x.TotalAncillaries),
                    ExceptionRate = 0,
                    AncillariesRate = 0,
                };

                AM_CountList.Add(grandTotalAM);


                //------Exception Summary


                var exception_summary_list = Obj.vDash_ExceptionSummary_Admin
           .Select(x => new DashExceptionSummary
           {
               ErrDesc = x.Exception ?? "",
               RuleCategory = x.ErrPriority ?? "",
               RuleCode = x.RuleCode,
               ExceptionType = x.ExceptionType ?? "",
               ThresholdValue = x.Threshold_Value, // Assuming ThresholdValue should be numeric
        CurrentWeekCount = x.CurrentWeekExceptionCountTotal ?? 0,
               LastWeekCount = x.LastWeekExceptionCount ?? 0,
               Last13WeeksAvgCount = x.Last13WeeksExceptionCount ?? 0
        // Include other properties as needed
    })
           .OrderBy(x => x.RuleCode)
           .ToList();

                var grandTotal_exceptionsummary = new DashExceptionSummary
                {
                    ErrDesc = "",
                    RuleCategory = "Grand Total",
                    RuleCode = 0,
                    ExceptionType = "",
                    ThresholdValue = "", // Assuming ThresholdValue is numeric
                    CurrentWeekCount = exception_summary_list.Sum(x => x.CurrentWeekCount),
                    LastWeekCount = exception_summary_list.Sum(x => x.LastWeekCount),
                    Last13WeeksAvgCount = exception_summary_list.Sum(x => x.Last13WeeksAvgCount)
                    // Include other properties as needed
                };

                exception_summary_list.Add(grandTotal_exceptionsummary);



                ///--------------------------

                var top10CustomerData = Obj.Dash_Top10Customer
         .GroupBy(x => x.FinanceGroup)
         .Select(group => new
         {
             FinanceGroup = group.Key,
             TotCount = group.Sum(x => x.TotCount)
         })
         .OrderByDescending(x => x.TotCount)
         .Take(10)
         .AsEnumerable()  // Switch to LINQ to Objects
         .Select(x => new Dash_Top10Customer
         {
             FinanceGroup = x.FinanceGroup,
             TotCount = x.TotCount
         })
         .ToList();

                var top10ExceptionData = Obj.Dash_Top10Exception
                    .GroupBy(x => x.ErrDesc)
                    .Select(group => new
                    {
                        ErrDesc = group.Key,
                        TotCount = group.Sum(x => x.TotCount)
                    })
                    .OrderByDescending(x => x.TotCount)
                    .Take(10)
                    .AsEnumerable()  // Switch to LINQ to Objects
                    .Select(x => new Dash_Top10Exception
                    {
                        ErrDesc = x.ErrDesc,
                        TotCount = x.TotCount
                    })
                    .ToList();



                var filteredData = Obj.vDash_TotalMeasure_Admin
            .GroupBy(x => 1) // Group by a constant to perform aggregate functions
            .Select(g => new
            {
                TotalExceptions = g.Sum(x => (int?)x.TotalExceptions) ?? 0,
                TotalCustomer = g.Sum(x => (int?)x.TotalCustomer) ?? 0,
                TotalCarrier = g.Sum(x => (int?)x.TotalCarrier) ?? 0,
                total_nett = g.Sum(x => (decimal?)x.total_nett) ?? 0,
                buy_total_nett = g.Sum(x => (decimal?)x.buy_total_nett) ?? 0,
                TotalWeight = g.Sum(x => (decimal?)x.TotalWeight) ?? 0,
                Cubic = g.Sum(x => (decimal?)x.Cubic) ?? 0,
                HeldCount = g.Sum(x => (int?)x.HeldCount) ?? 0,
                TotalAllConnotes = g.Sum(x => (int?)x.TotalAllConnotes) ?? 0,
                TotalManualEntry = g.Sum(x => (int?)x.TotalManualEntry) ?? 0,
                TotalNotInMoveit = g.Sum(x => (int?)x.TotalNotInMoveit) ?? 0,
                // Ensure the divisor is not 0
                ExceptionRate = g.Any() && g.Sum(x => x.TotalAllConnotes) > 0 ? g.Sum(x => (int?)x.TotalExceptions) * 100.0 / g.Sum(x => x.TotalAllConnotes) : 0,
                TotalAncillaries = g.Sum(x => (int?)x.TotalAncillaries) ?? 0,
                AncillariesRate = g.Any() && g.Sum(x => x.TotalAllConnotes) > 0 ? g.Sum(x => (int?)x.TotalAncillaries) * 100.0 / g.Sum(x => x.TotalAllConnotes) : 0,
                ManualEntryRate = g.Any() && g.Sum(x => x.TotalManualEntry) > 0 ? g.Sum(x => (int?)x.TotalManualEntry) * 100.0 / g.Sum(x => x.TotalAllConnotes) : 0,
                NotInMoveitRate = g.Any() && g.Sum(x => x.TotalNotInMoveit) > 0 ? g.Sum(x => (int?)x.TotalNotInMoveit) * 100.0 / g.Sum(x => x.TotalAllConnotes) : 0,

                // Repeat the pattern for other properties
                L13WExceptionRate = g.Any() && g.Sum(x => x.Last13WeeksTradingCount) > 0 ? g.Sum(x => (int?)x.Last13WeeksCount) * 100.0 / g.Sum(x => x.Last13WeeksTradingCount) : 0,
                Last13WeeksCount = g.Sum(x => (int?)x.Last13WeeksCount) ?? 0,
                Last13WeeksTradingCount = g.Sum(x => (int?)x.Last13WeeksTradingCount) ?? 0,
                Last13WeeksTotalAncillaries = g.Sum(x => (int?)x.Last13WeeksTotalAncillaries) ?? 0,
                L13WAncillariesRate = g.Any() && g.Sum(x => x.Last13WeeksTradingCount) > 0 ? g.Sum(x => (int?)x.Last13WeeksTotalAncillaries) * 100.0 / g.Sum(x => x.Last13WeeksTradingCount) : 0,
                Last13WeeksManualEntryCount = g.Sum(x => (int?)x.Last13WeekTotalManualEntry) ?? 0,
                L13WManualEntryRate = g.Any() && g.Sum(x => x.Last13WeekTotalManualEntry) > 0 ? g.Sum(x => (int?)x.Last13WeekTotalManualEntry) * 100.0 / g.Sum(x => x.Last13WeeksTradingCount) : 0,
                Last13WeeksNotInMoveitCount = g.Sum(x => (int?)x.Last13WeekTotalNotInMoveit) ?? 0,
                L13WNotInMoveitRate = g.Any() && g.Sum(x => x.Last13WeekTotalNotInMoveit) > 0 ? g.Sum(x => (int?)x.Last13WeekTotalNotInMoveit) * 100.0 / g.Sum(x => x.Last13WeeksTradingCount) : 0,

                LWExceptionRate = g.Any() && g.Sum(x => x.LastWeekTradingCount) > 0 ? g.Sum(x => (int?)x.LastWeekCount) * 100.0 / g.Sum(x => x.LastWeekTradingCount) : 0,
                LastWeeksCount = g.Sum(x => (int?)x.LastWeekCount) ?? 0,
                LastWeeksTradingCount = g.Sum(x => (int?)x.LastWeekTradingCount) ?? 0,
                LastWeeksTotalAncillaries = g.Sum(x => (int?)x.LastWeekTotalAncillaries) ?? 0,
                LWAncillariesRate = g.Any() && g.Sum(x => x.LastWeekTradingCount) > 0 ? g.Sum(x => (int?)x.LastWeekTotalAncillaries) * 100.0 / g.Sum(x => x.LastWeekTradingCount) : 0,
                LastWeekWeeksManualEntryCount = g.Sum(x => (int?)x.LastWeekTotalManualEntry) ?? 0,
                LastWeekManualEntryRate = g.Any() && g.Sum(x => x.LastWeekTotalManualEntry) > 0 ? g.Sum(x => (int?)x.LastWeekTotalManualEntry) * 100.0 / g.Sum(x => x.LastWeekTradingCount) : 0,
                LastWeeksNotInMoveitCount = g.Sum(x => (int?)x.LastWeekTotalNotInMoveit) ?? 0,
                LastWeekNotInMoveitRate = g.Any() && g.Sum(x => x.LastWeekTotalNotInMoveit) > 0 ? g.Sum(x => (int?)x.LastWeekTotalNotInMoveit) * 100.0 / g.Sum(x => x.LastWeekTradingCount) : 0,
            })
            .ToList();



                var totalMeasuresData = new List<Custom_Dash_TotalMeasures_Admin>
{
    new Custom_Dash_TotalMeasures_Admin
    {
        TotalExceptions = filteredData?.Sum(x => x.TotalExceptions) ?? 0,
        TotalCustomer = filteredData?.Sum(x => x.TotalCustomer) ?? 0,
        TotalCarrier = filteredData?.Sum(x => x.TotalCarrier) ?? 0,
        total_nett = filteredData?.Sum(x => x.total_nett) ?? 0,
        buy_total_nett = filteredData?.Sum(x => x.buy_total_nett) ?? 0,

        TotalWeight = filteredData?.Sum(x => x.TotalWeight) ?? 0,
        Cubic = filteredData?.Sum(x => x.Cubic) ?? 0,
        HeldCount = filteredData?.Sum(x => x.HeldCount) ?? 0,
        TotalManualEntry = filteredData?.Sum(x => x.TotalManualEntry) ?? 0,
        TotalNotInMoveit = filteredData?.Sum(x => x.TotalNotInMoveit) ?? 0,
        ExceptionRate = Convert.ToDecimal(filteredData?.Sum(x => x.ExceptionRate)),
        AncillaryRate = Convert.ToDecimal(filteredData?.Sum(x => x.AncillariesRate)),
         ManualEntryRate = Convert.ToDecimal(filteredData?.Sum(x => x.ManualEntryRate)),
         NotInMoveitRate = Convert.ToDecimal(filteredData?.Sum(x => x.NotInMoveitRate)),
      TotalAllConnotes = filteredData?.Sum(x => x.TotalAllConnotes) ?? 0,
        TotalAncillaries = filteredData?.Sum(x => x.TotalAncillaries) ?? 0,
        Last13WeeksCount = filteredData?.Sum(x => x.Last13WeeksCount) ?? 0,
        Last13WeeksTradingCount = filteredData?.Sum(x => x.Last13WeeksTradingCount) ?? 0,
        Last13WeeksTotalAncillaries = filteredData?.Sum(x => x.Last13WeeksTotalAncillaries) ?? 0,
          L13WManualEntryRate = Convert.ToDecimal(filteredData?.Sum(x => x.L13WManualEntryRate)),
          L13WNotInMoveitRate = Convert.ToDecimal(filteredData?.Sum(x => x.L13WNotInMoveitRate)),
          L13WManualEntryCount = filteredData?.Sum(x => x.Last13WeeksManualEntryCount) ?? 0,
          L13WNotInMoveitCount = filteredData?.Sum(x => x.Last13WeeksNotInMoveitCount) ?? 0,


        L13WAncillariesRate = Convert.ToDecimal(filteredData?.Sum(x => x.L13WAncillariesRate)),
        L13WExceptionRate=Convert.ToDecimal(filteredData?.Sum(x => x.L13WExceptionRate)),

        LastWeekCount = filteredData?.Sum(x => x.LastWeeksCount) ?? 0,
        LastWeekTradingCount = filteredData?.Sum(x => x.LastWeeksTradingCount) ?? 0,
        LastWeekManualEntryCount = filteredData?.Sum(x => x.LastWeekWeeksManualEntryCount) ?? 0,
        LastWeekNotInMoveitCount = filteredData?.Sum(x => x.LastWeeksNotInMoveitCount) ?? 0,
         LastWeekManualEntryRate = Convert.ToDecimal(filteredData?.Sum(x => x.LastWeekManualEntryRate)),
          LastWeekNotInMoveitRate = Convert.ToDecimal(filteredData?.Sum(x => x.LastWeekNotInMoveitRate)),

        LastWeekTotalAncillaries = filteredData.Sum(x => x.LastWeeksTotalAncillaries),
        LWAncillariesRate = Convert.ToDecimal(filteredData?.Sum(x => x.LWAncillariesRate)),
        LWExceptionRate=Convert.ToDecimal(filteredData?.Sum(x => x.LWExceptionRate)),
    }
};
                var LastIVTRunData = Obj.Dash_LastIVTRun
                        .GroupBy(x => x.CurrentWeek)
                        .Select(group => new
                        {
                            CurrentWeek = group.Key,
                            TotCount = group.Sum(x => x.TotCount)
                        })
                         .OrderByDescending(x => x.CurrentWeek)
                        .Take(5)
                        .AsEnumerable() // Switch to LINQ to Objects
                        .Select(item => new Dash_LastIVTRun
                        {
                            CurrentWeek = item.CurrentWeek,
                            TotCount = item.TotCount
                        })
                        .ToList();





                var currentWeek = Obj.FileLoadControl.FirstOrDefault()?.InvoiceWeek.ToString("dd/MM/yyyy");


                // Assuming DashExceptionSummary and vDash_Exception_Summary have similar properties
                //var exception_summary_list = Obj.vDash_Exception_Summary
                //    .Select(x => new DashExceptionSummary
                //    {
                //        ErrDesc = x.ErrDesc,
                //        CurrentWeekCount = x.CurrentWeekCount,
                //        LastWeekCount = x.LastWeekCount,
                //        Last13WeeksAvgCount = x.Last13WeeksAvgCount
                //        // Include other properties as needed
                //    })
                //    .ToList();


                var viewModel = new DashboardViewModel
                {
                    // CommonMeasureData = commonMeasureData,
                    Top10CarrierData = top10CarrierData,
                    Top10CustomerData = top10CustomerData,
                    Top10ExceptionData = top10ExceptionData,
                    TotalMeasuresData_Admin = totalMeasuresData,
                    LastIVTRunData = LastIVTRunData,
                    CurrentWeek = currentWeek,
                    Exception_summary_list = exception_summary_list,
                    Customer_Count_list = CustomerCountList,

                    AM_Count_list = AM_CountList
                };


                return View(viewModel);

            }
            catch (Exception ex)
            {

                return RedirectToAction("TimeOut", "Home");
            }


        }

        public ActionResult Login()
        {


            IVTEntities Obj = new IVTEntities();
            ViewBag.DisplayLinkStatus = "hidden";
            ViewBag.DisplayForAdmin = "hidden";

            ViewBag.LoginList =
          new SelectList(Obj.vPortalLogin.Where(x => x.UserType == 0 && x.IsDeleted == 0 && x.IsActive == 1).ToList().
          Select(x => new { Id = x.LoginName, DWHEditor = x.UserName }), "Id", "DWHEditor ", "acamassa"); // Viewbag
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public ActionResult Login(User user)
        {
            ViewBag.DisplayForAdmin = "hidden";
            ViewBag.DisplayLinkStatus = "hidden";
            ViewBag.DisplayForSuperAdmin = "hidden";
            string submittedIdentity = (user.Username ?? "").Trim();
            string loggeduser = UserIdentity.NormalizeLogin(submittedIdentity);
            IVTEntities usersEntities = new IVTEntities();
            IVTEntities IVTObj = new IVTEntities();

            var userinfo = IVTObj.vPortalLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
            ViewBag.UserName = loggeduser;
            TempData["LoggedAs"] = loggeduser;
            Session["LoggedAs"] = loggeduser;
            MyGlobalVariables.loggedas = 1;
            string message = string.Empty;

            if (userinfo != null && userinfo.IsActive == 1)
            {
                //if(userinfo.IsFirstLogin==true)
                //{
                //    ViewBag.DisplayLinkStatus = "visible";
                //    ViewBag.DisplayForAdmin = "hidden";
                //    ViewBag.DisplayForSuperAdmin = "hidden";
                //    TempData["UserName"] = userinfo.UserName;
                //    Session["Uname"] = userinfo.UserName;
                //    TempData["Utype"]= userinfo.UserType;
                //    TempData["LoginName"] = user.Username.Trim();
                //    Session["LoginName"] = user.Username.Trim();
                //    // return RedirectToAction("ChangePassword");
                //    return RedirectToAction("SearchConsignments", "IntegrityConsignment");

                //}
                //else
                if (userinfo.UserType == 0   && userinfo.IsActive == 1) // Account Manager
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(user.Username, user.RememberMe);
                    TempData["UserName"] = userinfo.UserName;
                    Session["Uname"] = userinfo.UserName;
                    Session["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = user.Username.Trim().ToUpper();
                    Session["LoginName"] = user.Username.Trim().ToUpper();
                    TempData["WindowLogin"] = userinfo.LoginName.Trim().ToUpper();
                    Session["WindowLogin"] = userinfo.LoginName.Trim().ToUpper();
                    return RedirectToAction("Dashboard", "IVT", new { loginname = user.Username.Trim().ToUpper(), loggedas = loggeduser });
                }
                else if ((userinfo.UserType == 1) ) // Admin
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(user.Username, user.RememberMe);
                    TempData["UserName"] = userinfo.UserName;
                    Session["Uname"] = userinfo.UserName;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = user.Username.Trim().ToUpper();
                    Session["LoginName"] = user.Username.Trim().ToUpper();
                    TempData["WindowLogin"] = userinfo.LoginName.Trim().ToUpper();
                    Session["WindowLogin"] = userinfo.LoginName.Trim().ToUpper();

                    return RedirectToAction("Dashboard", "Home");

                }
                //SuperAdmin
                else if ((userinfo.UserType == 2) )
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "visible";
                    FormsAuthentication.SetAuthCookie(user.Username, user.RememberMe);
                    TempData["UserName"] = userinfo.UserName;
                    Session["Uname"] = userinfo.UserName;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = user.Username.Trim().ToUpper();
                    Session["LoginName"] = user.Username.Trim().ToUpper();
                    TempData["WindowLogin"] = userinfo.LoginName.Trim().ToUpper();
                    Session["WindowLogin"] = userinfo.LoginName.Trim().ToUpper();


                    return RedirectToAction("Dashboard", "Home");

                }

            }
            else
            {
                // var checkdisable = usersEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == user.Username.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
                if (userinfo != null && userinfo.IsActive == 0)
                {
                    ViewBag.DisplayLinkStatus = "hidden";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    TempData["Utype"] = 0;
                    message = "Your User Has Disabled!, Please Contact Business Admin";
                }
                else
                {
                    ViewBag.DisplayLinkStatus = "hidden";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    TempData["Utype"] = 0;
                    message = "Username is incorrect - Please Use Windows Login Name";

                }

            }


            ViewBag.Message = message;
            return View(user);
        }






        public ActionResult Logout()
        {
            ViewBag.DisplayLinkStatus = "hidden";
            ViewBag.DisplayForAdmin = "hidden";
            TempData["UserName"] = null;
            Session["Uname"] = null;
            TempData["Utype"] = null;
            Session["Utype"] = null;
            TempData["LoginName"] = null;
            Session["LoginName"] = null;
            TempData["LoggedAs"] = null;
            Session["LoggedAs"] = null;
            MyGlobalVariables.loggedas = 0;
            FormsAuthentication.SignOut();
            // "Back to Admin" uses this action to leave the Account Manager view; it must not end the Microsoft sign-in.
            return RedirectToAction("Dashboard", "Home");
        }
        public ActionResult LogoutAdmin()
        {
            ViewBag.DisplayLinkStatus = "hidden";
            ViewBag.DisplayForAdmin = "hidden";
            ViewBag.DisplayForSuperAdmin = "visible";
            TempData["LoggedAs"] = null;
            Session["LoggedAs"] = null;
            TempData["LoginName"] = TempData["LoginName_SuperAdmin"];
            Session["LoginName"] = Session["LoginName_SuperAdmin"];
            return RedirectToAction("Dashboard", "IVT");
        }



        public ActionResult NotAuthorized()
        {

            return View();
        }

        [HttpPost]

        public ActionResult Logout(string user = "NA")
        {
            ViewBag.DisplayForAdmin = "hidden";
            ViewBag.DisplayLinkStatus = "hidden";
            FormsAuthentication.SignOut();
            // Real sign-out (Profile page): Microsoft users also leave their Entra session.
            if (AFSDataGateway.Services.Entra.EntraAuth.IsEntraUser(HttpContext)) return RedirectToAction("SignOut", "Entra");
            return RedirectToAction("Login");
        }

        public ActionResult ChangePassword()
        {
            //ViewBag.DisplayLinkStatus = "hidden";
            return View();
        }



        public ActionResult TimeOut()
        {
            return View();
        }









        public ActionResult EditUser(long Id)
        {
            using (IVTEntities Obj = new IVTEntities())
            {
                var am = Obj.vIVTUser.Where(a => a.Id == Id).FirstOrDefault();
                var AccountManager = am.UserName;
                ViewBag.CurrentAM = AccountManager;
                ViewBag.UserStatus =
 new SelectList(
new List<SelectListItem>
{

        new SelectListItem { Selected = true, Text = "Account Manager", Value = "0"},
            new SelectListItem { Selected = false, Text = "Admin", Value = "1"},
         new SelectListItem { Selected = false, Text = "Super Admin", Value = "2"},

}, "Value", "Text", 0);
                // ViewBag.DWHEditors =
                //new SelectList(Obj.DWHEditorLogin.GroupBy(x => x.DWHEditor).Select(x => x.FirstOrDefault()).ToList().
                //Select(x => new { Id = x.DWHEditor, DWHEditor = x.DWHEditor }), "Id", "DWHEditor ", DWHEditor.Trim()); // Viewbag
                return View(am);
            }

        }

        [HttpPost]
        public ActionResult EditUser(IVT_User cma)
        {
            try
            {
                string authinticated_user = (string)TempData["LoginName"];

                if (authinticated_user == null)
                {
                    authinticated_user = (string)Session["LoginName"];
                }

                string customer = "";
                if (cma != null)
                {
                    using (IVTEntities Obj = new IVTEntities())
                    {
                        var cma_ = Obj.Entry(cma);
                        IVT_User cmaObj = Obj.IVT_User.Where(x => x.Id == cma.Id).FirstOrDefault();

                        cmaObj.UserName = cma.UserName;
                        cmaObj.LoginName = cma.LoginName;
                        cmaObj.UserType = cma.UserType;

                        Obj.SaveChanges();
                        var loggeduser = authinticated_user.Split('\\').Last();

                        //Obj.spIntegrityReportLog(loggeduser, "User  " + cma.DWHEditor + " has been updated");

                        ViewBag.message = "User  " + cma.UserName + " has been updated";
                        return RedirectToAction("UserList", new
                        {

                            message = ViewBag.message = "User  " + cma.UserName + " has been updated",
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("UserList", new
                    {

                        message = " Failed to update user",
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {

                return RedirectToAction("UserList", new
                {

                    message = "  Failed to update user due to  " + ex.Message,
                    updatestatus = 2
                }); ;
            }

        }

        // COmment for test
        public ActionResult DeleteUser(long Id)
        {
            using (IVTEntities Obj = new IVTEntities())
            {
                var am = Obj.vIVTUser.Where(a => a.Id == Id).FirstOrDefault();
                var AccountManager = am.UserName;
                ViewBag.CurrentAM = AccountManager;
                // ViewBag.DWHEditors =
                //new SelectList(Obj.DWHEditorLogin.GroupBy(x => x.DWHEditor).Select(x => x.FirstOrDefault()).ToList().
                //Select(x => new { Id = x.DWHEditor, DWHEditor = x.DWHEditor }), "Id", "DWHEditor ", DWHEditor.Trim()); // Viewbag
                return View(am);
            }

        }

        [HttpPost]


        public ActionResult DeleteUser(IVT_User cma)
        {
            try
            {
                string authinticated_user = (string)TempData["LoginName"];

                if (authinticated_user == null)
                {
                    authinticated_user = (string)Session["LoginName"];
                }

                if (cma != null)
                {
                    using (IVTEntities Obj = new IVTEntities())
                    {

                        IVT_User cmaObj = Obj.IVT_User.Where(x => x.Id == cma.Id).FirstOrDefault();


                        cmaObj.IsDeleted = 1;

                        Obj.SaveChanges();

                        var loggeduser = authinticated_user.Split('\\').Last();

                    


                        ViewBag.message = "User  " + cmaObj.UserName + " has Deleted Sucessfully";
                        return RedirectToAction("UserList", new
                        {

                            message = ViewBag.message = "User  " + cmaObj.UserName + " has Deleted Sucessfully",
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("UserList", new
                    {

                        message = " Failed to Delete user",
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {

                return RedirectToAction("UserList", new
                {

                    message = "  Failed to Delete user due to  " + ex.Message,
                    updatestatus = 2
                }); ;
            }

        }


        public ActionResult DisableUser(long id)
        {
            try
            {
                string authinticated_user = (string)TempData["LoginName"];

                if (authinticated_user == null)
                {
                    authinticated_user = (string)Session["LoginName"];
                }

                if (id != null)
                {
                    using (IVTEntities Obj = new IVTEntities())
                    {

                        IVT_User cmaObj = Obj.IVT_User.Where(x => x.Id == id).FirstOrDefault();

                        if (cmaObj.IsActive == 1)
                        {
                            cmaObj.IsActive = 0;

                            Obj.SaveChanges();

                            var loggeduser = authinticated_user.Split('\\').Last();

                        

                            ViewBag.message = "User  " + cmaObj.UserName + " has Disabled Sucessfully";
                            return RedirectToAction("UserList", new
                            {

                                message = ViewBag.message = "User  " + cmaObj.UserName + " has Disabled Sucessfully",
                                updatestatus = 1
                            });
                        }

                        else
                        {
                            cmaObj.IsActive = 1;

                            Obj.SaveChanges();

                            var loggeduser = authinticated_user.Split('\\').Last();

                        

                            ViewBag.message = "User  " + cmaObj.UserName + " has Enabled Sucessfully";
                            return RedirectToAction("UserList", new
                            {

                                message = ViewBag.message = "User  " + cmaObj.UserName + " has Enabled Sucessfully",
                                updatestatus = 1
                            });

                        }






                    }
                }
                else
                {

                    return RedirectToAction("UserList", new
                    {

                        message = " Failed to disable user",
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {

                return RedirectToAction("UserList", new
                {

                    message = "  Failed to disable/Enable user due to  " + ex.Message,
                    updatestatus = 2
                }); ;
            }

        }


        public ActionResult ResetPassword(long id)
        {
            try
            {

                if (id != null)
                {
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {

                        DWHEditorLogin cmaObj = Obj.DWHEditorLogin.Where(x => x.Id == id).FirstOrDefault();


                        cmaObj.Password = "123A123a";
                        cmaObj.IsFirstLogin = true;

                        Obj.SaveChanges();

                        ViewBag.message = "Password has reseted to default password (123A123a) and MUST be changed after login";
                        return RedirectToAction("UserList", new
                        {

                            message = ViewBag.message = "Password has reseted to default password (123A123a) and MUST be changed after login",
                            updatestatus = 1
                        });
                    }

                }
                else
                {

                    return RedirectToAction("UserList", new
                    {

                        message = " Failed to reset password",
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {

                return RedirectToAction("UserList", new
                {

                    message = "  Failed to reset password due to  " + ex.Message,
                    updatestatus = 2
                }); ;
            }

        }


        public ActionResult CreateUser()
        {
            ViewBag.UserStatus =
             new SelectList(
    new List<SelectListItem>
    {

        new SelectListItem { Selected = true, Text = "Account Manager", Value = "0"},
            new SelectListItem { Selected = false, Text = "Admin", Value = "1"},
         new SelectListItem { Selected = false, Text = "Super Admin", Value = "2"},

    }, "Value", "Text", 0);
            return View();

        }

        [HttpPost]

        public ActionResult CreateUser(IVT_User aml)
        {
            string customer = "";
            try
            {
                string authinticated_user = (string)TempData["LoginName"];

                if (authinticated_user == null)
                {
                    authinticated_user = (string)Session["LoginName"];
                }

                if (aml.LoginName != null && aml.UserName != null)
                {

                    using (IVTEntities Obj = new IVTEntities())
                    {
                        var loginname = Obj.IVT_User.Where(x => x.LoginName == aml.LoginName).FirstOrDefault();
                        if (loginname == null)
                        {
                            aml.IsActive = 1;
                            aml.IsDeleted = 0;
                            aml.IsFirstLogin = false;
                            aml.IsFormLogin = false;
                            aml.Password = "123A123a";
                            aml.ModifiedDate = DateTime.Today;
                            aml.UserType = aml.UserType;
                            Obj.IVT_User.Add(aml);
                            Obj.SaveChanges();
                            // var user = Obj.DWHEditorLogin.Where(x => x.LoginName == aml.LoginName).FirstOrDefault();

                            // Obj.SaveChanges();

                            var loggeduser = authinticated_user.Split('\\').Last();

                           // Obj.spIntegrityReportLog(loggeduser, "User Added by the name " + aml.UserName);

                            ViewBag.message = "User Added by the name " + aml.UserName;
                            return RedirectToAction("UserList", new
                            {
                                message = "User Added by the name " + aml.UserName,
                                updatestatus = 1
                            });
                        }
                        else
                        {

                            ViewBag.message = "User already exisits under the login " + loginname;
                            return RedirectToAction("UserList", new
                            {
                                message = "User already exisits under the login " + loginname,
                                updatestatus = 1
                            });

                        }




                    }
                }
                else
                {

                    return RedirectToAction("UserList", new
                    {

                        message = " Failed to add Account Manager due to missing information",
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {
                return RedirectToAction("UserList", new
                {

                    message = " Failed to create user",
                    updatestatus = 2
                }); ;
            }


        }








        public ActionResult UserList(string message, int? updatestatus)
        {
            if (message == null)
            {
                ViewBag.ViewMessage = "hidden";
            }
            else if (updatestatus >= 1)
            {
                TempData["message"] = message;
                ViewBag.Message = message;
                ViewBag.messageStatus = updatestatus;
            }
            else
            {
                TempData["message"] = null;
                ViewBag.messageStatus = 0;
                ViewBag.Message = null;
            }

            return View(Get_AllUsers());
        }

        public IEnumerable<vIVTUser> Get_AllUsers()
        {


            string loggeduser = UserIdentity.CurrentLogin(); // "sallan";




            using (IVTEntities Obj = new IVTEntities())
            {
                var userinfo = Obj.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
                ViewBag.UserName = loggeduser;

                string username = (string)userinfo.LoginName;

                int utype = (int)userinfo.UserType;

                if (utype == 2)
                {
                    ViewBag.DisplayForSuperAdmin = "visible";
                }
                else
                {
                    ViewBag.DisplayForSuperAdmin = "hidden";
                }


                if (username != null && utype == 1)
                {
                    return Obj.vIVTUser.Where(a => a.UserType == 0).ToList<vIVTUser>();
                }

                else if (username != null && utype == 2)
                {
                    return Obj.vIVTUser.ToList<vIVTUser>();
                }
                else
                {
                    return Obj.vIVTUser.Where(a => a.UserName == "Test222").ToList<vIVTUser>();
                }

            }
        }

        [HttpPost]

        public ActionResult ChangePassword(User user)
        {
            string LoginName = (string)Session["LoginName"];

            try
            {
                if (user.Password == "123A123a")
                {
                    ViewBag.messageStatus = 2;

                    TempData["chngpwdmessage"] = "Sorry - Please Select another Password!!";
                    return View();

                }
                else if (!(user.Password == user.ConfirmPassword))
                {
                    ViewBag.messageStatus = 2;

                    TempData["chngpwdmessage"] = "Sorry - Passwords are not matched!!";
                    return View();
                }
                else
                {
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {

                        // Obj.DWHEditorLogin.Where(x => x.LoginName.Trim() == LoginName).FirstOrDefault();

                        DWHEditorLogin DWHEditorLoginObj = Obj.DWHEditorLogin.Where(x => x.LoginName.Trim() == LoginName).FirstOrDefault();
                        DWHEditorLoginObj.Password = user.Password;
                        DWHEditorLoginObj.IsFirstLogin = false;
                        Obj.SaveChanges();
                        FormsAuthentication.SetAuthCookie(LoginName, user.RememberMe);
                        //ViewBag.messageStatus = 1;
                        //TempData["chngpwdmessage"] = "Password Changed Successfully ";
                        //return View();

                        ViewBag.message = "Password Changed Successfully";
                        return RedirectToAction("Dashboard", "IVT", new
                        {

                            message = "Password Changed Successfully",
                            updatestatus = 1
                        });

                    }
                }


            }
            catch (Exception)
            {
                //ViewBag.messageStatus = 2;

                //TempData["chngpwdmessage"]= "Error in Changing Password";
                //return View();

                ViewBag.messageStatus = 2;

                TempData["chngpwdmessage"] = "Sorry - Error In Changing Password!!";
                return View();
            }
        }



        //----------IVT Super Admin-----------------------------------------------

        IVTEntities ivtEntities = new IVTEntities();

        IVTEntities usersEntities = new IVTEntities();


        private string GetLoggedUser()
        {
            string loggeduser = "";

            if (TempData["LoggedAs"] == null && Session["LoggedAs"] == null)
            {
                loggeduser = UserIdentity.CurrentLogin();
                Session["Uname"] = loggeduser;
                Session["LoginName"] = loggeduser;
            }
            else if (Session["LoggedAs"] != null)
            {
                loggeduser = (string)Session["LoggedAs"];
                Session["Uname"] = loggeduser;
                Session["LoginName"] = loggeduser;
            }
            else if (TempData["LoggedAs"] != null)
            {
                loggeduser = (string)TempData["LoggedAs"];
                Session["Uname"] = loggeduser;
                Session["LoginName"] = loggeduser;
            }
            else
            {
                loggeduser = UserIdentity.CurrentLogin(); // "sallan";
                Session["Uname"] = loggeduser;
                Session["LoginName"] = loggeduser;
            }



            return loggeduser;
        }

        [HttpPost]
        public async Task<ActionResult> CurrentConsignment(FormCollection formCollection)
        {
            string loggedUser = GetLoggedUser() ?? UserIdentity.CurrentLogin();

            string encodedValue = HttpUtility.UrlEncode(formCollection["MyKey"]); // Replace "YourKey" with the actual key
            try
            {

                if (formCollection["ID"] == null && (formCollection["search"].ToString() == "") && (formCollection["accountManagerFilter"].ToString() == ""))
                {
                    return RedirectToAction("CurrentConsignment", new
                    {
                        message = "Please Enter Search word OR Select Connote(s) First!!",
                        updatestatus = 2
                    });
                }
                else if (formCollection["ID"] != null)
                {
                    string[] ids = formCollection["ID"].Split(new char[] { ',' });

                    foreach (string id in ids)
                    {
                        try
                        {
                            string authenticatedUser = (string)TempData["LoginName"];

                            if (authenticatedUser == null)
                            {
                                authenticatedUser = (string)Session["LoginName"];
                            }

                            if (id != null)
                            {
                                var heldId = Convert.ToInt64(id);
                                var heldConnote = ivtEntities.vIVT_Result.Where(a => a.Id == heldId).FirstOrDefault();

                                if (heldConnote != null)
                                {
                                    using (IVTEntities Obj = new IVTEntities())
                                    {
                                        // Copy relevant properties from HeldConsignment to ConsignmentForReview
                                        ConsignmentForReview connotetoHold = new ConsignmentForReview
                                        {
                                            Connote = heldConnote.Connote,
                                            jno = heldConnote.jno,
                                            Reason = heldConnote.Reason,
                                            cons_date = heldConnote.cons_date,
                                            Customer = heldConnote.customer,
                                            Carrier = heldConnote.carrier,
                                            Sub_Reason = heldConnote.Sub_Reason,
                                            ErrCode = heldConnote.ErrCode,
                                            ErrPriority = heldConnote.ErrPriority,
                                            ShortErrDesc = heldConnote.ShortErrDesc,
                                            ReleasedBy = heldConnote.HeldBy,
                                            InvoiceWeek = heldConnote.CurrentWeek,
                                            Comment = formCollection["Reason"].ToString(),
                                            // Copy other relevant properties from ConsignmentForReview to ReleasedConsignment
                                            ActionedBy = loggedUser,
                                            ActionTime = DateTime.Now,
                                        };
                                        //Obj.sp_IVT_Portal_Log(authenticatedUser.Split('\\').Last(), "Connote:" + connotetoHold.Connote + " has been held");
                                        Obj.ConsignmentForReview.Add(connotetoHold);
                                        // Obj.ReleasedConsignment.Remove(heldConnote);
                                        Obj.SaveChanges();

                                        ViewBag.message = "Connote " + heldConnote.Connote + " has been held";
                                    }

                                }// If not null
                            }
                            else
                            {
                                return RedirectToAction("CurrentConsignment", new
                                {
                                    message = "Your Session Has Expired!!! - Failed to hold consignment - Please Try Again",
                                    updatestatus = 2
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            return RedirectToAction("CurrentConsignment", new
                            {
                                message = "Your Session Has Expired!!! - Failed to hold consignment - Please Try Again - Error: " + ex.Message,
                                updatestatus = 2
                            });
                        }
                    }

                    //-----------Send Notification------------------------------------
                  

                    string body = "Hi IVT Admins,<br><br>";
                    body += "The following connotes have been <b>HELD</b>:<br><br>";
                    foreach (string id in ids)
                    {
                        if (!string.IsNullOrEmpty(id))
                        {

                            var heldId = Convert.ToInt64(id);
                            var heldConnote = ivtEntities.vIVT_Result.Where(a => a.Id == heldId).FirstOrDefault();
                            if (heldConnote != null)
                            {
                                var connote = heldConnote.Connote;
                                var AccountManager = loggedUser;
                                var Customer = heldConnote.customer;
                                var Comment = formCollection["Reason"].ToString();

                                body += $"Connote: {connote},  Held By: {AccountManager},  Customer: {Customer},  Comment: {Comment}<br>";
                            }

                        }
                    }

                    body += "<br><br><br>Please make sure to reflect the changes on billing system and <b>ALWAYS</b> check  the complete list in IVT2.0 System.<br><br><br><br><br> ";

                    body += "This is an Automated System Notification.<br><br> ";
                    body += "Invoice Vetting Tool (IVT2.0) <br> ";

                    await EmailNotification.SendNotificationEmailAsync("IVT Hold Notification - " + loggedUser.ToUpper(), body,loggedUser);

                    using (IVTEntities Obj = new IVTEntities())
                    {
                        // Copy relevant properties from ConsignmentForReview to ReleasedConsignment

                        await Task.Run(() => Obj.Database.ExecuteSqlCommand("EXEC [WebIVT].[sp_IVTDashboardRefresh_TotalMeasure]"));

                    }
                    return RedirectToAction("CurrentConsignment", new
                    {
                        message = "Selected connotes have been held successfully",
                        updatestatus = 1
                    });
                }
                // Rest of the code remains unchanged
                else if (formCollection["ID"] == null && (formCollection["search"] != null))
                {
                    return RedirectToAction("CurrentConsignment", new
                    {
                        search = formCollection["search"].ToString()


                    });
                }
                else
                {
                    return RedirectToAction("CurrentConsignment");
                }
            }
            catch (Exception ex)
            {
                return RedirectToAction("CurrentConsignment", new
                {
                    message = "Your Session Has Expired!!! - Failed to hold consignment - Please Try Again - Error: " + ex.Message,
                    updatestatus = 2
                });
            }
        }



        public ActionResult CurrentConsignment(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
             , string accountManagerFilter, string customerGroupFilter, string customerFilter, string carrierFilter, string workcodeFilter, string minMargin, string maxMargin)
        {
            try
            {
                ViewBag.DisplayForAdmin = "hidden";
                ViewBag.DisplayForSuperAdmin = "hidden";
                ViewBag.DisplayLinkStatus = "hidden";
                //Selected values for default DL list 

                ViewBag.selectedAccountManager = accountManagerFilter;
                ViewBag.selectedCustomerGroup = customerGroupFilter;
                ViewBag.selectedCustomer = customerFilter;
                ViewBag.selectedCarrier = carrierFilter;
                ViewBag.selectedWorkCode = workcodeFilter;

                string authinticated_user = UserIdentity.CurrentLogin();

                if (authinticated_user == null)
                {
                    authinticated_user = (string)Session["LoginName"];
                }


                if (DateTime.Now.Hour < 12)
                {
                    ViewBag.lblGreeting = "Good Morning ";

                }
                else if (DateTime.Now.Hour < 17)
                {
                    ViewBag.lblGreeting = "Good Afternoon ";

                }
                else
                {
                    ViewBag.lblGreeting = "Good Evening ";

                }

                string loggeduser = "";
                if (TempData["LoggedAs"] == null && Session["LoggedAs"] == null && loggedas == null)
                {
                    //string olduserconfig =System.Web.HttpContext.Current.WindowsIdentity.GetCurrent().Name.Split('\\').Last();
                    //string userold=WindowsIdentity.GetCurrent().Name;
                    //HttpContext.Current.WindowsIdentity.GetCurrent().Name


                    loggeduser = UserIdentity.CurrentLogin();
                    //loggeduser = "adagostino";
                    Session["Uname"] = loggeduser;

                    //if (Session["NewVisit"] == null)
                    //{
                    //    ivtEntities.spIntegrityReportLog(loggeduser, "User  " + loggedUser.ToUpper() + " has re-visited portal");
                    //    Session["NewVisit"] = "Visited";
                    //}
                }
                else if (Session["LoggedAs"] != null)
                {

                    loggeduser = (string)Session["LoggedAs"];
                }
                else if (loggedas != null)
                {

                    loggeduser = loggedas;
                }
                else if (TempData["LoggedAs"] != null)
                {

                    loggeduser = (string)TempData["LoggedAs"]; ;
                }
                else
                {
                    loggeduser = UserIdentity.CurrentLogin(); // "sallan";

                }


                var userinfo = usersEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
                ViewBag.UserName = loggeduser;

                string loginmessage = string.Empty;

                if (userinfo != null && userinfo.IsActive == 1)
                {


                    if ((userinfo.UserType == 1))
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "visible";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();



                    }
                    else if ((userinfo.UserType == 2) && userinfo.IsFirstLogin != true)
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "visible";
                        ViewBag.DisplayForSuperAdmin = "visible";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();

                        if (message == null)
                        {
                            ViewBag.ViewMessage = "hidden";
                            ViewBag.LastUpdateViewMessage = "visible";
                        }
                        else if (updatestatus >= 1)
                        {
                            TempData["message"] = message;
                            ViewBag.Message = message;
                            ViewBag.messageStatus = updatestatus;
                            ViewBag.LastUpdateViewMessage = "hidden";
                        }
                        else
                        {
                            TempData["message"] = null;
                            ViewBag.messageStatus = 0;
                            ViewBag.Message = null;
                            ViewBag.LastUpdateViewMessage = "visible";
                        }



                        IVTEntities Obj = new IVTEntities();
                        //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();
                        // //Obj.sp_IVT_Portal_Log(loggeduser, "User has viewed IVT Result");
                        IQueryable<vIVT_Result_Admin> cons = Obj.vIVT_Result_Admin;



                        var UniqueAccountManager = Obj.vIVT_Result_Admin

                             .Select(x => x.AccountManager)
                             .Distinct().OrderBy(x => x)
                             .ToList();


                        var UniqueCustomerGroup = Obj.vIVT_Result_Admin

                             .Select(x => x.FinanceGroup)
                             .Distinct().OrderBy(x => x)
                             .ToList();


                        var uniqueCustomers = Obj.vIVT_Result_Admin
                          .Select(x => x.customer)
                          .Distinct()
                          .OrderBy(x => x)
                          .ToList();


                        var uniqueCarriers = Obj.vIVT_Result_Admin
                        .Select(x => x.carrier)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                        var uniqueWorkcodes = Obj.vIVT_Result_Admin
                        .Select(x => x.work_code)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();


                        ViewBag.UniqueAccountManager = new SelectList(UniqueAccountManager);
                        ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                        ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                        ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                        ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);


                        if (!string.IsNullOrEmpty(search))
                        {
                            cons = cons.Where(x => x.con_note.Contains(search) || x.Reason.Contains(search) || x.customer.Contains(search) || x.carrier.Contains(search) || x.ErrCode.Contains(search));
                        }

                        if (!string.IsNullOrEmpty(accountManagerFilter))
                        {
                            cons = cons.Where(x => x.AccountManager.Equals(accountManagerFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(customerGroupFilter))
                        {
                            cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                        }

                        // New filters
                        if (!string.IsNullOrEmpty(customerFilter))
                        {
                            cons = cons.Where(x => x.customer.Contains(customerFilter));
                        }

                        if (!string.IsNullOrEmpty(carrierFilter))
                        {
                            cons = cons.Where(x => x.carrier.Contains(carrierFilter));
                        }

                        if (!string.IsNullOrEmpty(workcodeFilter))
                        {
                            cons = cons.Where(x => x.work_code.Contains(workcodeFilter));
                        }

                        if (!string.IsNullOrEmpty(minMargin))
                        {
                            decimal minMarginValue = string.IsNullOrEmpty(minMargin) ? 0 : Convert.ToDecimal(minMargin);

                            cons = cons.Where(x => x.GrossMargin >= minMarginValue);
                        }

                        if (!string.IsNullOrEmpty(maxMargin))
                        {
                            decimal maxMarginValue = string.IsNullOrEmpty(maxMargin) ? decimal.MaxValue : Convert.ToDecimal(maxMargin);
                            cons = cons.Where(x => x.GrossMargin <= maxMarginValue);
                        }

                        if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                            && string.IsNullOrEmpty(minMargin) && string.IsNullOrEmpty(maxMargin) && string.IsNullOrEmpty(customerGroupFilter) && string.IsNullOrEmpty(accountManagerFilter))
                        {
                            cons = cons.OrderByDescending(x => x.total_nett); 
                        }


                        // At this point the query has generated a SQL statement based on the conditions above,
                        // but it will not be executed until the until the next line - i.e. when calling .ToList()
                        return View(cons.ToList().Take(200));

                    }

                }
                else
                {
                    // var checkdisable = ivtEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
                    if (userinfo != null && userinfo.IsActive == 0)
                    {
                        ViewBag.DisplayLinkStatus = "hidden";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        TempData["Utype"] = 0;
                        message = "Your User Has Disabled!, Please Contact Business Admin";
                        return RedirectToAction("NotAuthorized", "Home");

                    }
                    else
                    {
                        ViewBag.DisplayLinkStatus = "hidden";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        TempData["Utype"] = 0;
                        message = "You  have no rights , please contact support.";
                        return RedirectToAction("NotAuthorized", "Home");

                    }

                }
                try
                {
                    if (Session["LoggedAs"] != null)
                    {

                        ViewBag.DisplayForSuperAdmin = "visible";
                    }





                    var DWHEditor = (string)Session["Uname"];
                    IVTEntities Obj = new IVTEntities();
                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    IQueryable<vIVT_Result_Admin> cons = Obj.vIVT_Result_Admin;


                    var UniqueAccountManager = Obj.vIVT_Result_Admin

                         .Select(x => x.AccountManager)
                         .Distinct().OrderBy(x => x)
                         .ToList();


                    var UniqueCustomerGroup = Obj.vIVT_Result_Admin

                         .Select(x => x.FinanceGroup)
                         .Distinct().OrderBy(x => x)
                         .ToList();


                    var uniqueCustomers = Obj.vIVT_Result_Admin
                      .Select(x => x.customer)
                      .Distinct()
                      .OrderBy(x => x)
                      .ToList();


                    var uniqueCarriers = Obj.vIVT_Result_Admin
                    .Select(x => x.carrier)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                    var uniqueWorkcodes = Obj.vIVT_Result_Admin
                    .Select(x => x.work_code)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();


                    ViewBag.UniqueAccountManager = new SelectList(UniqueAccountManager);
                    ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                    ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                    ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                    ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);


                    if (!string.IsNullOrEmpty(search))
                    {
                        cons = cons.Where(x => x.con_note.Contains(search) || x.Reason.Contains(search) || x.customer.Contains(search) || x.carrier.Contains(search) || x.ErrCode.Contains(search));
                    }

                    if (!string.IsNullOrEmpty(accountManagerFilter))
                    {
                        cons = cons.Where(x => x.AccountManager.Equals(accountManagerFilter)).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(customerGroupFilter))
                    {
                        cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                    }

                    // New filters
                    if (!string.IsNullOrEmpty(customerFilter))
                    {
                        cons = cons.Where(x => x.customer.Contains(customerFilter));
                    }

                    if (!string.IsNullOrEmpty(carrierFilter))
                    {
                        cons = cons.Where(x => x.carrier.Contains(carrierFilter));
                    }

                    if (!string.IsNullOrEmpty(workcodeFilter))
                    {
                        cons = cons.Where(x => x.work_code.Contains(workcodeFilter));
                    }

                    if (!string.IsNullOrEmpty(minMargin))
                    {
                        decimal minMarginValue = string.IsNullOrEmpty(minMargin) ? 0 : Convert.ToDecimal(minMargin);

                        cons = cons.Where(x => x.GrossMargin >= minMarginValue);
                    }

                    if (!string.IsNullOrEmpty(maxMargin))
                    {
                        decimal maxMarginValue = string.IsNullOrEmpty(maxMargin) ? decimal.MaxValue : Convert.ToDecimal(maxMargin);
                        cons = cons.Where(x => x.GrossMargin <= maxMarginValue);
                    }

                    if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                        && string.IsNullOrEmpty(minMargin) && string.IsNullOrEmpty(maxMargin) && string.IsNullOrEmpty(customerGroupFilter) && string.IsNullOrEmpty(accountManagerFilter))
                    {
                        cons = cons.OrderByDescending(x => x.total_nett);
                    }

                    // At this point the query has generated a SQL statement based on the conditions above,
                    // but it will not be executed until the until the next line - i.e. when calling .ToList()

                   

                    var results = cons.Take(200).ToList();

                    // Now, pass the results to the View
                    return View(results);
                }
                catch (Exception)
                {
                    return RedirectToAction("TimeOut", "Home");
                }

            }
            catch (Exception)
            {

                return RedirectToAction("TimeOut", "Home");
            }

        }


        public ActionResult ExceptionHistory(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
      , string accountManagerFilter, string customerGroupFilter, string customerFilter, string carrierFilter, string workcodeFilter, string minMargin, string maxMargin, DateTime? invoiceWeek)
        {




            ViewBag.selectedCustomerGroup = customerGroupFilter;
            ViewBag.selectedCustomer = customerFilter;
            ViewBag.selectedCarrier = carrierFilter;
            ViewBag.selectedWorkCode = workcodeFilter;

            ViewBag.Search = search;
            ViewBag.CustomerFilter = customerFilter;
            ViewBag.CarrierFilter = carrierFilter;
            ViewBag.WorkcodeFilter = workcodeFilter;
            ViewBag.MinMargin = minMargin;
            ViewBag.MaxMargin = maxMargin;
            ViewBag.InvoiceWeek = invoiceWeek;



            try
            {
                ViewBag.DisplayForAdmin = "hidden";
                ViewBag.DisplayForSuperAdmin = "hidden";
                ViewBag.DisplayLinkStatus = "hidden";
                //Selected values for default DL list 

                ViewBag.selectedAccountManager = accountManagerFilter;
                ViewBag.selectedCustomerGroup = customerGroupFilter;
                ViewBag.selectedCustomer = customerFilter;
                ViewBag.selectedCarrier = carrierFilter;
                ViewBag.selectedWorkCode = workcodeFilter;

                string authinticated_user = UserIdentity.CurrentLogin();

                if (authinticated_user == null)
                {
                    authinticated_user = (string)Session["LoginName"];
                }



                string loggeduser = "";
                if (TempData["LoggedAs"] == null && Session["LoggedAs"] == null && loggedas == null)
                {
                    //string olduserconfig =System.Web.HttpContext.Current.WindowsIdentity.GetCurrent().Name.Split('\\').Last();
                    //string userold=WindowsIdentity.GetCurrent().Name;
                    //HttpContext.Current.WindowsIdentity.GetCurrent().Name


                    loggeduser = UserIdentity.CurrentLogin();
                    //loggeduser = "adagostino";
                    Session["Uname"] = loggeduser;

                    //if (Session["NewVisit"] == null)
                    //{
                    //    ivtEntities.spIntegrityReportLog(loggeduser, "User  " + loggedUser.ToUpper() + " has re-visited portal");
                    //    Session["NewVisit"] = "Visited";
                    //}
                }
                else if (Session["LoggedAs"] != null)
                {

                    loggeduser = (string)Session["LoggedAs"];
                }
                else if (loggedas != null)
                {

                    loggeduser = loggedas;
                }
                else if (TempData["LoggedAs"] != null)
                {

                    loggeduser = (string)TempData["LoggedAs"]; ;
                }
                else
                {
                    loggeduser = UserIdentity.CurrentLogin(); // "sallan";

                }


                var userinfo = usersEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
                ViewBag.UserName = loggeduser;

                string loginmessage = string.Empty;

                if (userinfo != null && userinfo.IsActive == 1)
                {


                    if ((userinfo.UserType == 1))
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "visible";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();



                    }
                    else if ((userinfo.UserType == 2) )
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "visible";
                        ViewBag.DisplayForSuperAdmin = "visible";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();

                        if (message == null)
                        {
                            ViewBag.ViewMessage = "hidden";
                            ViewBag.LastUpdateViewMessage = "visible";
                        }
                        else if (updatestatus >= 1)
                        {
                            TempData["message"] = message;
                            ViewBag.Message = message;
                            ViewBag.messageStatus = updatestatus;
                            ViewBag.LastUpdateViewMessage = "hidden";
                        }
                        else
                        {
                            TempData["message"] = null;
                            ViewBag.messageStatus = 0;
                            ViewBag.Message = null;
                            ViewBag.LastUpdateViewMessage = "visible";
                        }








                        IVTEntities Obj = new IVTEntities();
                        //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();
                        //Obj.sp_IVT_Portal_Log(loggeduser, "User has viewed IVT Result");
                        IQueryable<vIVT_ResultHistory_Admin> cons = Obj.vIVT_ResultHistory_Admin;


                        var UniqueAccountManager = Obj.vIVT_ResultHistory_Admin

                    .Select(x => x.AccountManager)
                    .Distinct().OrderBy(x => x)
                    .ToList();


                        var UniqueCustomerGroup = Obj.vIVT_ResultHistory_Admin
                            
                            .Select(x => x.FinanceGroup)
                            .Distinct().OrderBy(x => x)
                            .ToList();


                        var uniqueCustomers = Obj.IVT_ResultHistory
                                  
                                  .Select(x => x.customer)
                                  .Distinct().OrderBy(x => x)
                                  .ToList();

                        var uniqueCarriers = Obj.IVT_ResultHistory.Where(x =>  x.CurrentWeek == invoiceWeek.Value).Select(x => x.carrier).Distinct().OrderBy(x => x).ToList();
                        var uniqueWorkcodes = Obj.IVT_ResultHistory.Where(x =>  x.CurrentWeek == invoiceWeek.Value).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();

                        ViewBag.UniqueAccountManager = new SelectList(UniqueAccountManager);
                        ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                        ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                        ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                        ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);




                        // New filters

                        if (!string.IsNullOrEmpty(accountManagerFilter))
                        {
                            cons = cons.Where(x => x.AccountManager.Equals(accountManagerFilter)).Take(1000);
                        }


                        if (!string.IsNullOrEmpty(customerGroupFilter))
                        {
                            cons = cons.Where(x =>  x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                        }



                        if (!string.IsNullOrEmpty(search))
                        {
                            cons = cons.Where(x =>  x.con_note.Contains(search) || x.Reason.Contains(search) || x.customer.Contains(search) || x.ErrCode.Contains(search)).Take(1000);
                        }

                        // New filters
                        if (!string.IsNullOrEmpty(customerFilter))
                        {
                            cons = cons.Where(x =>  x.customer.Equals(customerFilter));
                        }

                        if (!string.IsNullOrEmpty(carrierFilter))
                        {
                            cons = cons.Where(x =>  x.carrier.Equals(carrierFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(workcodeFilter))
                        {
                            cons = cons.Where(x =>  x.work_code.Equals(workcodeFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(minMargin))
                        {
                            decimal minMarginValue = string.IsNullOrEmpty(minMargin) ? 0 : Convert.ToDecimal(minMargin);

                            cons = cons.Where(x =>  x.GrossMargin >= minMarginValue).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(maxMargin))
                        {
                            decimal maxMarginValue = string.IsNullOrEmpty(maxMargin) ? decimal.MaxValue : Convert.ToDecimal(maxMargin);
                            cons = cons.Where(x =>  x.GrossMargin <= maxMarginValue).Take(1000);
                        }


                        if (invoiceWeek != null)
                        {


                            cons = cons
                                .Where(x =>  x.CurrentWeek == invoiceWeek.Value)
                                .Take(1000);
                        }

                        if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                          && string.IsNullOrEmpty(accountManagerFilter) && string.IsNullOrEmpty(minMargin) && string.IsNullOrEmpty(maxMargin) && string.IsNullOrEmpty(customerGroupFilter) && invoiceWeek == null)
                        {
                            cons = cons.Where(x => x.HeldBy.Equals(loggeduser)).Take(200);
                        }


                        cons = cons.OrderByDescending(x => x.total_nett);

                        return View(cons.ToList().Take(200));
                    }

                }

                else
                {
                    // var checkdisable = ivtEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
                    if (userinfo != null && userinfo.IsActive == 0)
                    {
                        ViewBag.DisplayLinkStatus = "hidden";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        TempData["Utype"] = 0;
                        message = "Your User Has Disabled!, Please Contact Business Admin";
                        return RedirectToAction("NotAuthorized", "Home");

                    }
                    else
                    {
                        ViewBag.DisplayLinkStatus = "hidden";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        TempData["Utype"] = 0;
                        message = "You  have no rights , please contact support.";
                        return RedirectToAction("NotAuthorized", "Home");

                    }

                }

                try
                {
                    if (Session["LoggedAs"] != null)
                    {

                        ViewBag.DisplayForSuperAdmin = "visible";
                    }





                    var DWHEditor = (string)Session["Uname"];
                    IVTEntities Obj = new IVTEntities();
                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    IQueryable<vIVT_ResultHistory_Admin> cons = Obj.vIVT_ResultHistory_Admin;


                    var UniqueAccountManager = Obj.vIVT_ResultHistory_Admin

                .Select(x => x.AccountManager)
                .Distinct().OrderBy(x => x)
                .ToList();


                    var UniqueCustomerGroup = Obj.vIVT_ResultHistory_Admin
                       
                        .Select(x => x.FinanceGroup)
                        .Distinct().OrderBy(x => x)
                        .ToList();


                    var uniqueCustomers = Obj.IVT_ResultHistory
                             
                              .Select(x => x.customer)
                              .Distinct().OrderBy(x => x)
                              .ToList();

                    var uniqueCarriers = Obj.IVT_ResultHistory.Where(x => x.CurrentWeek == invoiceWeek.Value).Select(x => x.carrier).Distinct().OrderBy(x => x).ToList();
                    var uniqueWorkcodes = Obj.IVT_ResultHistory.Where(x => x.CurrentWeek == invoiceWeek.Value).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();

                    ViewBag.UniqueAccountManager = new SelectList(UniqueAccountManager);
                    ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                    ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                    ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                    ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);




                    // New filters

                    if (!string.IsNullOrEmpty(accountManagerFilter))
                    {
                        cons = cons.Where(x => x.AccountManager.Equals(accountManagerFilter)).Take(1000);
                    }


                    if (!string.IsNullOrEmpty(customerGroupFilter))
                    {
                        cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                    }



                    if (!string.IsNullOrEmpty(search))
                    {
                        cons = cons.Where(x => x.con_note.Contains(search) || x.Reason.Contains(search) || x.customer.Contains(search) || x.ErrCode.Contains(search)).Take(1000);
                    }

                    // New filters
                    if (!string.IsNullOrEmpty(customerFilter))
                    {
                        cons = cons.Where(x => x.customer.Equals(customerFilter));
                    }

                    if (!string.IsNullOrEmpty(carrierFilter))
                    {
                        cons = cons.Where(x => x.carrier.Equals(carrierFilter)).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(workcodeFilter))
                    {
                        cons = cons.Where(x => x.work_code.Equals(workcodeFilter)).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(minMargin))
                    {
                        decimal minMarginValue = string.IsNullOrEmpty(minMargin) ? 0 : Convert.ToDecimal(minMargin);

                        cons = cons.Where(x => x.GrossMargin >= minMarginValue).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(maxMargin))
                    {
                        decimal maxMarginValue = string.IsNullOrEmpty(maxMargin) ? decimal.MaxValue : Convert.ToDecimal(maxMargin);
                        cons = cons.Where(x => x.GrossMargin <= maxMarginValue).Take(1000);
                    }


                    if (invoiceWeek != null)
                    {


                        cons = cons
                            .Where(x => x.CurrentWeek == invoiceWeek.Value)
                            .Take(1000);
                    }

                    if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                      && string.IsNullOrEmpty(accountManagerFilter) && string.IsNullOrEmpty(minMargin) && string.IsNullOrEmpty(maxMargin) && string.IsNullOrEmpty(customerGroupFilter) && invoiceWeek == null)
                    {
                        cons = cons.Where(x => x.HeldBy.Equals(loggeduser)).Take(200);
                    }


                    cons = cons.OrderByDescending(x => x.total_nett);

                    return View(cons.ToList().Take(200));
                    // Now, pass the results to the View

                }

                catch (Exception)
                {
                    return RedirectToAction("TimeOut", "Home");
                }
            }
          
  


            catch (Exception)
            {

                return RedirectToAction("TimeOut", "Home");
            }

        }




        public ActionResult DownloadCSV_IVTResultsHistory(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
      , string accountManagerFilter, string customerGroupFilter, string customerFilter, string carrierFilter, string workcodeFilter, string minMargin, string maxMargin, DateTime? invoiceWeek)
        {






            IVTEntities Obj = new IVTEntities();
            IQueryable<vIVT_ResultHistory> cons = Obj.vIVT_ResultHistory;


            var UniqueAccountManager = Obj.vIVT_ResultHistory

        .Select(x => x.AccountManager)
        .Distinct().OrderBy(x => x)
        .ToList();


            var UniqueCustomerGroup = Obj.vIVT_ResultHistory

                .Select(x => x.FinanceGroup)
                .Distinct().OrderBy(x => x)
                .ToList();


            var uniqueCustomers = Obj.IVT_ResultHistory

                      .Select(x => x.customer)
                      .Distinct().OrderBy(x => x)
                      .ToList();

            var uniqueCarriers = Obj.IVT_ResultHistory.Where(x => x.CurrentWeek == invoiceWeek.Value).Select(x => x.carrier).Distinct().OrderBy(x => x).ToList();
            var uniqueWorkcodes = Obj.IVT_ResultHistory.Where(x => x.CurrentWeek == invoiceWeek.Value).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();

            ViewBag.UniqueAccountManager = new SelectList(UniqueAccountManager);
            ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
            ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
            ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
            ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);




            // New filters

            if (!string.IsNullOrEmpty(accountManagerFilter))
            {
                cons = cons.Where(x => x.AccountManager.Equals(accountManagerFilter));
            }


            if (!string.IsNullOrEmpty(customerGroupFilter))
            {
                cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter));
            }



            if (!string.IsNullOrEmpty(search))
            {
                cons = cons.Where(x => x.con_note.Contains(search) || x.Reason.Contains(search) || x.customer.Contains(search) || x.ErrCode.Contains(search));
            }

            // New filters
            if (!string.IsNullOrEmpty(customerFilter))
            {
                cons = cons.Where(x => x.customer.Equals(customerFilter));
            }

            if (!string.IsNullOrEmpty(carrierFilter))
            {
                cons = cons.Where(x => x.carrier.Equals(carrierFilter));
            }

            if (!string.IsNullOrEmpty(workcodeFilter))
            {
                cons = cons.Where(x => x.work_code.Equals(workcodeFilter));
            }

            if (!string.IsNullOrEmpty(minMargin))
            {
                decimal minMarginValue = string.IsNullOrEmpty(minMargin) ? 0 : Convert.ToDecimal(minMargin);

                cons = cons.Where(x => x.GrossMargin >= minMarginValue).Take(1000);
            }

            if (!string.IsNullOrEmpty(maxMargin))
            {
                decimal maxMarginValue = string.IsNullOrEmpty(maxMargin) ? decimal.MaxValue : Convert.ToDecimal(maxMargin);
                cons = cons.Where(x => x.GrossMargin <= maxMarginValue).Take(1000);
            }


            if (invoiceWeek != null)
            {


                cons = cons
                    .Where(x => x.CurrentWeek == invoiceWeek.Value)
                    .Take(1000);
            }

            if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
              && string.IsNullOrEmpty(accountManagerFilter) && string.IsNullOrEmpty(minMargin) && string.IsNullOrEmpty(maxMargin) && string.IsNullOrEmpty(customerGroupFilter) && invoiceWeek == null)
            {
                cons = cons.Take(200);
            }



            // Retrieve data for CSV
            var data = cons.ToList();


            // Generate CSV content
            var csvContent = "InvoiceWeek,Connote,Customer,ErrCode,Exception,Carrier,ConnoteDate,Exception Detail\n";
            foreach (var item in data)
            {
                csvContent += $"{item.CurrentWeek},{item.Connote},{item.customer},{item.ErrCode},{item.Reason},{item.carrier},{item.cons_date},{item.Sub_Reason}\n";
            }

            // Return CSV file
            return File(new System.Text.UTF8Encoding().GetBytes(csvContent), "text/csv", "IVT_ExceptionsHistory_Download.csv");
        }


        public ActionResult GetCustomerGroups(string accountmanager, string viewname)
        {

            IVTEntities Obj = new IVTEntities();

            if (viewname == "CurrentConsignment")
            {

                IQueryable<vIVT_Result> customers = Obj.vIVT_Result;

                if (!string.IsNullOrEmpty(accountmanager))
                {
                    customers = customers.Where(x => x.AccountManager == accountmanager);
                }

                var distinctcustomers = customers.Select(x => x.FinanceGroup).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "AncillarySummary")
            {

                IQueryable<vAllCurrentAncillarySummary> customers = Obj.vAllCurrentAncillarySummary;

                if (!string.IsNullOrEmpty(accountmanager))
                {
                    customers = customers.Where(x => x.AccountManager == accountmanager);
                }

                var distinctcustomers = customers.Select(x => x.FinanceGroup).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "TradingData")
            {

                IQueryable<vAllCurrentDetails> customers = Obj.vAllCurrentDetails;

                if (!string.IsNullOrEmpty(accountmanager))
                {
                    customers = customers.Where(x => x.AccountManager == accountmanager);
                }

                var distinctcustomers = customers.Select(x => x.FinanceGroup).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "TradingDataHistory")
            {

                IQueryable<vIVT_TradingDataHistory> customers = Obj.vIVT_TradingDataHistory;

                if (!string.IsNullOrEmpty(accountmanager))
                {
                    customers = customers.Where(x => x.AccountManager == accountmanager);
                }

                var distinctcustomers = customers.Select(x => x.FinanceGroup).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "ExceptionHistory")
            {

                IQueryable<vIVT_AllResultHistory> customers = Obj.vIVT_AllResultHistory;

                if (!string.IsNullOrEmpty(accountmanager))
                {
                    customers = customers.Where(x => x.AccountManager == accountmanager);
                }

                var distinctcustomers = customers.Select(x => x.FinanceGroup).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else
            {

                IQueryable<vIVT_Result> customers = Obj.vIVT_Result;

                if (!string.IsNullOrEmpty(accountmanager))
                {
                    customers = customers.Where(x => x.AccountManager == accountmanager);
                }

                var distinctcustomers = customers.Select(x => x.FinanceGroup).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }





        }

        public ActionResult GetCustomers(string customergroup, string viewname)
        {
           
            IVTEntities Obj = new IVTEntities();

            if (viewname == "CurrentConsignment")
            {
              
                IQueryable<vIVT_Result> customers = Obj.vIVT_Result;

                if (!string.IsNullOrEmpty(customergroup))
                {
                    customers = customers.Where(x => x.FinanceGroup == customergroup);
                }

                var distinctcustomers = customers.Select(x => x.customer).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "AncillarySummary")
            {
             
                IQueryable<vAllCurrentAncillarySummary> customers = Obj.vAllCurrentAncillarySummary;

                if (!string.IsNullOrEmpty(customergroup))
                {
                    customers = customers.Where(x => x.FinanceGroup == customergroup);
                }

                var distinctcustomers = customers.Select(x => x.CustomerCode).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "TradingData")
            {
             
                IQueryable<vAllCurrentDetails> customers = Obj.vAllCurrentDetails;

                if (!string.IsNullOrEmpty(customergroup))
                {
                    customers = customers.Where(x => x.FinanceGroup == customergroup);
                }

                var distinctcustomers = customers.Select(x => x.Customer).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "TradingDataHistory")
            {
       
                IQueryable<vIVT_TradingDataHistory> customers = Obj.vIVT_TradingDataHistory;

                if (!string.IsNullOrEmpty(customergroup))
                {
                    customers = customers.Where(x => x.FinanceGroup == customergroup);
                }

                var distinctcustomers = customers.Select(x => x.Customer).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "ExceptionHistory")
            {
          
                IQueryable<vIVT_AllResultHistory> customers = Obj.vIVT_AllResultHistory;

                if (!string.IsNullOrEmpty(customergroup))
                {
                    customers = customers.Where(x => x.FinanceGroup == customergroup);
                }

                var distinctcustomers = customers.Select(x => x.customer).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else
            {
               
                IQueryable<vIVT_Result> customers = Obj.vIVT_Result;

                if (!string.IsNullOrEmpty(customergroup))
                {
                    customers = customers.Where(x => x.FinanceGroup == customergroup);
                }

                var distinctcustomers = customers.Select(x => x.customer).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }





        }

        public ActionResult GetCarriers(string customer, string viewname)
        {
           
            IVTEntities Obj = new IVTEntities();

            if (viewname == "CurrentConsignment")
            {
                IQueryable<vIVT_Result> carriers = Obj.vIVT_Result;

                if (!string.IsNullOrEmpty(customer))
                {
                    carriers = carriers.Where(x => x.customer == customer);
                }

                var distinctCarriers = carriers.Select(x => x.carrier).Distinct();

                var result = distinctCarriers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "AncillarySummary")
            {
                IQueryable<AncillarySummary> carriers = Obj.AncillarySummary;

                if (!string.IsNullOrEmpty(customer))
                {
                    carriers = carriers.Where(x => x.CustomerCode == customer);
                }

                var distinctCarriers = carriers.Select(x => x.Carrier).Distinct();

                var result = distinctCarriers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "TradingData")
            {
                IQueryable<vAllCurrentDetails> carriers = Obj.vAllCurrentDetails;
       

                if (!string.IsNullOrEmpty(customer))
                {
                    carriers = carriers.Where(x => x.Customer == customer);
                }

                var distinctCarriers = carriers.Select(x => x.Carrier).Distinct();

                var result = distinctCarriers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "TradingDataHistory")
            {
                IQueryable<IVT_TradingDataHistory> carriers = Obj.IVT_TradingDataHistory;
                

                if (!string.IsNullOrEmpty(customer))
                {
                    carriers = carriers.Where(x => x.Customer == customer);
                }

                var distinctCarriers = carriers.Select(x => x.Carrier).Distinct();

                var result = distinctCarriers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "ExceptionHistory")
            {
                IQueryable<IVT_ResultHistory> carriers = Obj.IVT_ResultHistory;
               

                if (!string.IsNullOrEmpty(customer))
                {
                    carriers = carriers.Where(x => x.customer == customer);
                }

                var distinctCarriers = carriers.Select(x => x.carrier).Distinct();

                var result = distinctCarriers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else
            {
                IQueryable<vIVT_Result> carriers = Obj.vIVT_Result;
           

                if (!string.IsNullOrEmpty(customer))
                {
                    carriers = carriers.Where(x => x.customer == customer);
                }

                var distinctCarriers = carriers.Select(x => x.carrier).Distinct();

                var result = distinctCarriers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }





        }

        public ActionResult GetWorkcodes(string customer, string carrier, string viewname)
        {
            
            IVTEntities Obj = new IVTEntities();
            IQueryable<string> workcodes;

            if (viewname == "CurrentConsignment") // Current Consignment
            {


                if (string.IsNullOrEmpty(customer) && string.IsNullOrEmpty(carrier))
                {
                    // If both customer and carrier are null or empty, return unique workcodes for the logged user
                    workcodes = Obj.vIVT_Result
                        
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(customer) && !string.IsNullOrEmpty(carrier))
                {
                    // If both customer and carrier are provided, filter based on both
                    workcodes = Obj.vIVT_Result
                        .Where(x => x.customer == customer && x.carrier == carrier && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(customer))
                {
                    // If only customer is provided, filter based on customer
                    workcodes = Obj.vIVT_Result
                        .Where(x => x.customer == customer && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(carrier))
                {
                    // If only carrier is provided, filter based on carrier
                    workcodes = Obj.vIVT_Result
                        .Where(x => x.carrier == carrier && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else
                {
                    // If none of the parameters are provided, return unique workcodes for the logged user
                    workcodes = Obj.vIVT_Result
                        
                        .Select(x => x.work_code).Where(x =>  !string.IsNullOrEmpty(x))
                        .Distinct();
                }

                var result = workcodes.Select(w => new { value = w, text = w });
                return Json(result, JsonRequestBehavior.AllowGet);
            }
            else if (viewname == "AncillarySummary")
            {
                if (string.IsNullOrEmpty(customer) && string.IsNullOrEmpty(carrier))
                {
                    // If both customer and carrier are null or empty, return unique workcodes for the logged user
                    workcodes = Obj.AncillarySummary
                        
                        .Select(x => x.WorkCode)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(customer) && !string.IsNullOrEmpty(carrier))
                {
                    // If both customer and carrier are provided, filter based on both
                    workcodes = Obj.AncillarySummary
                        .Where(x => x.CustomerCode == customer && x.Carrier == carrier && !string.IsNullOrEmpty(x.WorkCode))
                        .Select(x => x.WorkCode)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(customer))
                {
                    // If only customer is provided, filter based on customer
                    workcodes = Obj.AncillarySummary
                        .Where(x => x.CustomerCode == customer && !string.IsNullOrEmpty(x.WorkCode))
                        .Select(x => x.WorkCode)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(carrier))
                {
                    // If only carrier is provided, filter based on carrier
                    workcodes = Obj.AncillarySummary
                        .Where(x => x.Carrier == carrier && !string.IsNullOrEmpty(x.WorkCode))
                        .Select(x => x.WorkCode)
                        .Distinct();
                }
                else
                {
                    // If none of the parameters are provided, return unique workcodes for the logged user
                    workcodes = Obj.vIVT_Result
                        
                        .Select(x => x.work_code)
                        .Distinct();
                }

                var result = workcodes.Select(w => new { value = w, text = w });
                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "TradingData")
            {
                if (string.IsNullOrEmpty(customer) && string.IsNullOrEmpty(carrier))
                {
                    // If both customer and carrier are null or empty, return unique workcodes for the logged user
                    workcodes = Obj.vAllCurrentDetails
                        
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(customer) && !string.IsNullOrEmpty(carrier))
                {
                    // If both customer and carrier are provided, filter based on both
                    workcodes = Obj.vAllCurrentDetails
                        .Where(x => x.Customer == customer && x.Carrier == carrier && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(customer))
                {
                    // If only customer is provided, filter based on customer
                    workcodes = Obj.vAllCurrentDetails
                        .Where(x => x.Customer == customer && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(carrier))
                {
                    // If only carrier is provided, filter based on carrier
                    workcodes = Obj.vAllCurrentDetails
                        .Where(x => x.Carrier == carrier && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else
                {
                    // If none of the parameters are provided, return unique workcodes for the logged user
                    workcodes = Obj.vAllCurrentDetails
                        
                        .Select(x => x.work_code)
                        .Distinct();
                }

                var result = workcodes.Select(w => new { value = w, text = w });
                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "TradingDataHistory")
            {
                if (string.IsNullOrEmpty(customer) && string.IsNullOrEmpty(carrier))
                {
                    // If both customer and carrier are null or empty, return unique workcodes for the logged user
                    workcodes = Obj.IVT_TradingDataHistory
                        
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(customer) && !string.IsNullOrEmpty(carrier))
                {
                    // If both customer and carrier are provided, filter based on both
                    workcodes = Obj.IVT_TradingDataHistory
                        .Where(x => x.Customer == customer && x.Carrier == carrier && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(customer))
                {
                    // If only customer is provided, filter based on customer
                    workcodes = Obj.IVT_TradingDataHistory
                        .Where(x => x.Customer == customer && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(carrier))
                {
                    // If only carrier is provided, filter based on carrier
                    workcodes = Obj.IVT_TradingDataHistory
                        .Where(x => x.Carrier == carrier && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else
                {
                    // If none of the parameters are provided, return unique workcodes for the logged user
                    workcodes = Obj.IVT_TradingDataHistory
                        
                        .Select(x => x.work_code)
                        .Distinct();
                }

                var result = workcodes.Select(w => new { value = w, text = w });
                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "ExceptionHistory")
            {
                if (string.IsNullOrEmpty(customer) && string.IsNullOrEmpty(carrier))
                {
                    // If both customer and carrier are null or empty, return unique workcodes for the logged user
                    workcodes = Obj.IVT_ResultHistory
                        
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(customer) && !string.IsNullOrEmpty(carrier))
                {
                    // If both customer and carrier are provided, filter based on both
                    workcodes = Obj.IVT_ResultHistory
                        .Where(x => x.customer == customer && x.carrier == carrier && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(customer))
                {
                    // If only customer is provided, filter based on customer
                    workcodes = Obj.IVT_ResultHistory
                        .Where(x => x.customer == customer && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(carrier))
                {
                    // If only carrier is provided, filter based on carrier
                    workcodes = Obj.IVT_ResultHistory
                        .Where(x => x.carrier == carrier && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else
                {
                    // If none of the parameters are provided, return unique workcodes for the logged user
                    workcodes = Obj.IVT_ResultHistory
                        
                        .Select(x => x.work_code)
                        .Distinct();
                }

                var result = workcodes.Select(w => new { value = w, text = w });
                return Json(result, JsonRequestBehavior.AllowGet);
            }
            else // by default return Current Consignment Results
            {
                if (string.IsNullOrEmpty(customer) && string.IsNullOrEmpty(carrier))
                {
                    // If both customer and carrier are null or empty, return unique workcodes for the logged user
                    workcodes = Obj.vIVT_Result
                        
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(customer) && !string.IsNullOrEmpty(carrier))
                {
                    // If both customer and carrier are provided, filter based on both
                    workcodes = Obj.vIVT_Result
                        .Where(x => x.customer == customer && x.carrier == carrier && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(customer))
                {
                    // If only customer is provided, filter based on customer
                    workcodes = Obj.vIVT_Result
                        .Where(x => x.customer == customer && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else if (!string.IsNullOrEmpty(carrier))
                {
                    // If only carrier is provided, filter based on carrier
                    workcodes = Obj.vIVT_Result
                        .Where(x => x.carrier == carrier && !string.IsNullOrEmpty(x.work_code))
                        .Select(x => x.work_code)
                        .Distinct();
                }
                else
                {
                    // If none of the parameters are provided, return unique workcodes for the logged user
                    workcodes = Obj.vIVT_Result
                        
                        .Select(x => x.work_code)
                        .Distinct();
                }

                var result = workcodes.Select(w => new { value = w, text = w });
                return Json(result, JsonRequestBehavior.AllowGet);

            }
        }



        public ActionResult AncillarySummary(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
            , string accountManagerFilter, string customerGroupFilter, string customerFilter, string carrierFilter, string workcodeFilter, string minMargin, string maxMargin)
        {
            try
            {
                ViewBag.DisplayForAdmin = "hidden";
                ViewBag.DisplayForSuperAdmin = "hidden";
                ViewBag.DisplayLinkStatus = "hidden";

                //Selected values for default DL list 
                ViewBag.selectedAccountManager = accountManagerFilter;
                ViewBag.selectedCustomerGroup = customerGroupFilter;
                ViewBag.selectedCustomer = customerFilter;
                ViewBag.selectedCarrier = carrierFilter;
                ViewBag.selectedWorkCode = workcodeFilter;

                string authinticated_user = UserIdentity.CurrentLogin();

                if (authinticated_user == null)
                {
                    authinticated_user = (string)Session["LoginName"];
                }


                string loggeduser = "";
                if (TempData["LoggedAs"] == null && Session["LoggedAs"] == null && loggedas == null)
                {
                    //string olduserconfig =System.Web.HttpContext.Current.WindowsIdentity.GetCurrent().Name.Split('\\').Last();
                    //string userold=WindowsIdentity.GetCurrent().Name;
                    //HttpContext.Current.WindowsIdentity.GetCurrent().Name


                    loggeduser = UserIdentity.CurrentLogin();
                    //loggeduser = "adagostino";
                    Session["Uname"] = loggeduser;

                    //if (Session["NewVisit"] == null)
                    //{
                    //    ivtEntities.spIntegrityReportLog(loggeduser, "User  " + loggedUser.ToUpper() + " has re-visited portal");
                    //    Session["NewVisit"] = "Visited";
                    //}
                }
                else if (Session["LoggedAs"] != null)
                {

                    loggeduser = (string)Session["LoggedAs"];
                }
                else if (loggedas != null)
                {

                    loggeduser = loggedas;
                }
                else if (TempData["LoggedAs"] != null)
                {

                    loggeduser = (string)TempData["LoggedAs"]; ;
                }
                else
                {
                    loggeduser = UserIdentity.CurrentLogin(); // "sallan";

                }


                var userinfo = usersEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
                ViewBag.UserName = loggeduser;

                string loginmessage = string.Empty;

                if (userinfo != null && userinfo.IsActive == 1)
                {
                    if (userinfo.IsFirstLogin == true)
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim();
                        Session["LoginName"] = loggeduser.Trim();
                        return RedirectToAction("ChangePassword");

                    }
                 
                     if ((userinfo.UserType == 1) && userinfo.IsFirstLogin != true)
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "visible";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();

                      

                    }
                     if ((userinfo.UserType == 2) && userinfo.IsFirstLogin != true)
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "visible";
                        ViewBag.DisplayForSuperAdmin = "visible";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();

                        if (message == null)
                        {
                            ViewBag.ViewMessage = "hidden";
                            ViewBag.LastUpdateViewMessage = "visible";
                        }
                        else if (updatestatus >= 1)
                        {
                            TempData["message"] = message;
                            ViewBag.Message = message;
                            ViewBag.messageStatus = updatestatus;
                            ViewBag.LastUpdateViewMessage = "hidden";
                        }
                        else
                        {
                            TempData["message"] = null;
                            ViewBag.messageStatus = 0;
                            ViewBag.Message = null;
                            ViewBag.LastUpdateViewMessage = "visible";
                        }



                        IVTEntities Obj = new IVTEntities();
                        //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                        IQueryable<vAllCurrentAncillarySummary> cons = Obj.vAllCurrentAncillarySummary;


                        var UniqueAccountManager = Obj.vAllCurrentAncillarySummary

                             .Select(x => x.AccountManager)
                             .Distinct().OrderBy(x => x)
                             .ToList();


                        var uniqueCustomers = Obj.vAllCurrentAncillarySummary
                          .Select(x => x.CustomerCode)
                          .Distinct()
                          .OrderBy(x => x)
                          .ToList();


                        var uniqueCarriers = Obj.vAllCurrentAncillarySummary
                        .Select(x => x.Carrier)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                        var uniqueWorkcodes = Obj.vAllCurrentAncillarySummary
                        .Select(x => x.WorkCode)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();


                        var UniqueCustomerGroup = Obj.vAllCurrentAncillarySummary
                      .Select(x => x.FinanceGroup)
                      .Distinct().OrderBy(x => x)
                      .ToList();



                        ViewBag.UniqueAccountManager = new SelectList(UniqueAccountManager);
                        ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                        ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                        ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                        ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);


                        if (!string.IsNullOrEmpty(accountManagerFilter))
                        {
                            cons = cons.Where(x => x.AccountManager.Equals(accountManagerFilter));
                        }

                        if (!string.IsNullOrEmpty(customerGroupFilter))
                        {
                            cons = cons.Where(x =>  x.FinanceGroup.Equals(customerGroupFilter));
                        }


                        if (!string.IsNullOrEmpty(search))
                        {
                            cons = cons.Where(x => x.Carrier.Contains(search) || x.CustomerCode.Contains(search) || x.WorkCode.Contains(search));
                        }

                        // New filters
                        if (!string.IsNullOrEmpty(customerFilter))
                        {
                            cons = cons.Where(x => x.CustomerCode.Contains(customerFilter));
                        }

                        if (!string.IsNullOrEmpty(carrierFilter))
                        {
                            cons = cons.Where(x => x.Carrier.Contains(carrierFilter));
                        }

                        if (!string.IsNullOrEmpty(workcodeFilter))
                        {
                            cons = cons.Where(x => x.WorkCode.Contains(workcodeFilter));
                        }



                        if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && 
                            string.IsNullOrEmpty(workcodeFilter) && string.IsNullOrEmpty(customerGroupFilter) && string.IsNullOrEmpty(accountManagerFilter)
                           )
                        {
                            cons = cons.Take(200);
                        }

                        
                        return View(cons.ToList());
                    }

                }
                else
                {
                    // var checkdisable = ivtEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
                    if (userinfo != null && userinfo.IsActive == 0)
                    {
                        ViewBag.DisplayLinkStatus = "hidden";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        TempData["Utype"] = 0;
                        message = "Your User Has Disabled!, Please Contact Business Admin";
                        return RedirectToAction("NotAuthorized", "Home");

                    }
                    else
                    {
                        ViewBag.DisplayLinkStatus = "hidden";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        TempData["Utype"] = 0;
                        message = "You  have no rights , please contact support.";
                        return RedirectToAction("NotAuthorized", "Home");

                    }

                }
                try
                {
                    if (Session["LoggedAs"] != null)
                    {

                        ViewBag.DisplayForSuperAdmin = "visible";
                    }





                    var DWHEditor = (string)Session["Uname"];
                    IVTEntities Obj = new IVTEntities();
                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    IQueryable<vAllCurrentAncillarySummary> cons = Obj.vAllCurrentAncillarySummary;


                    var UniqueAccountManager = Obj.vAllCurrentAncillarySummary

                         .Select(x => x.AccountManager)
                         .Distinct().OrderBy(x => x)
                         .ToList();


                    var uniqueCustomers = Obj.vAllCurrentAncillarySummary
                      .Select(x => x.CustomerCode)
                      .Distinct()
                      .OrderBy(x => x)
                      .ToList();


                    var uniqueCarriers = Obj.vAllCurrentAncillarySummary
                    .Select(x => x.Carrier)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                    var uniqueWorkcodes = Obj.vAllCurrentAncillarySummary
                    .Select(x => x.WorkCode)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();


                    var UniqueCustomerGroup = Obj.vAllCurrentAncillarySummary
                  .Select(x => x.FinanceGroup)
                  .Distinct().OrderBy(x => x)
                  .ToList();



                    ViewBag.UniqueAccountManager = new SelectList(UniqueAccountManager);
                    ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                    ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                    ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                    ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);


                    if (!string.IsNullOrEmpty(accountManagerFilter))
                    {
                        cons = cons.Where(x => x.AccountManager.Equals(accountManagerFilter));
                    }

                    if (!string.IsNullOrEmpty(customerGroupFilter))
                    {
                        cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter));
                    }


                    if (!string.IsNullOrEmpty(search))
                    {
                        cons = cons.Where(x => x.Carrier.Contains(search) || x.CustomerCode.Contains(search) || x.WorkCode.Contains(search));
                    }

                    // New filters
                    if (!string.IsNullOrEmpty(customerFilter))
                    {
                        cons = cons.Where(x => x.CustomerCode.Contains(customerFilter));
                    }

                    if (!string.IsNullOrEmpty(carrierFilter))
                    {
                        cons = cons.Where(x => x.Carrier.Contains(carrierFilter));
                    }

                    if (!string.IsNullOrEmpty(workcodeFilter))
                    {
                        cons = cons.Where(x => x.WorkCode.Contains(workcodeFilter));
                    }



                    if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) &&
                        string.IsNullOrEmpty(workcodeFilter) && string.IsNullOrEmpty(customerGroupFilter) && string.IsNullOrEmpty(accountManagerFilter)
                       )
                    {
                        cons = cons.Take(200);
                    }

                    return View(cons.ToList());



                  
                }
                catch (Exception)
                {
                    return RedirectToAction("TimeOut", "Home");
                }

            }
            catch (Exception)
            {

                return RedirectToAction("TimeOut", "Home");
            }

        }



        public ActionResult TradingData(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
          ,string accountManagerFilter, string customerGroupFilter, string customerFilter, string carrierFilter, string workcodeFilter, string SupplierRef, string SellNett)
        {
            try
            {

                ViewBag.DisplayForAdmin = "hidden";
                ViewBag.DisplayForSuperAdmin = "hidden";
                ViewBag.DisplayLinkStatus = "hidden";

                ViewBag.Search = search;
                ViewBag.CustomerFilter = customerFilter;
                ViewBag.CarrierFilter = carrierFilter;
                ViewBag.WorkcodeFilter = workcodeFilter;
                ViewBag.SupplierRef = SupplierRef;
                ViewBag.SellNett = SellNett;

                //Selected values for default DL list 

                ViewBag.selectedAccountManager = accountManagerFilter;
                ViewBag.selectedCustomerGroup = customerGroupFilter;
                ViewBag.selectedCustomer = customerFilter;
                ViewBag.selectedCarrier = carrierFilter;
                ViewBag.selectedWorkCode = workcodeFilter;


                string authinticated_user = UserIdentity.CurrentLogin();

                if (authinticated_user == null)
                {
                    authinticated_user = (string)Session["LoginName"];
                }


                string loggeduser = "";
                if (TempData["LoggedAs"] == null && Session["LoggedAs"] == null && loggedas == null)
                {
                    //string olduserconfig =System.Web.HttpContext.Current.WindowsIdentity.GetCurrent().Name.Split('\\').Last();
                    //string userold=WindowsIdentity.GetCurrent().Name;
                    //HttpContext.Current.WindowsIdentity.GetCurrent().Name


                    loggeduser = UserIdentity.CurrentLogin();
                    //loggeduser = "adagostino";
                    Session["Uname"] = loggeduser;

                    //if (Session["NewVisit"] == null)
                    //{
                    //    ivtEntities.spIntegrityReportLog(loggeduser, "User  " + loggedUser.ToUpper() + " has re-visited portal");
                    //    Session["NewVisit"] = "Visited";
                    //}
                }
                else if (Session["LoggedAs"] != null)
                {

                    loggeduser = (string)Session["LoggedAs"];
                }
                else if (loggedas != null)
                {

                    loggeduser = loggedas;
                }
                else if (TempData["LoggedAs"] != null)
                {

                    loggeduser = (string)TempData["LoggedAs"]; ;
                }
                else
                {
                    loggeduser = UserIdentity.CurrentLogin(); // "sallan";

                }


                var userinfo = usersEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
                ViewBag.UserName = loggeduser;

                string loginmessage = string.Empty;

                if (userinfo != null && userinfo.IsActive == 1)
                {
                
                      if ((userinfo.UserType == 1) && userinfo.IsFirstLogin != true)
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "visible";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();

                    

                    }
                    else if ((userinfo.UserType == 2) && userinfo.IsFirstLogin != true)
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "visible";
                        ViewBag.DisplayForSuperAdmin = "visible";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();

                        if (message == null)
                        {
                            ViewBag.ViewMessage = "hidden";
                            ViewBag.LastUpdateViewMessage = "visible";
                        }
                        else if (updatestatus >= 1)
                        {
                            TempData["message"] = message;
                            ViewBag.Message = message;
                            ViewBag.messageStatus = updatestatus;
                            ViewBag.LastUpdateViewMessage = "hidden";
                        }
                        else
                        {
                            TempData["message"] = null;
                            ViewBag.messageStatus = 0;
                            ViewBag.Message = null;
                            ViewBag.LastUpdateViewMessage = "visible";
                        }



                        IVTEntities Obj = new IVTEntities();
                        //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                        IQueryable<vAllCurrentDetails> cons = Obj.vAllCurrentDetails;


                        var UniqueAccountManager = Obj.vAllCurrentDetails

                             .Select(x => x.AccountManager)
                             .Distinct().OrderBy(x => x)
                             .ToList();


                        var UniqueCustomerGroup = Obj.vAllCurrentDetails
                
                     .Select(x => x.FinanceGroup)
                        .Distinct().OrderBy(x => x)
                            .ToList();

                        var uniqueCustomers = Obj.vAllCurrentDetails
                      .Select(x => x.Customer)
                      .Distinct()
                      .OrderBy(x => x)
                      .ToList();


                        var uniqueCarriers = Obj.vAllCurrentDetails
                        .Select(x => x.Carrier)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                        var uniqueWorkcodes = Obj.vAllCurrentDetails
                        .Select(x => x.work_code)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                        ViewBag.UniqueAccountManager = new SelectList(UniqueAccountManager);
                        ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                        ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                        ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                        ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);


                        if (!string.IsNullOrEmpty(customerGroupFilter))
                        {
                            cons = cons.Where(x =>  x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                        }


                        if (!string.IsNullOrEmpty(accountManagerFilter))
                        {
                            cons = cons.Where(x => x.AccountManager.Equals(accountManagerFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(search))
                        {
                            cons = cons.Where(x => x.ConsignmentNote.Contains(search) || x.Carrier.Contains(search) || x.Customer.Contains(search) || x.work_code.Contains(search)).Take(1000);
                        }

                        // New filters
                        if (!string.IsNullOrEmpty(customerFilter))
                        {
                            cons = cons.Where(x => x.Customer.Contains(customerFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(carrierFilter))
                        {
                            cons = cons.Where(x => x.Carrier.Contains(carrierFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(workcodeFilter))
                        {
                            cons = cons.Where(x => x.work_code.Contains(workcodeFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(SupplierRef))
                        {

                            cons = cons.Where(x => x.SupplierRef.Contains(SupplierRef)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(SellNett))
                        {
                            decimal SellNettValue = string.IsNullOrEmpty(SellNett) ? decimal.MaxValue : Convert.ToDecimal(SellNett);
                            cons = cons.Where(x => x.Sell_Total_Nett >= SellNettValue).Take(1000);
                        }

                        if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                            && string.IsNullOrEmpty(SupplierRef) && string.IsNullOrEmpty(SellNett) && string.IsNullOrEmpty(customerGroupFilter) && string.IsNullOrEmpty(accountManagerFilter))
                        {
                            cons = cons.OrderByDescending(x => x.Sell_Total_Nett).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(customerGroupFilter))
                        {
                            cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                        }

                     



                        cons = cons.OrderByDescending(x => x.Sell_Total_Nett).Take(1000);
                        // At this point the query has generated a SQL statement based on the conditions above,
                        // but it will not be executed until the until the next line - i.e. when calling .ToList()
                        return View(cons.ToList().Take(200));

                    }

                }
                else
                {
                    // var checkdisable = ivtEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
                    if (userinfo != null && userinfo.IsActive == 0)
                    {
                        ViewBag.DisplayLinkStatus = "hidden";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        TempData["Utype"] = 0;
                        message = "Your User Has Disabled!, Please Contact Business Admin";
                        return RedirectToAction("NotAuthorized", "Home");

                    }
                    else
                    {
                        ViewBag.DisplayLinkStatus = "hidden";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        TempData["Utype"] = 0;
                        message = "You  have no rights , please contact support.";
                        return RedirectToAction("NotAuthorized", "Home");

                    }

                }
                try
                {
                    if (Session["LoggedAs"] != null)
                    {

                        ViewBag.DisplayForSuperAdmin = "visible";
                    }





                    var DWHEditor = (string)Session["Uname"];
                    IVTEntities Obj = new IVTEntities();
             
                    IQueryable<vAllCurrentDetails> cons = Obj.vAllCurrentDetails;


                    var UniqueAccountManager = Obj.vAllCurrentDetails

                         .Select(x => x.AccountManager)
                         .Distinct().OrderBy(x => x)
                         .ToList();


                    var UniqueCustomerGroup = Obj.vAllCurrentDetails

                 .Select(x => x.FinanceGroup)
                    .Distinct().OrderBy(x => x)
                        .ToList();

                    var uniqueCustomers = Obj.vAllCurrentDetails
                  .Select(x => x.Customer)
                  .Distinct()
                  .OrderBy(x => x)
                  .ToList();


                    var uniqueCarriers = Obj.vAllCurrentDetails
                    .Select(x => x.Carrier)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                    var uniqueWorkcodes = Obj.vAllCurrentDetails
                    .Select(x => x.work_code)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                    ViewBag.UniqueAccountManager = new SelectList(UniqueAccountManager);
                    ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                    ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                    ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                    ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);


                    if (!string.IsNullOrEmpty(customerGroupFilter))
                    {
                        cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                    }


                    if (!string.IsNullOrEmpty(accountManagerFilter))
                    {
                        cons = cons.Where(x => x.AccountManager.Equals(accountManagerFilter)).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(search))
                    {
                        cons = cons.Where(x => x.ConsignmentNote.Contains(search) || x.Carrier.Contains(search) || x.Customer.Contains(search) || x.work_code.Contains(search)).Take(1000);
                    }

                    // New filters
                    if (!string.IsNullOrEmpty(customerFilter))
                    {
                        cons = cons.Where(x => x.Customer.Contains(customerFilter)).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(carrierFilter))
                    {
                        cons = cons.Where(x => x.Carrier.Contains(carrierFilter)).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(workcodeFilter))
                    {
                        cons = cons.Where(x => x.work_code.Contains(workcodeFilter)).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(SupplierRef))
                    {

                        cons = cons.Where(x => x.SupplierRef.Contains(SupplierRef)).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(SellNett))
                    {
                        decimal SellNettValue = string.IsNullOrEmpty(SellNett) ? decimal.MaxValue : Convert.ToDecimal(SellNett);
                        cons = cons.Where(x => x.Sell_Total_Nett >= SellNettValue).Take(1000);
                    }

                    if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                        && string.IsNullOrEmpty(SupplierRef) && string.IsNullOrEmpty(SellNett) && string.IsNullOrEmpty(customerGroupFilter) && string.IsNullOrEmpty(accountManagerFilter))
                    {
                        cons = cons.OrderByDescending(x => x.Sell_Total_Nett).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(customerGroupFilter))
                    {
                        cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                    }


                    cons = cons.OrderByDescending(x => x.Sell_Total_Nett).Take(1000) ;
                    // At this point the query has generated a SQL statement based on the conditions above,
                    // but it will not be executed until the until the next line - i.e. when calling .ToList()
                    return View(cons.ToList().Take(200));
                }
                catch (Exception)
                {
                    return RedirectToAction("TimeOut", "Home");
                }

            }
            catch (Exception)
            {

                return RedirectToAction("TimeOut", "Home");
            }

        }


        public ActionResult HeldConsignment(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
          , string accountManagerFilter, string customerGroupFilter, string customerFilter, string carrierFilter)
        {
            try
            {

                ViewBag.DisplayForAdmin = "hidden";
                ViewBag.DisplayForSuperAdmin = "hidden";
                ViewBag.DisplayLinkStatus = "hidden";

                ViewBag.Search = search;
                ViewBag.CustomerFilter = customerFilter;
                ViewBag.CarrierFilter = carrierFilter;
            

                //Selected values for default DL list 

                ViewBag.selectedAccountManager = accountManagerFilter;
                ViewBag.selectedCustomerGroup = customerGroupFilter;
                ViewBag.selectedCustomer = customerFilter;
                ViewBag.selectedCarrier = carrierFilter;
            


                string authinticated_user = UserIdentity.CurrentLogin();

                if (authinticated_user == null)
                {
                    authinticated_user = (string)Session["LoginName"];
                }


                string loggeduser = "";
                if (TempData["LoggedAs"] == null && Session["LoggedAs"] == null && loggedas == null)
                {
                    //string olduserconfig =System.Web.HttpContext.Current.WindowsIdentity.GetCurrent().Name.Split('\\').Last();
                    //string userold=WindowsIdentity.GetCurrent().Name;
                    //HttpContext.Current.WindowsIdentity.GetCurrent().Name


                    loggeduser = UserIdentity.CurrentLogin();
                    //loggeduser = "adagostino";
                    Session["Uname"] = loggeduser;

                    //if (Session["NewVisit"] == null)
                    //{
                    //    ivtEntities.spIntegrityReportLog(loggeduser, "User  " + loggedUser.ToUpper() + " has re-visited portal");
                    //    Session["NewVisit"] = "Visited";
                    //}
                }
                else if (Session["LoggedAs"] != null)
                {

                    loggeduser = (string)Session["LoggedAs"];
                }
                else if (loggedas != null)
                {

                    loggeduser = loggedas;
                }
                else if (TempData["LoggedAs"] != null)
                {

                    loggeduser = (string)TempData["LoggedAs"]; ;
                }
                else
                {
                    loggeduser = UserIdentity.CurrentLogin(); // "sallan";

                }


                var userinfo = usersEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
                ViewBag.UserName = loggeduser;

                string loginmessage = string.Empty;

                if (userinfo != null && userinfo.IsActive == 1)
                {

                    if ((userinfo.UserType == 1) && userinfo.IsFirstLogin != true)
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "visible";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();



                    }
                    else if ((userinfo.UserType == 2) && userinfo.IsFirstLogin != true)
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "visible";
                        ViewBag.DisplayForSuperAdmin = "visible";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();

                        if (message == null)
                        {
                            ViewBag.ViewMessage = "hidden";
                            ViewBag.LastUpdateViewMessage = "visible";
                        }
                        else if (updatestatus >= 1)
                        {
                            TempData["message"] = message;
                            ViewBag.Message = message;
                            ViewBag.messageStatus = updatestatus;
                            ViewBag.LastUpdateViewMessage = "hidden";
                        }
                        else
                        {
                            TempData["message"] = null;
                            ViewBag.messageStatus = 0;
                            ViewBag.Message = null;
                            ViewBag.LastUpdateViewMessage = "visible";
                        }



                        IVTEntities Obj = new IVTEntities();
                        //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                        IQueryable<vConsignmentForReview> cons = Obj.vConsignmentForReview;


                        var UniqueAccountManager = Obj.vConsignmentForReview

                             .Select(x => x.AccountManager)
                             .Distinct().OrderBy(x => x)
                             .ToList();


                        var UniqueCustomerGroup = Obj.vConsignmentForReview

                     .Select(x => x.FinanceGroup)
                        .Distinct().OrderBy(x => x)
                            .ToList();

                        var uniqueCustomers = Obj.vConsignmentForReview
                      .Select(x => x.Customer)
                      .Distinct()
                      .OrderBy(x => x)
                      .ToList();


                        var uniqueCarriers = Obj.vConsignmentForReview
                        .Select(x => x.Carrier)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                      

                        ViewBag.UniqueAccountManager = new SelectList(UniqueAccountManager);
                        ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                        ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                        ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                     


                        if (!string.IsNullOrEmpty(customerGroupFilter))
                        {
                            cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                        }

                     

                        if (!string.IsNullOrEmpty(accountManagerFilter))
                        {
                            cons = cons.Where(x => x.AccountManager.Equals(accountManagerFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(search))
                        {
                            cons = cons.Where(x => x.Connote.Contains(search) || x.Carrier.Contains(search) || x.Customer.Contains(search) || x.work_code.Contains(search)).Take(1000);
                        }

                        // New filters
                        if (!string.IsNullOrEmpty(customerFilter))
                        {
                            cons = cons.Where(x => x.Customer.Contains(customerFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(carrierFilter))
                        {
                            cons = cons.Where(x => x.Carrier.Contains(carrierFilter)).Take(1000);
                        }

                       

                        if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) 
                             && string.IsNullOrEmpty(customerGroupFilter) && string.IsNullOrEmpty(accountManagerFilter))
                        {
                            cons = cons.OrderByDescending(x => x.InvoiceWeek).Take(1000);
                        }

                        cons = cons.OrderByDescending(x => x.InvoiceWeek).Take(1000);


                        return View(cons.ToList());

                    }

                }
                else
                {
                    // var checkdisable = ivtEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
                    if (userinfo != null && userinfo.IsActive == 0)
                    {
                        ViewBag.DisplayLinkStatus = "hidden";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        TempData["Utype"] = 0;
                        message = "Your User Has Disabled!, Please Contact Business Admin";
                        return RedirectToAction("NotAuthorized", "Home");

                    }
                    else
                    {
                        ViewBag.DisplayLinkStatus = "hidden";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "hidden";
                        TempData["Utype"] = 0;
                        message = "You  have no rights , please contact support.";
                        return RedirectToAction("NotAuthorized", "Home");

                    }

                }
                try
                {
                    if (Session["LoggedAs"] != null)
                    {

                        ViewBag.DisplayForSuperAdmin = "visible";
                    }





                    var DWHEditor = (string)Session["Uname"];
                    IVTEntities Obj = new IVTEntities();
                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    IQueryable<vConsignmentForReview> cons = Obj.vConsignmentForReview;


                    var UniqueAccountManager = Obj.vConsignmentForReview

                         .Select(x => x.AccountManager)
                         .Distinct().OrderBy(x => x)
                         .ToList();


                    var UniqueCustomerGroup = Obj.vConsignmentForReview

                 .Select(x => x.FinanceGroup)
                    .Distinct().OrderBy(x => x)
                        .ToList();

                    var uniqueCustomers = Obj.vConsignmentForReview
                  .Select(x => x.Customer)
                  .Distinct()
                  .OrderBy(x => x)
                  .ToList();


                    var uniqueCarriers = Obj.vConsignmentForReview
                    .Select(x => x.Carrier)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();



                    ViewBag.UniqueAccountManager = new SelectList(UniqueAccountManager);
                    ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                    ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                    ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);



                    if (!string.IsNullOrEmpty(customerGroupFilter))
                    {
                        cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                    }



                    if (!string.IsNullOrEmpty(accountManagerFilter))
                    {
                        cons = cons.Where(x => x.AccountManager.Equals(accountManagerFilter)).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(search))
                    {
                        cons = cons.Where(x => x.Connote.Contains(search) || x.Carrier.Contains(search) || x.Customer.Contains(search) || x.work_code.Contains(search)).Take(1000);
                    }

                    // New filters
                    if (!string.IsNullOrEmpty(customerFilter))
                    {
                        cons = cons.Where(x => x.Customer.Contains(customerFilter)).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(carrierFilter))
                    {
                        cons = cons.Where(x => x.Carrier.Contains(carrierFilter)).Take(1000);
                    }



                    if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter)
                         && string.IsNullOrEmpty(customerGroupFilter) && string.IsNullOrEmpty(accountManagerFilter))
                    {

                        cons = cons.OrderByDescending(x => x.InvoiceWeek).Take(1000);
                    }


                    cons = cons.OrderByDescending(x => x.InvoiceWeek).Take(1000);

                    return View(cons.ToList().Take(1000));
                }
                catch (Exception)
                {
                    return RedirectToAction("TimeOut", "Home");
                }

            }
            catch (Exception)
            {

                return RedirectToAction("TimeOut", "Home");
            }

        }


        [HttpPost]
        public async Task<ActionResult> HeldConsignment(FormCollection formCollection)
        {
            try
            {
                string Justifications = formCollection["Reason"].ToString();
                string loggeduser = GetLoggedUser();
                if (string.IsNullOrEmpty(loggeduser))
                {
                    loggeduser = UserIdentity.CurrentLogin();
                }

                if (formCollection["ID"] == null)
                {
                    return RedirectToAction("HeldConsignment", new
                    {
                        message = "Please Select Connote(s) First!!",
                        updatestatus = 2
                    });
                }

                string[] ids;

                if (string.IsNullOrEmpty(formCollection["ID"]))
                {
                    // Option 1: Initialize ids as an empty array if formCollection["ID"] is null or empty.
                    ids = new string[0];
                }
                else
                {
                    // Option 2: Split the non-null formCollection["ID"] value.
                    ids = formCollection["ID"].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                }

                // Now ids is guaranteed to be initialized, and you can safely use it in your application logic.

                if (ids.Length > 0)
                {
                    var heldIds = ids.Select(id => long.Parse(id)).ToList();
                    using (var dbContext = new IVTEntities())
                    {
                        var heldConnotes = await dbContext.ConsignmentForReview
                                                         .Where(c => heldIds.Contains(c.Id) && !c.IsDeleted)
                                                         .ToListAsync();

                        if (!heldConnotes.Any())
                        {
                            return RedirectToAction("HeldConsignment", new { message = "No connotes found to Release.", updatestatus = 2 });
                        }

                        // Prepare email details
                        string emailBody = "Hi IVT Admins,<br><br>" + loggeduser.ToUpper() + " has <b>Released</b> the following connotes:<br><br>";
                        string activityComment = "Released Details - ";

                        // Initialize the table for the email
                        emailBody += "<table style='border-collapse: collapse; width: 100%;'>" +
                                     "<tr style='background-color: #f2f2f2;'>" +
                                     "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Connote</th>" +
                                     "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Released By</th>" +
                                     "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Customer</th>" +
                                     "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Comment</th>" +
                                     "</tr>";

                        List<ReleasedConsignment> releasedConsignments = new List<ReleasedConsignment>();

                        foreach (var heldConnote in heldConnotes)
                        {
                            // Mark as deleted
                            heldConnote.IsDeleted = true;

                            // Prepare for new entry in ReleasedConsignment
                            ReleasedConsignment connotetoRelease = new ReleasedConsignment
                            {
                                // Assign properties from heldConnote and new values
                                Connote = heldConnote.Connote,
                                jno = heldConnote.jno,
                                cons_date = heldConnote.cons_date,
                                Customer = heldConnote.Customer,
                                Carrier = heldConnote.Carrier,
                                Reason = heldConnote.Reason,
                                Sub_Reason = heldConnote.Sub_Reason,
                                ErrCode = heldConnote.ErrCode,
                                ErrPriority = heldConnote.ErrPriority,
                                ShortErrDesc = heldConnote.ShortErrDesc,
                                ReleasedBy = heldConnote.ReleasedBy,
                                InvoiceWeek = heldConnote.InvoiceWeek,
                                Justification = Justifications,
                                Comment = heldConnote.Comment,
                                ActionedBy = loggeduser,
                                ActionTime = DateTime.Now,
                            };
                            releasedConsignments.Add(connotetoRelease);

                            // Append details for email
                            emailBody += $"<tr>" +
                                         $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{heldConnote.Connote}</td>" +
                                         $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{loggeduser}</td>" +
                                         $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{heldConnote.Customer}</td>" +
                                         $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{Justifications}</td>" +
                                         $"</tr>";
                            activityComment += $"Connote: {heldConnote.Connote}, Released By: {loggeduser}, Customer: {heldConnote.Customer}, Comment: {Justifications}; ";
                        }

                        // Finish preparing email body
                        emailBody += "</table>";
                        emailBody += "<br><br>Please make sure to reflect the changes in the billing system and <b>ALWAYS</b> check the complete list in IVT2.0 System.<br><br>This is an Automated System Notification.<br><br>Invoice Vetting Tool (IVT2.0) <br>";

                        // Insert the new released consignments
                        dbContext.ReleasedConsignment.AddRange(releasedConsignments);
                        await dbContext.SaveChangesAsync();

                        // Send notification and log activity
                        string subject = "IVT Release Notification - " + loggeduser.ToUpper();
                        await SendNotificationAndLogActivity(subject, emailBody, activityComment, loggeduser, "Release Connote");

                        return RedirectToAction("HeldConsignment", new { message = "Selected Connotes have been Released Successfully", updatestatus = 1 });

                    }
                }
                else if (string.IsNullOrEmpty(formCollection["ID"]) && !string.IsNullOrEmpty(formCollection["search"]))
                {
                    return RedirectToAction("HeldConsignment", new { search = formCollection["search"] });
                }
                else
                {
                    return RedirectToAction("HeldConsignment");
                }
            }
            catch (Exception ex)
            {
                return RedirectToAction("HeldConsignment", new
                {
                    message = "Failed to release consignment - Error: " + ex.Message,
                    updatestatus = 2
                });
            }
        }



        private async Task SendNotificationAndLogActivity(string subject, string emailBody, string activityComment, string loggedUser, string activityType)
        {
            // Assuming EmailNotification and Activity Logging Methods are implemented and support async
            var emailTask = EmailNotification.SendNotificationEmailAsync(subject, emailBody, loggedUser);

            // Start logActivityTask without awaiting it immediately
            var logActivityTask = EmailNotification.AM_Activity(activityComment, activityType, loggedUser).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    // Handle exception from logActivityTask
                    // For example, log the error
                    Console.WriteLine($"Logging activity failed: {task.Exception}");
                }
            });

            // Now await only the emailTask because we're handling logActivityTask's failure separately
            await emailTask;

            // Optionally, if you still want to ensure logActivityTask has completed (either succeeded or failed) by this point:
            await logActivityTask;
        }



        public ActionResult ViewReleasedConsignment(long Id)
        {
            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }

            IVTEntities Obj = new IVTEntities();




            var cons = Obj.vIVT_Result.Where(a => a.Id == Id).FirstOrDefault();


            // At this point the query has generated a SQL statement based on the conditions above,
            // but it will not be executed until the until the next line - i.e. when calling .ToList()
            return View(cons);
        }

        public ActionResult ViewHeldConsignment(long Id)
        {
            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }

            IVTEntities Obj = new IVTEntities();




            var cons = Obj.vConsignmentForReview.Where(a => a.Id == Id).FirstOrDefault();

            // Retrieve logs for this consignment
            var consignmentLogs = Obj.Consignment_LogBook
                                    .Where(l => l.JobNumber == cons.jno && !l.IsDeleted)
                                    .OrderByDescending(l => l.ActionTime)
                                    .ToList();

            ViewBag.ConsignmentLogs = consignmentLogs;

           

            // At this point the query has generated a SQL statement based on the conditions above,
            // but it will not be executed until the until the next line - i.e. when calling .ToList()
            return View(cons);
        }


        [HttpPost]
        public async Task<JsonResult> AddLogComment(string JobNumber, string LogText, string LogType)
        {
            try
            {
                string loggeduser = UserIdentity.CurrentLogin();

                using (IVTEntities dbContext = new IVTEntities())
                {
                    // Default LogType to "Comment" if missing
                    if (string.IsNullOrEmpty(LogType))
                    {
                        LogType = "Comment";
                    }

                    // Insert new log entry
                    Consignment_LogBook newLog = new Consignment_LogBook
                    {
                        JobNumber = JobNumber,
                        LogType = LogType,
                        LogText = LogText,
                        CreatedBy = loggeduser,
                        ActionTime = DateTime.Now,
                        IsDeleted = false
                    };

                    dbContext.Consignment_LogBook.Add(newLog);
                    dbContext.SaveChanges();

                    // Send notification
                    // await SendLogNotification(JobNumber, LogType, LogText, loggeduser);

                    return Json(new
                    {
                        success = true,
                        logType = LogType,
                        logText = LogText,
                        createdBy = loggeduser,
                        actionTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        public async Task SendLogNotification(string JobNumber, string LogType, string LogText, string loggedUser)
        {
            using (IVTEntities dbContext = new IVTEntities())
            {
                // Fetch consignment details
                var consignment = dbContext.ConsignmentForReview
                                           .FirstOrDefault(c => c.jno == JobNumber);

                if (consignment != null)
                {
                    // Prepare email details
                    string subject = $"IVT Log Notification - {loggedUser.ToUpper()}";
                    string emailBody = $"Hi Team,<br><br>{loggedUser.ToUpper()} has added a <b>{LogType}</b> log for the following consignment:<br><br>";

                    // Initialize the table for the email
                    emailBody += "<table style='border-collapse: collapse; width: 100%;'>" +
                                 "<tr style='background-color: #f2f2f2;'>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Job Number</th>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Connote</th>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Customer</th>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Log Type</th>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Comment</th>" +
                                 "</tr>";

                    emailBody += $"<tr>" +
                                 $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{JobNumber}</td>" +
                                 $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{consignment.Connote}</td>" +
                                 $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{consignment.Customer}</td>" +
                                 $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{LogType}</td>" +
                                 $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{LogText}</td>" +
                                 $"</tr>";

                    emailBody += "</table>";
                    emailBody += "<br><br>Please check the complete details in IVT2.0 System.<br><br>This is an Automated System Notification.<br><br>Invoice Vetting Tool (IVT2.0) <br>";

                    // Log activity
                    string activityComment = $"Log Added - JobNumber: {JobNumber}, LogType: {LogType}, Comment: {LogText}";

                    await SendNotificationAndLogActivity(subject, emailBody, activityComment, loggedUser, "Log Added");
                }
            }
        }


        public ActionResult HoldConsignment(long Id)
        {
            using (IVTEntities Obj = new IVTEntities())
            {
                // int CommentId = int.Parse(Id);

                var con = Obj.vIVT_Result.Where(a => a.Id == Id).FirstOrDefault();

                return View(con);
            }
        }

        [HttpPost]

        public ActionResult HoldConsignment(vIVT_Result Updatedconnote, FormCollection formCollection)
        {
            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }

            try
            {
                string authinticated_user = (string)TempData["LoginName"];

                if (authinticated_user == null)
                {
                    authinticated_user = (string)Session["LoginName"];
                }

                string customer = "";
                if (Updatedconnote != null)
                {
                    var loggeduser = authinticated_user.Split('\\').Last();


                    DateTime myDateTime = DateTime.Now;
                    // var Currentcon = Obj.vConnoteToRelease.Where(a => a.id == Updatedconnote.id).FirstOrDefault();

                    var heldId = Convert.ToInt64(Updatedconnote.Id);
                    var heldConnote = ivtEntities.vIVT_Result.Where(a => a.Id == heldId).FirstOrDefault();

                    using (IVTEntities Obj = new IVTEntities())
                    {
                        // Copy relevant properties from HeldConsignment to ConsignmentForReview
                        ConsignmentForReview connotetoHold = new ConsignmentForReview
                        {
                            Connote = heldConnote.Connote,
                            jno = heldConnote.jno,
                            Reason = heldConnote.Reason,
                            cons_date = heldConnote.cons_date,
                            Customer = heldConnote.customer,
                            Carrier = heldConnote.carrier,
                            Sub_Reason = heldConnote.Sub_Reason,
                            ErrCode = heldConnote.ErrCode,
                            ErrPriority = heldConnote.ErrPriority,
                            ShortErrDesc = heldConnote.ShortErrDesc,
                            ReleasedBy = heldConnote.HeldBy,
                            InvoiceWeek = heldConnote.CurrentWeek,
                            Comment = formCollection["HoldReason"].ToString(),
                            // Copy other relevant properties from ConsignmentForReview to ReleasedConsignment
                            ActionedBy = authinticated_user.Split('\\').Last(),
                            ActionTime = DateTime.Now,
                        };

                        Obj.ConsignmentForReview.Add(connotetoHold);
                        // Obj.ReleasedConsignment.Remove(heldConnote);


                        Obj.SaveChanges();


                        ViewBag.message = "Connote " + heldConnote.Connote + " has been held";

                        //Log----

                        using (IVTEntities Obj2 = new IVTEntities())
                        {
                            // Copy relevant properties from ConsignmentForReview to ReleasedConsignment

                            Obj2.Database.ExecuteSqlCommand("EXEC [WebIVT].[sp_IVTDashboardRefresh_TotalMeasure]");

                        }
                        // ViewBag.message = "Consignment " + Updatedconnote.Connote + " hold reasons updated";
                        return RedirectToAction("CurrentConsignment", new
                        {

                            message = "Consignment has been held sucessfully",
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("CurrentConsignment", new
                    {

                        message = "Consignment has failed to be held",
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {

                return RedirectToAction("CurrentConsignment", new
                {

                    message = "Consignment has failed to be held",
                    updatestatus = 2
                }); ;
            }

        }






        public ActionResult ReleaseConsignment(long Id)
        {
            using (IVTEntities Obj = new IVTEntities())
            {
                // int CommentId = int.Parse(Id);

                var con = Obj.vConsignmentForReview.Where(a => a.Id == Id).FirstOrDefault();

                return View(con);
            }
        }



        public ActionResult DownloadCSV_AncillarySummary()
        {

            IVTEntities Obj = new IVTEntities();
            IQueryable<AncillarySummary> held_cons = Obj.AncillarySummary;
         
            // Retrieve data for CSV
            var data = held_cons.ToList();


            // Generate CSV content
            var csvContent = "Customer,Carrier,WorkCode,AncillaryDesc,CurrentWeekAvgTotalAncillaries,CurrentWeekAvgSellTotalNett,CurrentWeekAvgBuyTotalNett,CurrentGrossMargin,13WeekAvgTotalAncillaries,13WeekAvgSellTotalNett,13WeekAvgGrossMargin\n";
            foreach (var item in data)
            {
                csvContent += $"{item.CustomerCode},{item.Carrier},{item.WorkCode},{item.AncillaryDesc},{item.CurrentWeekAvgTotalAncillaries},{item.CurrentWeekAvgSellTotalNett},{item.CurrentWeekAvgBuyTotalNett},{item.CurrentGrossMargin},{item.C13WeekAvgTotalAncillaries},{item.C13WeekAvgSellTotalNett},{item.C13WeekAvgBuyTotalNett},{item.AvgGrossMargin}\n";
            }

            // Return CSV file
            return File(new System.Text.UTF8Encoding().GetBytes(csvContent), "text/csv", "IVT_AncillarySummary_Download.csv");
        }


        public ActionResult DownloadCSV_AM_Counts()
        {

            IVTEntities Obj = new IVTEntities();
            var AM_Count = from data in Obj.vDash_AM_Counts
                           group data by new { data.AccountManager, data.LoginName } into groupedData
                           select new AMCountResult
                           {
                               AccountManager = groupedData.Key.AccountManager,
                               LoginName = groupedData.Key.LoginName,
                               TotalExceptions = groupedData.Sum(x => x.TotalExceptions),
                               TotalCurrentWeekTradingCount = groupedData.Sum(x => x.TotalAllConnotes),

                               TotalLastWeekCount = groupedData.Sum(x => x.TotalLastWeekCount),
                               TotalLastWeekTradingCount = groupedData.Sum(x => x.TotalLastWeekTradingCount),
                               AverageLast13WeeksAvgCount = groupedData.Average(x => x.AverageLast13WeeksAvgCount),
                               AverageLast13WeeksTradingCountAVG = groupedData.Average(x => x.AverageLast13WeeksTradingCountAVG),
                               TotalLast13WeeksTradingCount = groupedData.Sum(x => x.TotalLast13WeeksTradingCount),
                               TotalCustomer = groupedData.Sum(x => x.TotalCustomer),
                               TotalCarrier = groupedData.Sum(x => x.TotalCarrier),
                               SellTotalNett = groupedData.Sum(x => x.SellTotalNett),
                               BuyTotalNett = groupedData.Sum(x => x.BuyTotalNett),
                               TotalWeight = groupedData.Sum(x => x.TotalWeight),
                               TotalCubic = groupedData.Sum(x => x.TotalCubic),
                               TotalQuantity = groupedData.Sum(x => x.TotalQuantity),
                               HeldCount = groupedData.Sum(x => x.HeldCount),
                               TotalAncillaries = groupedData.Sum(x => x.TotalAncillaries),
                               ExceptionRate = groupedData.Sum(x => x.ExceptionRate),
                               AncillariesRate = groupedData.Sum(x => x.AncillariesRate),
                           };

            // Convert to list after retrieving the data
            var AM_CountList = AM_Count.AsEnumerable().Select(x => new AMCountResult
            {
                AccountManager = x.AccountManager,
                LoginName = x.LoginName,
                TotalCurrentWeekCount = Convert.ToInt32(x.TotalCurrentWeekCount),
                TotalCurrentWeekTradingCount = Convert.ToInt32(x.TotalCurrentWeekTradingCount),
              
                TotalLastWeekCount = Convert.ToInt32(x.TotalLastWeekCount),
                TotalLastWeekTradingCount = Convert.ToInt32(x.TotalLastWeekTradingCount),
                AverageLast13WeeksAvgCount = Convert.ToInt32(x.AverageLast13WeeksAvgCount),
                TotalLast13WeeksTradingCount = Convert.ToInt32(x.TotalLast13WeeksTradingCount),
                AverageLast13WeeksTradingCountAVG = Convert.ToInt32(x.AverageLast13WeeksTradingCountAVG),
                TotalCustomer = Convert.ToInt32(x.TotalCustomer),
                TotalCarrier = Convert.ToInt32(x.TotalCarrier),
                HeldCount = Convert.ToInt32(x.HeldCount),
                TotalAncillaries = Convert.ToInt32(x.TotalAncillaries),
                ExceptionRate = Convert.ToDecimal(x.ExceptionRate),
                AncillariesRate = Convert.ToDecimal(x.AncillariesRate),
                SellTotalNett = Convert.ToDecimal(x.SellTotalNett),
                BuyTotalNett = Convert.ToDecimal(x.BuyTotalNett),
                TotalWeight = Convert.ToDecimal(x.TotalWeight),
                TotalCubic = Convert.ToDecimal(x.TotalCubic),
                TotalQuantity = Convert.ToDecimal(x.TotalQuantity),
            }).ToList();



            // Generate CSV content
            var csvContent = "AccountManager,LoginName,TotalCurrentWeekCount,TotalCurrentWeekTradingCount,AverageCurrentWeekTradingAVG,TotalLastWeekCount,TotalLastWeekTradingCount,AverageLast13WeeksAvgCount,TotalLast13WeeksTradingCount,AverageLast13WeeksTradingCountAVG,TotalCustomer,TotalCarrier,HeldCount,TotalAncillaries,ExceptionRate,AncillariesRate,SellNett,BuyNett,Weight,Cubic,TotalQuantity\n";
            foreach (var item in AM_CountList)
            {
                csvContent += $"{item.AccountManager},{item.LoginName},{item.TotalCurrentWeekCount},{item.TotalCurrentWeekTradingCount},{item.AverageCurrentWeekTradingAVG},{item.TotalLastWeekCount},{item.TotalLastWeekTradingCount},{item.AverageLast13WeeksAvgCount},{item.TotalLast13WeeksTradingCount},{item.AverageLast13WeeksTradingCountAVG},{item.TotalCustomer},{item.TotalCarrier},{item.HeldCount},{item.TotalAncillaries},{item.ExceptionRate},{item.AncillariesRate},{item.SellTotalNett},{item.BuyTotalNett},{item.TotalWeight},{item.TotalCubic},{item.TotalQuantity}\n";
            }

            // Return CSV file
            return File(new System.Text.UTF8Encoding().GetBytes(csvContent), "text/csv", "IVT_AccountManager_Counts_Download.csv");
        }


        public ActionResult DownloadCSV_CustomerCounts()
        {

            IVTEntities Obj = new IVTEntities();
            var CustomerCount = from data in Obj.vDash_CustomerCounts
                                group data by new { data.FinanceGroup, data.LoginName } into groupedData
                                select new CustomerCountResult
                                {
                                    FinanceGroup = groupedData.Key.FinanceGroup,
                                    LoginName = groupedData.Key.LoginName,
                                    TotalCurrentWeekCount = groupedData.Sum(x => x.CurrentWeekCount),
                                    TotalCurrentWeekTradingCount = groupedData.Sum(x => x.CurrentWeekTradingCount),
                                    AverageCurrentWeekTrading = groupedData.Average(x => x.CurrentWeekTradingCount),
                                    TotalLastWeekCount = groupedData.Sum(x => x.LastWeekCount),
                                    TotalLastWeekTradingCount = groupedData.Sum(x => x.LastWeekTradingCount),
                                    AverageLastWeekTrading = groupedData.Average(x => x.LastWeekTradingCount),
                                    AverageLast13WeeksCount = groupedData.Average(x => x.Last13WeeksAvgCount),
                                    TotalLast13WeeksTradingCount = groupedData.Sum(x => x.Last13WeeksTradingCount),
                                    AverageLast13WeeksTrading = groupedData.Average(x => x.Last13WeeksTradingCountAVG),
                                };

            // Convert to list after retrieving the data
            var CustomerCountList = CustomerCount.AsEnumerable().Select(x => new CustomerCountResult
            {
                FinanceGroup = x.FinanceGroup,
                LoginName = x.LoginName,
                TotalCurrentWeekCount = Convert.ToInt32(x.TotalCurrentWeekCount),
                TotalCurrentWeekTradingCount = Convert.ToInt32(x.TotalCurrentWeekTradingCount),
                AverageCurrentWeekTrading = Convert.ToInt32(x.AverageCurrentWeekTrading),
                TotalLastWeekCount = Convert.ToInt32(x.TotalLastWeekCount),
                TotalLastWeekTradingCount = Convert.ToInt32(x.TotalLastWeekTradingCount),
                AverageLastWeekTrading = Convert.ToInt32(x.AverageLastWeekTrading),
                AverageLast13WeeksCount = Convert.ToInt32(x.AverageLast13WeeksCount),
                TotalLast13WeeksTradingCount = Convert.ToInt32(x.TotalLast13WeeksTradingCount),
                AverageLast13WeeksTrading = Convert.ToInt32(x.AverageLast13WeeksTrading),
            }).ToList();



            // To get the final result as a list
            var Customer_CountList = CustomerCountList.ToList();


            // Generate CSV content
            var csvContent = "Customer,TotalCurrentWeekCount,TotalCurrentWeekTradingCount,TotalLastWeekCount,TotalLastWeekTradingCount,AverageLast13WeeksCount,AverageLast13WeeksTrading\n";
            foreach (var item in Customer_CountList)
            {
                csvContent += $"{item.FinanceGroup},{item.TotalCurrentWeekCount},{item.TotalCurrentWeekTradingCount},{item.TotalLastWeekCount},{item.TotalLastWeekTradingCount},{item.AverageLast13WeeksCount},{item.AverageLast13WeeksTrading}\n";
            }

            // Return CSV file
            return File(new System.Text.UTF8Encoding().GetBytes(csvContent), "text/csv", "IVT_Customer_Counts_Download.csv");
        }


        public ActionResult DownloadCSV_AncillaryDetailsData(string customer, string carrier, string workcode)
        {


            string loggeduser = "";
            if (TempData["LoggedAs"] == null && Session["LoggedAs"] == null)
            {

                loggeduser = UserIdentity.CurrentLogin();

                Session["Uname"] = loggeduser;
            }
            else if (Session["LoggedAs"] != null)
            {

                loggeduser = (string)Session["LoggedAs"];
            }

            else if (TempData["LoggedAs"] != null)
            {

                loggeduser = (string)TempData["LoggedAs"]; ;
            }
            else
            {
                loggeduser = UserIdentity.CurrentLogin(); // "sallan";

            }


            IVTEntities Obj = new IVTEntities();
            var data = Obj.vAllCurrentDetails.Where(a => a.Customer == customer && a.Carrier == carrier && a.work_code == workcode).ToList();


            // Generate CSV content
            var csvContent = "CurrentWeek,CustomerGroup,Customer,CarrierGroup,Carrier,SupplierRef,ConsignmentNote,JobNumber,Cons_Date,Moveit_DespatchDate,Moveit_FreightStatus,from,to,option_code,work_code,quantity,space,lift,weight,chg_kg,pallet,hours,cubic,cus_ref,Sell_Freight_Charges,Sell_Other_Charges,Sell_Fuel_Surch,Sell_Total_Nett,Buy_Freight_Charges,Buy_Other_Charges,Buy_Fuel_Surcharge,Buy_Total_Nett,Nett_Margin,FSL_%_Sell,FSL_%_Buy,FSL_%_Var,ExternalComments\n";

            foreach (var item in data)
            {
                csvContent += $"{item.CurrentWeek},{item.CustomerGroup},{item.Customer},{item.CarrierGroup},{item.Carrier},{item.SupplierRef},{item.ConsignmentNote},{item.JobNumber},{item.Cons_Date},{item.Moveit_DespatchDate},{item.Moveit_FreightStatus},{item.from},{item.to},{item.option_code},{item.work_code},{item.quantity},{item.space},{item.lift},{item.weight},{item.chg_kg},{item.pallet},{item.hours},{item.cubic},{item.cus_ref},{item.Sell_Freight_Charges},{item.Sell_Other_Charges},{item.Sell_Fuel_Surch},{item.Sell_Total_Nett},{item.Buy_Freight_Charges},{item.Buy_Other_Charges},{item.Buy_Fuel_Surcharge},{item.Buy_Total_Nett},{item.Nett_Margin},{item.FSL___Sell},{item.C_FSL_Buy},{item.FSL___Var},{item.ExternalComments}\n";
            }


            // Return CSV file
            return File(new System.Text.UTF8Encoding().GetBytes(csvContent), "text/csv", "IVT_AncillarySummaryDetail_Download.csv");
        }

        public ActionResult DownloadCSV_TradingData()
        {


        


            IVTEntities Obj = new IVTEntities();
            IQueryable<vAllCurrentDetails> held_cons = Obj.vAllCurrentDetails;
           
            // Retrieve data for CSV
            var data = held_cons.ToList();


            var csvContent = new StringBuilder();
            csvContent.AppendLine("CurrentWeek,CustomerGroup,Customer,CarrierGroup,Carrier,SupplierRef,ConsignmentNote,JobNumber,Cons_Date,Moveit_DespatchDate,Moveit_FreightStatus,from,to,option_code,work_code,quantity,space,lift,weight,chg_kg,pallet,hours,cubic,cus_ref,Sell_Freight_Charges,Sell_Other_Charges,Sell_Fuel_Surch,Sell_Total_Nett,Buy_Freight_Charges,Buy_Other_Charges,Buy_Fuel_Surcharge,Buy_Total_Nett,Nett_Margin,FSL_%_Sell,FSL_%_Buy,FSL_%_Var,ExternalComments,Moveit_TotWeightKg,Moveit_TotVolM,Moveit_RateIncSurch,Moveit_LineOfBusiness,Moveit_ActualDeliveryDate");

            //foreach (var item in data)
            //{
            //    if (!string.IsNullOrEmpty(item.ConsignmentNote))
            //    {
            //        csvContent.AppendLine($"{item.CurrentWeek},{item.CustomerGroup},{item.Customer},{item.CarrierGroup},{item.Carrier},{item.SupplierRef},{item.ConsignmentNote},{item.JobNumber},{item.Cons_Date},{item.Moveit_DespatchDate},{item.Moveit_FreightStatus},{item.from},{item.to},{item.option_code},{item.work_code},{item.quantity},{item.space},{item.lift},{item.weight},{item.chg_kg},{item.pallet},{item.hours},{item.cubic},{item.cus_ref},{item.Sell_Freight_Charges},{item.Sell_Other_Charges},{item.Sell_Fuel_Surch},{item.Sell_Total_Nett},{item.Buy_Freight_Charges},{item.Buy_Other_Charges},{item.Buy_Fuel_Surcharge},{item.Buy_Total_Nett},{item.Nett_Margin},{item.FSL___Sell},{item.C_FSL_Buy},{item.FSL___Var},{item.ExternalComments},{item.Moveit_TotWeightKg},{item.Moveit_TotVolM},{item.Moveit_RateIncSurch},{item.Moveit_LineOfBusiness},{item.Moveit_ActualDeliveryDate}");
            //    }
            //}

            //// Return CSV file
            //return File(new System.Text.UTF8Encoding().GetBytes(csvContent.ToString()), "text/csv", "IVT_TradingData_Download_"+loggeduser+ DateTime.Now.ToString("yyyyMMdd_HHmm")+".csv");

            var tradingDataList = new List<CSVTradingData>();
            foreach (var item in data)
            {
                if (!string.IsNullOrEmpty(item.ConsignmentNote))
                {
                    var tradingData = new CSVTradingData
                    {
                        CurrentWeek = item.CurrentWeek.ToString(),
                        CustomerGroup = item.CustomerGroup,
                        Customer = item.Customer,
                        CarrierGroup = item.CarrierGroup,
                        Carrier = item.Carrier,
                        SupplierRef = item.SupplierRef,
                        ConsignmentNote = item.ConsignmentNote,
                        JobNumber = item.JobNumber,
                        Cons_Date = item.Cons_Date.ToString(),
                        Moveit_DespatchDate = item.Moveit_DespatchDate.ToString(),
                        Moveit_FreightStatus = item.Moveit_FreightStatus,
                        from = item.from,
                        to = item.to,
                        option_code = item.option_code,
                        work_code = item.work_code,
                        quantity = item.quantity,
                        space = item.space,
                        lift = item.lift,
                        weight = item.weight,
                        chg_kg = item.chg_kg,
                        pallet = item.pallet,
                        hours = item.hours,
                        cubic = item.cubic,
                        cus_ref = item.cus_ref,
                        Sell_Freight_Charges = item.Sell_Freight_Charges.ToString(),
                        Sell_Other_Charges = item.Sell_Other_Charges.ToString(),
                        Sell_Fuel_Surch = item.Sell_Fuel_Surch.ToString(),
                        Sell_Total_Nett = item.Sell_Total_Nett.ToString(),
                        Buy_Freight_Charges = item.Buy_Freight_Charges.ToString(),
                        Buy_Other_Charges = item.Buy_Other_Charges.ToString(),
                        Buy_Fuel_Surcharge = item.Buy_Fuel_Surcharge.ToString(),
                        Buy_Total_Nett = item.Buy_Total_Nett.ToString(),
                        Nett_Margin = item.Nett_Margin,
                        FSL___Sell = item.FSL___Sell,
                        C_FSL_Buy = item.C_FSL_Buy,
                        FSL___Var = item.FSL___Var.ToString(),
                        ExternalComments = item.ExternalComments,
                        Moveit_TotWeightKg = item.Moveit_TotWeightKg.ToString(),
                        Moveit_TotVolM = item.Moveit_TotVolM.ToString(),
                        Moveit_RateIncSurch = item.Moveit_RateIncSurch.ToString(),
                        Moveit_LineOfBusiness = item.Moveit_LineOfBusiness,
                        Moveit_ActualDeliveryDate = item.Moveit_ActualDeliveryDate.ToString()
                    };
                    tradingDataList.Add(tradingData);
                }
            }

            // Write the list of TradingData objects to a CSV file
            using (var memoryStream = new MemoryStream())
            using (var writer = new StreamWriter(memoryStream))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csv.WriteRecords(tradingDataList);
                writer.Flush();
                memoryStream.Position = 0;

                // Return the CSV file as a FileContentResult
                return File(memoryStream.ToArray(), "text/csv", "IVT_TradingData_Download_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv");
            }
        }
        public ActionResult DownloadCSV_HeldConsignment()
        {
            IVTEntities Obj = new IVTEntities();
            IQueryable<vConsignmentForReview> held_cons = Obj.vConsignmentForReview;

            // Retrieve data for CSV
            var data = held_cons.ToList();

            // Helper function to escape commas and quotes
            string EscapeCSVField(string field)
            {
                if (string.IsNullOrEmpty(field)) return "";
                // Escape double quotes by replacing with two double quotes
                field = field.Replace("\"", "\"\"");
                // If field contains comma, semicolon, or a newline, wrap in quotes
                if (field.Contains(",") || field.Contains(";") || field.Contains("\n"))
                {
                    field = $"\"{field}\"";
                }
                return field;
            }

            // Generate CSV content
            var csvContent = "InvoiceWeek,Connote,Customer,Held Reason,ErrCode,Exception,Carrier,ConnoteDate,Exception Detail\n";
            foreach (var item in data)
            {
                csvContent += $"{EscapeCSVField(item.InvoiceWeek.ToString())}," +
                              $"{EscapeCSVField(item.Connote)}," +
                              $"{EscapeCSVField(item.Customer)}," +
                              $"{EscapeCSVField(item.Comment)}," +
                              $"{EscapeCSVField(item.ErrCode)}," +
                              $"{EscapeCSVField(item.Reason)}," +
                              $"{EscapeCSVField(item.Carrier)}," +
                              $"{EscapeCSVField(item.cons_date.ToString())}," +
                              $"{EscapeCSVField(item.Sub_Reason)}\n";
            }

            // Return CSV file
            return File(new System.Text.UTF8Encoding().GetBytes(csvContent), "text/csv", "IVT_Held_Connote_Download.csv");
        }



        public ActionResult DownloadCSV_IVTResults()
        {


           

            IVTEntities Obj = new IVTEntities();
            IQueryable<vIVT_Result> cons = Obj.vIVT_Result;
            //cons = cons.Where(x => x.HeldBy.Equals(loggeduser));
            // Retrieve data for CSV
            var data = cons.ToList();


            // Generate CSV content
            var csvContent = "InvoiceWeek,Connote,Customer,ErrCode,Exception,Carrier,ConnoteDate,Exception Detail\n";
            foreach (var item in data)
            {
                csvContent += $"{item.CurrentWeek},{item.Connote},{item.customer},{item.ErrCode},{item.Reason},{item.carrier},{item.cons_date},{item.Sub_Reason}\n";
            }

            // Return CSV file
            return File(new System.Text.UTF8Encoding().GetBytes(csvContent), "text/csv", "IVT_Exceptions_Download.csv");
        }


        // Controller Action


   
        [HttpPost]
        public async Task<ActionResult> ReleaseConsignment(ConsignmentForReview connotetoRelease, FormCollection formCollection)
        {
            try
            {
                string loggeduser = GetLoggedUser();
                if (string.IsNullOrEmpty(loggeduser))
                {
                    loggeduser = UserIdentity.CurrentLogin();
                }

                var heldId = Convert.ToInt64(connotetoRelease.Id);
                var heldConnote = ivtEntities.ConsignmentForReview.Where(a => a.Id == heldId).FirstOrDefault();

                if (connotetoRelease != null && !string.IsNullOrEmpty(loggeduser))
                {
                    heldConnote.IsDeleted = true;

                    using (IVTEntities Obj = new IVTEntities())
                    {
                        ReleasedConsignment releaseObj = new ReleasedConsignment
                        {
                            Connote = heldConnote.Connote,
                            jno = heldConnote.jno,
                            Reason = heldConnote.Reason,
                            cons_date = heldConnote.cons_date,
                            Customer = heldConnote.Customer,
                            Carrier = heldConnote.Carrier,
                            Sub_Reason = heldConnote.Sub_Reason,
                            ErrCode = heldConnote.ErrCode,
                            ErrPriority = heldConnote.ErrPriority,
                            ShortErrDesc = heldConnote.ShortErrDesc,
                            ReleasedBy = heldConnote.ReleasedBy,
                            InvoiceWeek = heldConnote.InvoiceWeek,
                            Justification = formCollection["Justification"].ToString(),
                            Comment = heldConnote.Comment,
                            // Copy other relevant properties from ConsignmentForReview to ReleasedConsignment
                            ActionedBy = loggeduser,
                            ActionTime = DateTime.Now,
                            // Copy other relevant properties from ConsignmentForReview to ReleasedConsignment
                        };

                        Obj.ReleasedConsignment.Add(releaseObj);


                        await Task.Run(() => Obj.Database.ExecuteSqlCommand("EXEC [WebIVT].[sp_IVTDashboardRefresh_TotalMeasure]"));

                        Obj.SaveChanges();
                        //-----------Send Notification------------------------------------
                        var ActionedBy = loggeduser.ToUpper();

                        string body = "Hi IVT Admins,<br><br>";
                        body += ActionedBy + " has <b>RELEASED</b> Connotes <br><br>";


                        if (releaseObj != null)
                        {
                            body += "The following connote has been <b>RELEASED</b>:<br><br>";
                            var connote = heldConnote.Connote;
                            var AccountManager = ActionedBy;
                            var Customer = heldConnote.Customer;
                            var Justification = formCollection["Justification"].ToString();

                            body += $"Connote: {connote},  ReleasedBy: {AccountManager},  Customer: {Customer},  Justification: {Justification}<br>";
                        }



                        body += "<br><br>Please make sure to reflect the changes on billing system and <b>ALWAYS</b> check  the complete list in IVT2.0 System.<br><br><br><br><br><br><br> ";

                        body += "This is an Automated System Notification.<br><br> ";
                        body += "Invoice Vetting Tool (IVT2.0) <br> ";

                        await EmailNotification.SendNotificationEmailAsync("IVT Release Notification - " + ActionedBy, body, loggeduser);

                        ViewBag.message = "Connote " + connotetoRelease.Connote + " has been Released";
                        return RedirectToAction("HeldConsignment", new
                        {
                            message = "Connote " + connotetoRelease.Connote + " has been Released",
                            updatestatus = 1
                        });
                    }
                }
                else
                {
                    return RedirectToAction("HeldConsignment", new
                    {
                        message = "Failed to Release consignment - Invalid user or already Released ",
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {
                return RedirectToAction("HeldConsignment", new
                {
                    message = "Failed to Release consignment - Error: " + ex.Message,
                    updatestatus = 2
                });
            }
        }



        public ActionResult ViewAncillarySummaryDetails(string customer, string carrier, string workcode, string message,int? updatestatus)
        {
            try
            {
                ViewBag.DisplayForAdmin = "hidden";
                ViewBag.DisplayForSuperAdmin = "hidden";
                ViewBag.DisplayLinkStatus = "hidden";

                string loggeduser = "";
                if (TempData["LoggedAs"] == null && Session["LoggedAs"] == null )
                {
         
                    loggeduser = UserIdentity.CurrentLogin();
                  
                    Session["Uname"] = loggeduser;

                }
                else if (Session["LoggedAs"] != null)
                {

                    loggeduser = (string)Session["LoggedAs"];
                }
              
                else if (TempData["LoggedAs"] != null)
                {

                    loggeduser = (string)TempData["LoggedAs"]; ;
                }
                else
                {
                    loggeduser = UserIdentity.CurrentLogin(); // "sallan";

                }


                var userinfo = usersEntities.vIVTUser.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
                ViewBag.UserName = loggeduser;

                if ((userinfo.UserType == 1))
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.UserName;
                    Session["Uname"] = userinfo.UserName;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim().ToUpper();
                    Session["LoginName"] = loggeduser.Trim().ToUpper();

                }
                else if  ((userinfo.UserType == 2))
                    {
                        ViewBag.DisplayLinkStatus = "visible";
                        ViewBag.DisplayForAdmin = "hidden";
                        ViewBag.DisplayForSuperAdmin = "visible";
                        FormsAuthentication.SetAuthCookie(loggeduser, true);
                        TempData["UserName"] = userinfo.UserName;
                        Session["Uname"] = userinfo.UserName;
                        Session["Utype"] = userinfo.UserType;
                        TempData["Utype"] = userinfo.UserType;
                        TempData["LoginName"] = loggeduser.Trim().ToUpper();
                        Session["LoginName"] = loggeduser.Trim().ToUpper();

                    }

                if (message == null)
                {
                    ViewBag.ViewMessage = "hidden";
                    ViewBag.LastUpdateViewMessage = "visible";
                }
                else if (updatestatus >= 1)
                {
                    TempData["message"] = message;
                    ViewBag.Message = message;
                    ViewBag.messageStatus = updatestatus;
                    ViewBag.LastUpdateViewMessage = "hidden";
                }
                else
                {
                    TempData["message"] = null;
                    ViewBag.messageStatus = 0;
                    ViewBag.Message = null;
                    ViewBag.LastUpdateViewMessage = "visible";
                }

                ViewBag.LoggedUser = loggeduser;
                ViewBag.LoggedUserStatus = "visible";

                if (Session["LoggedAs"] != null)
                {

                    ViewBag.DisplayForSuperAdmin = "visible";
                }

                ViewBag.Customer = customer;
                ViewBag.Carrier = carrier;
                ViewBag.Workcode = workcode;

                // Limit the SQL query before materializing results so large groups stay bounded.
                using (var context = new IVTEntities())
                {
                    var records = context.vAllCurrentDetails
                        .AsNoTracking()
                        .Where(a => a.Customer == customer && a.Carrier == carrier && a.work_code == workcode)
                        .OrderByDescending(a => a.Cons_Date)
                        .ThenByDescending(a => a.PrimaryKeyField)
                        .Take(50)
                        .ToList();

                    return View(records);
                }
            }
            catch (Exception ex)
            {

                return RedirectToAction("TimeOut", "Home");
            }

        }


        [HttpPost]
        public ActionResult ViewAncillarySummaryDetails(FormCollection formCollection)
        {
            string encodedValue = HttpUtility.UrlEncode(formCollection["MyKey"]); // Replace "YourKey" with the actual key
            try
            {
                string customer = "";
                string carrier = "";
                string workcode = "";

                if (formCollection["ID"] == null && (formCollection["search"].ToString() == ""))
                {
                    return RedirectToAction("ViewAncillarySummaryDetails", new
                    {
                        message = "Please Select Connote(s) First!!",
                        updatestatus = 2
                    });
                }
                else if (formCollection["ID"] != null)
                {
                    string[] ids = formCollection["ID"].Split(new char[] { ',' });

                    foreach (string id in ids)
                    {
                        try
                        {
                            string authenticatedUser = (string)TempData["LoginName"];

                            if (authenticatedUser == null)
                            {
                                authenticatedUser = (string)Session["LoginName"];
                            }

                            if (id != null)
                            {
                                var heldId = Convert.ToInt64(id);
                                var heldConnote = ivtEntities.vAllCurrentDetails.Where(a => a.PrimaryKeyField == heldId).FirstOrDefault();
                                customer = heldConnote.Customer;
                                carrier = heldConnote.Carrier;
                                workcode = heldConnote.work_code;

                                if (heldConnote != null)
                                {
                                    using (IVTEntities Obj = new IVTEntities())
                                    {
                                        // Copy relevant properties from HeldConsignment to ConsignmentForReview
                                        ConsignmentForReview connotetoHold = new ConsignmentForReview
                                        {
                                            Connote = heldConnote.ConsignmentNote,
                                            jno = heldConnote.JobNumber,
                                            Reason = "Admin Action - AM Custom Held From Ancillary Detail Data",
                                            cons_date = heldConnote.Cons_Date.ToString(),
                                            Customer = heldConnote.Customer,
                                            Carrier = heldConnote.Carrier,
                                            Sub_Reason = formCollection["Reason"].ToString(),
                                            ErrCode = "9999",
                                            ErrPriority = "P9999",
                                            ShortErrDesc = "Custom Rule",
                                            ReleasedBy = authenticatedUser.Split('\\').Last(),
                                            InvoiceWeek = heldConnote.CurrentWeek,
                                            Comment = formCollection["Reason"].ToString(),
                                            // Copy other relevant properties from ConsignmentForReview to ReleasedConsignment
                                            ActionedBy = authenticatedUser.Split('\\').Last(),
                                            ActionTime = DateTime.Now,
                                        };
                                        //Obj.sp_IVT_Portal_Log(authenticatedUser.Split('\\').Last(), "Connote:" + connotetoHold.Connote + " has been held");
                                        Obj.ConsignmentForReview.Add(connotetoHold);
                                        // Obj.ReleasedConsignment.Remove(heldConnote);
                                        Obj.SaveChanges();

                                        ViewBag.message = "Connote " + heldConnote.ConsignmentNote + " has been held";
                                    }

                                }// If not null
                            }
                            else
                            {
                                return RedirectToAction("ViewAncillarySummaryDetails", new
                                {
                                    message = "Your Session Has Expired!!! - Failed to hold consignment - Please Try Again",
                                    updatestatus = 2
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            return RedirectToAction("ViewAncillarySummaryDetails", new
                            {
                                message = "Your Session Has Expired!!! - Failed to hold consignment - Please Try Again - Error: " + ex.Message,
                                updatestatus = 2
                            });
                        }
                    }
                    using (IVTEntities Obj = new IVTEntities())
                    {
                        // Copy relevant properties from ConsignmentForReview to ReleasedConsignment

                        Obj.Database.ExecuteSqlCommand("EXEC [WebIVT].[sp_IVTDashboardRefresh_TotalMeasure]");

                    }
                    return RedirectToAction("ViewAncillarySummaryDetails", new
                    {
                        message = "Selected connotes have been held successfully",
                        updatestatus = 1,
                        customer = customer,
                        carrier = carrier,
                        workcode = workcode
                    });
                }
                // Rest of the code remains unchanged
                else if (formCollection["ID"] == null && (formCollection["search"] != null))
                {
                    return RedirectToAction("ViewAncillarySummaryDetails", new
                    {
                        search = formCollection["search"].ToString()


                    });
                }
                else
                {
                    return RedirectToAction("ViewAncillarySummaryDetails");
                }
            }
            catch (Exception ex)
            {
                return RedirectToAction("ViewAncillarySummaryDetails", new
                {
                    message = "Your Session Has Expired!!! - Failed to hold consignment - Please Try Again - Error: " + ex.Message,
                    updatestatus = 2
                });
            }
        }


        //--------END of IVT-----------------------------
    }
}




public static class MyGlobalVariables
{
    // "Logged in as another user" flag. Was a static field shared by every user on the server, so one admin's
    // "view as" state leaked to everyone else; it is now stored per user in the ASP.NET session.
    private const string SessionKey = "MyGlobalVariables.loggedas";

    public static int loggedas
    {
        get
        {
            var session = System.Web.HttpContext.Current?.Session;
            return session?[SessionKey] as int? ?? 0;
        }
        set
        {
            var session = System.Web.HttpContext.Current?.Session;
            if (session != null) session[SessionKey] = value;
        }
    }
}
