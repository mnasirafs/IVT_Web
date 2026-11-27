using AFSDataGateway.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;

namespace AFSDataGateway.Controllers
{
    public class CustomerAtRiskController : Controller
    {
        // GET: CustomerAtRisk
        AFSXchange1Entities usersEntities = new AFSXchange1Entities();

        public ActionResult Index(string message,int? updatestatus)
        {

            ViewBag.DisplayForAdmin = "hidden";
            ViewBag.DisplayForSuperAdmin = "hidden";
            ViewBag.DisplayLinkStatus = "hidden";
           


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
            if (TempData["LoggedAs"] == null && Session["LoggedAs"]==null)
            {
                //string olduserconfig =System.Web.HttpContext.Current.User.Identity.Name.Split('\\').Last();
                //string userold=WindowsIdentity.GetCurrent().Name;
                //HttpContext.Current.HttpContext.User.Identity.Name
                var usernme = HttpContext.User.Identity.Name ; // "sallan";
                ViewBag.testuser = usernme;
                loggeduser =  HttpContext.User.Identity.Name.Split('\\').Last(); // "sallan";
                Session["Uname"] = loggeduser;

                if (Session["NewVisit"] == null)
                {
                    usersEntities.spAmPortalActivityLog(loggeduser, "User  " + loggeduser + " has re-visited portal");
                    Session["NewVisit"] = "Visited";
                }
            }
            else if(TempData["LoggedAs"] !=null)
            {
                loggeduser = HttpContext.User.Identity.Name.Split('\\').Last(); // "sallan";
                loggeduser = (string)TempData["LoggedAs"];
            }
            else
            {
                loggeduser = HttpContext.User.Identity.Name.Split('\\').Last(); // "sallan";
                loggeduser = (string)Session["LoggedAs"];
            }
           
          
            var userinfo = usersEntities.vAccountManagerLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
            ViewBag.UserName = loggeduser;
            
            string loginmessage = string.Empty;

            if (userinfo != null && userinfo.IsActive == 1)
            {
                if (userinfo.IsFirstLogin == true)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    TempData["UserName"] = userinfo.AccountManager;
                    Session["Uname"] = userinfo.AccountManager;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim();
                    Session["LoginName"] = loggeduser.Trim();
                    return RedirectToAction("ChangePassword");

                }
                else if (userinfo.UserType == 0 && userinfo.IsFirstLogin != true && userinfo.IsActive == 1)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(loggeduser,true);
                    TempData["UserName"] = userinfo.AccountManager;
                    Session["Uname"] = userinfo.AccountManager;
                    Session["Utype"] = userinfo.UserType;
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

                    try
                    {
                        AFSXchange1Entities Obj = new AFSXchange1Entities();
                        var lastupdate = Obj.vAccountCommentUpdateTime.FirstOrDefault();
                        TempData["LastUpdate"] = "Last Update was at " + String.Format("{0:dd/MM/yyyy} 3:00 PM", lastupdate.LastUpdate) + " , Next load will be at " + String.Format("{0:dd/MM/yyyy} 3:00 PM", lastupdate.NextLoad);
                    }
                    catch (Exception ex)
                    {

                        TempData["message"] = "Error Occured";
                        ViewBag.Message = "Error Occured";
                        ViewBag.messageStatus = 2;
                    }

                    return View(Get_AllAccountComments());
                }
                else if ((userinfo.UserType == 1) && userinfo.IsFirstLogin != true)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.AccountManager;
                    Session["Uname"] = userinfo.AccountManager;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim().ToUpper();
                    Session["LoginName"] = loggeduser.Trim().ToUpper();

                    return RedirectToAction("AccountManagerList", "Home");

                }
                else if ((userinfo.UserType == 2) && userinfo.IsFirstLogin != true)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "visible";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.AccountManager;
                    Session["Uname"] = userinfo.AccountManager;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim().ToUpper();
                    Session["LoginName"] = loggeduser.Trim().ToUpper();

                    return RedirectToAction("AccountManagerList", "Home");

                }

            }
            else
            {
                // var checkdisable = usersEntities.vAccountManagerLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
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

            return View(Get_AllAccountComments());



        }

      
        public ActionResult SearchConsignments(string search, DateTime? startdate, DateTime? enddate , long? Customer_id)
        {
            ViewBag.Datetime = DateTime.UtcNow;
            if(startdate!=null)
            {
                ViewBag.startdate = Convert.ToDateTime(startdate).Date;
                startdate = Convert.ToDateTime(startdate).Date;
            }
            else
            {
                ViewBag.startdate = ViewBag.Datetime;
            }
            if (enddate != null)
            {
                ViewBag.enddate = Convert.ToDateTime(enddate).Date;
                enddate = Convert.ToDateTime(enddate).Date;
            }
            else
            {
                ViewBag.enddate = ViewBag.Datetime;
            }

           
           var  accountmanager = (string)Session["Uname"];
            AFSXchange1Entities Obj = new AFSXchange1Entities();
            var delg = Obj.vAccountManagerDelegate.Where(k => k.Delegate_AM == accountmanager).Select(a => a.Original_AM).ToList();

            IQueryable<vConsignments> cons = Obj.vConsignments;

            if (search != null && search !=""  && startdate!=null && enddate!=null)
            {
                cons = cons.Where(x =>  x.ConsignmentNo.Contains(search) && x.DespatchDate >= startdate.Value && x.DespatchDate <= enddate.Value && (x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)));
            }
          else if (search != null && search != "")
            {
                cons = cons.Where(x =>  x.ConsignmentNo.Contains(search) && x.AccountManager.ToUpper()==accountmanager.ToUpper()) ;
            }

            else if (startdate.HasValue && enddate.HasValue && Customer_id!=null)
            {
                cons = cons.Where(x => x.DespatchDate >= startdate.Value && x.DespatchDate <= enddate.Value && x.Customer_id==Customer_id && (x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)));
            }

            else if (startdate.HasValue && enddate.HasValue && Customer_id == null)
            {
                cons = cons.Where(x => x.DespatchDate >= startdate.Value && x.DespatchDate <= enddate.Value && (x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)));
            }
            else if (startdate.HasValue)
            {
                cons = cons.Where(x => x.DespatchDate >= startdate.Value && (x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)));
            }
            else if (enddate.HasValue)
            {
                cons = cons.Where(x => x.DespatchDate <= enddate.Value && (x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)));
            }
            else
            {
                cons = cons.Where(x => x.AccountManager.ToUpper() == accountmanager.ToUpper()).OrderByDescending(x=>x.cId).Take(100);
            }
            // At this point the query has generated a SQL statement based on the conditions above,
            // but it will not be executed until the until the next line - i.e. when calling .ToList()
            return View(cons.ToList());
        }

        public List<spGetCustomerCounts_Result> GetGounts(DateTime? startdate, DateTime? enddate, bool initilize)
        {
            var accountmanager = (string)Session["Uname"];
            AFSXchange1Entities Obj = new AFSXchange1Entities();

            if (!initilize)
            {
               
                var delg = Obj.vAccountManagerDelegate.Where(k => k.Delegate_AM == accountmanager).Select(a => a.Original_AM).ToList();
                try
                {

                    var resultSet = from v in Obj.spGetCustomerCounts(DateTime.UtcNow, DateTime.UtcNow)
                    where v.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(v.AccountManager)
                    select v;


                    if (startdate != null && enddate != null)
                    {
                   

                        resultSet = from v in Obj.spGetCustomerCounts(startdate, enddate)
                                    where v.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(v.AccountManager)
                                    orderby v.CNT descending
                                    select v;

                    }
                    else if (startdate != null && enddate == null)
                    {
                        //resultSet = (from p in Obj.spGetCustomerCounts(startdate, Convert.ToDateTime("09/09/9999")).Where(x => x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)).ToList<vConsignments>();

                        resultSet = from v in  Obj.spGetCustomerCounts(startdate, Convert.ToDateTime("09/09/9999"))
                                                   where v.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(v.AccountManager)
                                    orderby v.CNT descending
                                    select v;


                    }
                    else if (startdate == null && enddate != null)
                    {
                        resultSet = from v in Obj.spGetCustomerCounts(Convert.ToDateTime("09/09/1900"), enddate)
                                    where v.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(v.AccountManager)
                                    orderby v.CNT descending
                                    select v;


                    }

                    return resultSet.ToList();
                }
                catch (Exception ex)
                {
                     RedirectToAction("TimeOut", "Home");
                    throw ex;
                }
            }
            else
            {
                var resultSet = from v in Obj.spGetCustomerCounts(DateTime.UtcNow, DateTime.UtcNow)
                                where v.AccountManager.ToUpper() == accountmanager.ToUpper() 
                                select v;
                return resultSet.ToList();

            }

         
            
        }
        public ActionResult CustomerStat( DateTime? startdate, DateTime? enddate)
        {
            if (Session["Uname"] == null || Session["Uname"].ToString().Length==0)
            {
                ViewBag.DisplayLinkStatus = "hidden";
                ViewBag.DisplayForAdmin = "hidden";
                TempData["Utype"] = 0;
               
                return RedirectToAction("NotAuthorized", "Home");

            }
            else
            {
                ViewBag.Datetime = DateTime.UtcNow;
                if (startdate != null)
                {
                    ViewBag.startdate = startdate;
                }
                else
                {
                    ViewBag.startdate = DateTime.Now.Date.AddDays(-1);
                    startdate = DateTime.Now.Date.AddDays(-1);
                }
                if (enddate != null)
                {
                    ViewBag.enddate = enddate;
                }
                else
                {
                    ViewBag.enddate = DateTime.Now.Date;
                    enddate = DateTime.Now.Date;
                }


                var accountmanager = (string)Session["Uname"];
                AFSXchange1Entities Obj = new AFSXchange1Entities();

                IQueryable<vConsignments> cons = Obj.vConsignments;

                var consCount=GetGounts(null, null,true);

                if (startdate.HasValue && enddate.HasValue)
                {
                    // cons = cons.Where(x => x.DespatchDate > startdate.Value && x.DespatchDate < enddate.Value && (x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)));
                    consCount = GetGounts(startdate, enddate,false);
                }
                else if (startdate.HasValue)
                {
                    consCount = GetGounts(startdate, null, false);

                }
                else if (enddate.HasValue)
                {
                    consCount = GetGounts(null, enddate, false);
                }
                else
                {
                    consCount = GetGounts(null, null, false);
                }
                // At this point the query has generated a SQL statement based on the conditions above,
                // but it will not be executed until the until the next line - i.e. when calling .ToList()
                return View(consCount.ToList());

            }

         
        }


        public ActionResult ViewConsignment(long Id)
        {
           
            AFSXchange1Entities Obj = new AFSXchange1Entities();

           
           

                var cons = Obj.vConsignments.Where(a => a.cId == Id).FirstOrDefault();
           

            // At this point the query has generated a SQL statement based on the conditions above,
            // but it will not be executed until the until the next line - i.e. when calling .ToList()
            return View(cons);
        }

        //public ActionResult SearchConsignments(IQueryable<vConsignments> cons=null)
        //{
        //    if (cons == null)
        //    {
        //        var accountmanager = (string)Session["Uname"];
        //        cons = cons.Where(x => x.AccountManager.ToUpper() == accountmanager.ToUpper()).Take(100);
        //    }
        //    // but it will not be executed until the until the next line - i.e. when calling .ToList()
        //    return View(cons.ToList());
        //}

        public ActionResult SearchCommentHistory(string search, DateTime? startdate, DateTime? enddate, string Customer)
        {
            ViewBag.Datetime = DateTime.Now.Date;
            if (startdate != null)
            {
                ViewBag.startdate = Convert.ToDateTime(startdate).Date;
                startdate = Convert.ToDateTime(startdate).Date;
            }
            else
            {
                DateTime initialdate= DateTime.Now.Date.AddDays(-30); 
                ViewBag.startdate = initialdate;
            }
            if (enddate != null)
            {
                ViewBag.enddate = Convert.ToDateTime(enddate).Date;
                enddate = Convert.ToDateTime(enddate).Date;
            }
            else
            {
                ViewBag.enddate = ViewBag.Datetime;
            }

            string username = (string)Session["Uname"];

         

            var accountmanager = (string)Session["Uname"];
            AFSXchange1Entities Obj = new AFSXchange1Entities();
            var delg = Obj.vAccountManagerDelegate.Where(k => k.Delegate_AM == username).Select(a => a.Original_AM).ToList();

            IQueryable<vAccountComments> comments = Obj.vAccountComments;

            if (search != null && search != "" && startdate != null && enddate != null)
            {
                comments = comments.Where(x => x.CustomerGroup.Contains(search) && x.InvoiceWeek >= startdate.Value && x.InvoiceWeek <= enddate.Value && (x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)) );
            }
            else if (search != null && search != "")
            {
                comments = comments.Where(x => x.CustomerGroup.Contains(search) && (x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)));
            }

            else if (startdate.HasValue && enddate.HasValue && Customer != null)
            {
                comments = comments.Where(x => x.InvoiceWeek >= startdate.Value && x.InvoiceWeek <= enddate.Value && x.CustomerGroup == Customer && (x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)));
            }

            else if (startdate.HasValue && enddate.HasValue && Customer == null)
            {
                comments = comments.Where(x => x.InvoiceWeek >= startdate.Value && x.InvoiceWeek <= enddate.Value && (x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)));
            }
            else if (startdate.HasValue)
            {
                comments = comments.Where(x => x.InvoiceWeek >= startdate.Value && (x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)));
            }
            else if (enddate.HasValue)
            {
                comments = comments.Where(x => x.InvoiceWeek <= enddate.Value && (x.AccountManager.ToUpper() == accountmanager.ToUpper() || delg.Contains(x.AccountManager)));
            }
            else
            {
                comments = comments.Where(x => x.AccountManager.ToUpper() == accountmanager.ToUpper()).OrderByDescending(x => x.Id).Take(100);
            }
            // At this point the query has generated a SQL statement based on the conditions above,
            // but it will not be executed until the until the next line - i.e. when calling .ToList()
            return View(comments.ToList());
        }

        

                 public ActionResult ViewCommentHistory(long Id)
        {
            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
                // int CommentId = int.Parse(Id);

                var comment = Obj.vAccountComments.Where(a => a.Id == Id).FirstOrDefault();

                var RL = comment.RiskLevel;
                var CS = comment.ContractStatus;

                if (RL == null)
                {
                    RL = "Null";
                }
                else
                {
                    RL = comment.RiskLevel.Trim().ToUpper();
                }
                if (CS == null)
                {
                    CS = "Null";
                }
                else
                {
                    CS = comment.ContractStatus.Trim().ToUpper();
                }

                return View(comment);
            }
        }
        public ActionResult ViewAll()
        {
            return View(Get_AllAccountComments());
        }

        /// <summary>  
        ///   
        /// Get All AccountComments_pre  
        /// </summary>  
        /// <returns></returns>  
        public IEnumerable<vAccountCommentsPre_All> Get_AllAccountComments()
        {
            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
                string username= (string)Session["Uname"];

                var delg = Obj.vAccountManagerDelegate.Where(k => k.Delegate_AM == username).Select(a => a.Original_AM).ToList();


                if (username!=null &&  (int)Session["Utype"] != 1)
                {
                    return Obj.vAccountCommentsPre_All.Where((a => a.AccountManager == username || delg.Contains(a.AccountManager) )).ToList<vAccountCommentsPre_All>();
                }
                else if (username != null &&  (int)Session["Utype"] == 1)
                {
                    return Obj.vAccountCommentsPre_All.ToList<vAccountCommentsPre_All>();
                }
                else 
                {
                    return Obj.vAccountCommentsPre_All.Where(a=>a.AccountManager=="Test222").ToList<vAccountCommentsPre_All>();
                }

            }
        }
        /// <summary>  
        /// Get AccountComments_pre With Id  
        /// </summary>  
        /// <param name="Id"></param>  
        /// <returns></returns>  
        public JsonResult Get_AccountCommentById(string Id)
        {
            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
                int CommentId = int.Parse(Id);
                return Json(Obj.vAccountCommentsPre_All.Find(CommentId), JsonRequestBehavior.AllowGet);
            }
        }
        
        public ActionResult Edit(long Id)
        {
            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
               // int CommentId = int.Parse(Id);

                var comment = Obj.vAccountComments_pre.Where(a => a.Id ==Id).FirstOrDefault();

                var RL = comment.RiskLevel;
                var CS = comment.ContractStatus;

                if (RL==null)
                {
                    RL = "Null";
                }
                else
                {
                    RL= comment.RiskLevel.Trim().ToUpper(); 
                }
                if (CS == null)
                {
                    CS = "Null";
                }
                else
                {
                    CS = comment.ContractStatus.Trim().ToUpper();
                }

                try
                {
                    ViewBag.RiskLevels =
           new SelectList(Obj.CustomerRiskLevel.ToList().
           Select(x => new { Id = x.Id, RiskLevel = x.RiskLevel}), "Id", "RiskLevel ", RL); // Viewbag

                    ViewBag.ContractStatuses =
              new SelectList(Obj.CustomerContractStatus.ToList().
              Select(x => new { Id = x.Id, ContractStatus = x.ContractStatus }), "Id", "ContractStatus ", CS); // Viewbag
                }
                catch (Exception)
                {

                    ;
                }

                return View(comment);
            }
        }

        [HttpPost]
        
        public ActionResult Edit(AccountComments_pre comment)
        {
            try
            {
                string customer = "";
                if (comment != null)
                {
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {
                        var Comment_ = Obj.Entry(comment);
                        AccountComments_pre CommentObj = Obj.AccountComments_pre.Where(x => x.Id == comment.Id).FirstOrDefault();
                        customer = CommentObj.CustomerGroup;
                        CommentObj.RiskLevel = comment.RiskLevel == null ? null : comment.RiskLevel;
                        CommentObj.CustomerComment = comment.CustomerComment?.ToString() ?? String.Empty;
                        CommentObj.RevenueComment = comment.RevenueComment?.ToString() ?? String.Empty;
                        CommentObj.ContractStatus = comment.ContractStatus == null ? null : comment.ContractStatus;
                        CommentObj.Executive = comment.Executive == null ? null : comment.Executive;
                        CommentObj.ActionRequired = comment.ActionRequired?.ToString() ?? String.Empty;
                        CommentObj.CustomerBusinessUpdate = comment.CustomerBusinessUpdate?.ToString() ?? String.Empty;
                        CommentObj.ContractStatusUpdate = comment.ContractStatusUpdate?.ToString() ?? String.Empty;
                        //if (comment.DueDate==null)
                        //{
                        //    comment.DueDate =Convert.ToDateTime("01-01-1900") ;
                        //}
                        CommentObj.DueDate = comment.DueDate;
                        Obj.SaveChanges();

                       var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();

                        Obj.spAmPortalActivityLog(loggeduser, "Edit Customer Comment for the customer " + CommentObj.CustomerGroup + " Comment is: " + CommentObj.CustomerComment);

                        ViewBag.message = "Comment Updated for The Customer " + customer;
                        return RedirectToAction("Index", new
                        {

                            message = "Comment Updated for The Customer " + customer,
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("Index", new
                    {

                        message = " Failed to update comment for the Customer " + customer,
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex) 
            {

                return RedirectToAction("Index", new
                {

                    message = " Failed to update comment ",
                    updatestatus = 2
                }); ;
            }
          
        }

        /// <summary>  
        /// Insert New AccountComments_pre  
        /// </summary>  
        /// <param name="Commentloye"></param>  
        /// <returns></returns>  

        
        public ActionResult Create(string accountmanager)
        {
            accountmanager = (string)Session["Uname"];
            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
                // int CommentId = int.Parse(Id);
               
                try
                {
                    var mymissingcustomers = Obj.vMissingComment.Where(a => a.AccountManager.ToUpper() == accountmanager.ToUpper());

                    ViewBag.Customers =
          new SelectList(mymissingcustomers.ToList().
          Select(x => new { Id = x.CustomerCode, CustomerGroup = x.CustomerCode }), "Id", "CustomerGroup ", ""); // Viewbag

                    ViewBag.RiskLevels =
           new SelectList(Obj.CustomerRiskLevel.ToList().
           Select(x => new { Id = x.Id, RiskLevel = x.RiskLevel }), "Id", "RiskLevel ","LOW"); // Viewbag

                    ViewBag.ContractStatuses =
              new SelectList(Obj.CustomerContractStatus.ToList().
              Select(x => new { Id = x.Id, ContractStatus = x.ContractStatus }), "Id", "ContractStatus ","NO"); // Viewbag
                }
                catch (Exception)
                {

                    ;
                }

                return View();
            }
        }

        [HttpPost]
        
        public ActionResult Create(AccountComments_pre AccountComment)
        {
            string customer = "";
            try
            {
                if (AccountComment.CustomerGroup != null && AccountComment.CustomerComment!= null)
                {
                    customer = AccountComment.CustomerGroup;
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {
                        try
                        {
                            string st = AccountComment.DueDate.ToString();
                           // AccountComment.DueDate = AccountComment.DueDate.to
                            string st2 = Convert.ToDateTime(st).ToShortDateString();
                            AccountComment.DueDate = Convert.ToDateTime(st2);
                        }
                        catch (Exception)
                        {

                            string dt = AccountComment.DueDate.ToString();
                            AccountComment.DueDate = Convert.ToDateTime("01/01/1900");
                        }
                        AccountComment.AccountManager = (string)Session["Uname"];
                        Obj.AccountComments_pre.Add(AccountComment);
                        Obj.SaveChanges();
                        var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();

                        Obj.spAmPortalActivityLog(loggeduser, "Comment Added for the Customer " + AccountComment.CustomerGroup + " Comment is: " + AccountComment.CustomerComment);
                        ViewBag.message = "Comment Added for the Customer " + AccountComment.CustomerGroup;
                        return RedirectToAction("Index", new
                        {
                            message = "Comment Added for the Customer ",
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("Index", new
                    {

                        message = " Failed to create customer's comment ",
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {
                return RedirectToAction("Index", new
                {

                    message = " Failed to create customer's comment ",
                    updatestatus = 2
                }); ;
            }
            
       
        }

        
        public ActionResult Delete(long Id)
        {
            try
            {
                using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                {
                    // int CommentId = int.Parse(Id);

                    var comment = Obj.AccountComments_pre.Where(a => a.Id == Id).FirstOrDefault();

                    return View(comment);
                }
            }
            catch (Exception ex)
            {

                throw;
            }
          
        }
        /// <summary>  
        /// Delete AccountComments_pre Information  
        /// </summary>  
        /// <param name="comment"></param>  
        /// <returns></returns>  

        
        [HttpPost]
        public ActionResult Delete(AccountComments_pre comment)
        {
            try
            {
                string customer = "";
                if (comment != null)
                {
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {
                        customer = comment.CustomerGroup;
                        var Comment_ = Obj.Entry(comment);
                        if (Comment_.State == System.Data.Entity.EntityState.Detached)
                        {
                            Obj.AccountComments_pre.Attach(comment);
                            Obj.AccountComments_pre.Remove(comment);
                        }
                        Obj.SaveChanges();

                        var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();

                        Obj.spAmPortalActivityLog(loggeduser, "Comment Deleted for The Customer " + customer + " Comment is: " + comment.CustomerComment);

                        ViewBag.message = "Comment Deleted for The Customer " + customer;
                        return RedirectToAction("Index", new
                        {

                            message = "Comment Deleted for The Customer " + customer,
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("Index", new
                    {

                        message = " Failed to Delete comment for the Customer " + customer,
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {
                return RedirectToAction("Index", new
                {

                    message = " Failed to Delete comment ",
                    updatestatus = 2
                });
            }
         
        }
        /// <summary>  
        /// Update AccountComments_pre Information  
        /// </summary>  
        /// <param name="comment"></param>  
        /// <returns></returns>  
        
    }
}