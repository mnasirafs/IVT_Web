using AFSDataGateway.Services;
using AFSDataGateway.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;

namespace AFSDataGateway.Controllers
{
    public class IntegrityConsignmentController : Controller
    {
        // GET: IntegrityConsignment
        AFSXchange1Entities usersEntities = new AFSXchange1Entities();

        //public ActionResult Index(string message,int? updatestatus,string loginname , string loggedas)
        //{

        //    ViewBag.DisplayForAdmin = "hidden";
        //    ViewBag.DisplayForSuperAdmin = "hidden";
        //    ViewBag.DisplayLinkStatus = "hidden";
        //    string authinticated_user = WindowsIdentity.GetCurrent().Name.Split('\\').Last();

        //    if (authinticated_user==null)
        //    {
        //         authinticated_user = (string)Session["LoginName"];
        //    }


        //    if (DateTime.Now.Hour < 12)
        //    {
        //        ViewBag.lblGreeting = "Good Morning ";

        //    }
        //    else if (DateTime.Now.Hour < 17)
        //    {
        //        ViewBag.lblGreeting = "Good Afternoon ";

        //    }
        //    else
        //    {
        //        ViewBag.lblGreeting = "Good Evening ";

        //    }

        //    string loggeduser = "";
        //    if (TempData["LoggedAs"] == null && Session["LoggedAs"]==null && loggedas==null)
        //    {
        //        //string olduserconfig =System.Web.HttpContext.Current.WindowsIdentity.GetCurrent().Name.Split('\\').Last();
        //        //string userold=WindowsIdentity.GetCurrent().Name;
        //        //HttpContext.Current.WindowsIdentity.GetCurrent().Name


        //        loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
        //        //loggeduser = "adagostino";
        //        Session["Uname"] = loggeduser;

        //        if (Session["NewVisit"] == null)
        //        {
        //            usersEntities.spIntegrityReportLog(loggeduser, "User  " + loggeduser + " has re-visited portal");
        //            Session["NewVisit"] = "Visited";
        //        }
        //    }
        //    else if(Session["LoggedAs"] !=null)
        //    {

        //        loggeduser = (string)Session["LoggedAs"]; 
        //    }
        //    else if ( loggedas != null)
        //    {

        //        loggeduser = loggedas;
        //    }
        //    else if (TempData["LoggedAs"] != null )
        //    {

        //        loggeduser = (string)TempData["LoggedAs"]; ;
        //    }
        //    else
        //    {
        //        loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last(); // "sallan";

        //    }


        //    var userinfo = usersEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
        //    ViewBag.UserName = loggeduser;

        //    string loginmessage = string.Empty;

        //    if (userinfo != null && userinfo.IsActive == 1)
        //    {
        //        if (userinfo.IsFirstLogin == true)
        //        {
        //            ViewBag.DisplayLinkStatus = "visible";
        //            ViewBag.DisplayForAdmin = "hidden";
        //            ViewBag.DisplayForSuperAdmin = "hidden";
        //            TempData["UserName"] = userinfo.DWHEditor;
        //            Session["Uname"] = userinfo.DWHEditor;
        //            TempData["Utype"] = userinfo.UserType;
        //            TempData["LoginName"] = loggeduser.Trim();
        //            Session["LoginName"] = loggeduser.Trim();
        //            return RedirectToAction("ChangePassword");

        //        }
        //        else if (userinfo.UserType == 0 && userinfo.IsFirstLogin != true && userinfo.IsActive == 1 && (string)Session["LoggedAs"] == null && loggedas==null && MyGlobalVariables.loggedas != 1)
        //        {
        //            ViewBag.DisplayLinkStatus = "visible";
        //            ViewBag.DisplayForAdmin = "hidden";
        //            ViewBag.DisplayForSuperAdmin = "hidden";
        //            FormsAuthentication.SetAuthCookie(loggeduser,true);
        //            TempData["UserName"] = userinfo.DWHEditor;
        //            Session["Uname"] = userinfo.DWHEditor;
        //            Session["Utype"] = userinfo.UserType;
        //            TempData["LoginName"] = loggeduser.Trim().ToUpper();
        //            Session["LoginName"] = loggeduser.Trim().ToUpper();


        //            if (message == null)
        //            {
        //                ViewBag.ViewMessage = "hidden";
        //                ViewBag.LastUpdateViewMessage = "visible";
        //            }
        //            else if (updatestatus >= 1)
        //            {
        //                TempData["message"] = message;
        //                ViewBag.Message = message;
        //                ViewBag.messageStatus = updatestatus;
        //                ViewBag.LastUpdateViewMessage = "hidden";
        //            }
        //            else
        //            {
        //                TempData["message"] = null;
        //                ViewBag.messageStatus = 0;
        //                ViewBag.Message = null;
        //                ViewBag.LastUpdateViewMessage = "visible";
        //            }

        //            try
        //            {
        //                AFSXchange1Entities Obj = new AFSXchange1Entities();
        //                var lastupdate = Obj.vAccountCommentUpdateTime.FirstOrDefault();
        //                TempData["LastUpdate"] = "Last Update was at " + String.Format("{0:dd/MM/yyyy} 3:00 PM", lastupdate.LastUpdate) + " , Next load will be at " + String.Format("{0:dd/MM/yyyy} 3:00 PM", lastupdate.NextLoad);
        //            }
        //            catch (Exception ex)
        //            {

        //                TempData["message"] = "Error Occured";
        //                ViewBag.Message = "Error Occured";
        //                ViewBag.messageStatus = 2;
        //            }

        //            return View(Get_AllAccountComments());
        //        }
        //        else if (userinfo.UserType == 0 && userinfo.IsFirstLogin != true && userinfo.IsActive == 1  &&  MyGlobalVariables.loggedas == 1)
        //        {
        //            ViewBag.DisplayLinkStatus = "visible";
        //            ViewBag.DisplayForAdmin = "hidden";
        //            ViewBag.DisplayForSuperAdmin = "visible";
        //            FormsAuthentication.SetAuthCookie(loggeduser, true);
        //            TempData["UserName"] = userinfo.DWHEditor;
        //            Session["Uname"] = userinfo.DWHEditor;
        //            Session["Utype"] = userinfo.UserType;
        //            TempData["LoginName"] = loggeduser.Trim().ToUpper();
        //            Session["LoginName"] = loggeduser.Trim().ToUpper();


        //            if (message == null)
        //            {
        //                ViewBag.ViewMessage = "hidden";
        //                ViewBag.LastUpdateViewMessage = "visible";
        //            }
        //            else if (updatestatus >= 1)
        //            {
        //                TempData["message"] = message;
        //                ViewBag.Message = message;
        //                ViewBag.messageStatus = updatestatus;
        //                ViewBag.LastUpdateViewMessage = "hidden";
        //            }
        //            else
        //            {
        //                TempData["message"] = null;
        //                ViewBag.messageStatus = 0;
        //                ViewBag.Message = null;
        //                ViewBag.LastUpdateViewMessage = "visible";
        //            }

        //            try
        //            {
        //                AFSXchange1Entities Obj = new AFSXchange1Entities();
        //                var lastupdate = Obj.vAccountCommentUpdateTime.FirstOrDefault();
        //                TempData["LastUpdate"] = "Last Update was at " + String.Format("{0:dd/MM/yyyy} 3:00 PM", lastupdate.LastUpdate) + " , Next load will be at " + String.Format("{0:dd/MM/yyyy} 3:00 PM", lastupdate.NextLoad);
        //            }
        //            catch (Exception ex)
        //            {

        //                TempData["message"] = "Error Occured";
        //                ViewBag.Message = "Error Occured";
        //                ViewBag.messageStatus = 2;
        //            }

        //            return View(Get_AllAccountComments());
        //        }
        //        else if ((userinfo.UserType == 1) && userinfo.IsFirstLogin != true)
        //        {
        //            ViewBag.DisplayLinkStatus = "visible";
        //            ViewBag.DisplayForAdmin = "visible";
        //            ViewBag.DisplayForSuperAdmin = "hidden";
        //            FormsAuthentication.SetAuthCookie(loggeduser, true);
        //            TempData["UserName"] = userinfo.DWHEditor;
        //            Session["Uname"] = userinfo.DWHEditor;
        //            Session["Utype"] = userinfo.UserType;
        //            TempData["Utype"] = userinfo.UserType;
        //            TempData["LoginName"] = loggeduser.Trim().ToUpper();
        //            Session["LoginName"] = loggeduser.Trim().ToUpper();

        //            return RedirectToAction("UserList", "Home");

        //        }
        //        else if ((userinfo.UserType == 2) && userinfo.IsFirstLogin != true)
        //        {
        //            ViewBag.DisplayLinkStatus = "visible";
        //            ViewBag.DisplayForAdmin = "visible";
        //            ViewBag.DisplayForSuperAdmin = "visible";
        //            FormsAuthentication.SetAuthCookie(loggeduser, true);
        //            TempData["UserName"] = userinfo.DWHEditor;
        //            Session["Uname"] = userinfo.DWHEditor;
        //            Session["Utype"] = userinfo.UserType;
        //            TempData["Utype"] = userinfo.UserType;
        //            TempData["LoginName"] = loggeduser.Trim().ToUpper();
        //            Session["LoginName"] = loggeduser.Trim().ToUpper();

        //            return RedirectToAction("UserList", "Home");

        //        }

        //    }
        //    else
        //    {
        //        // var checkdisable = usersEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
        //        if (userinfo != null && userinfo.IsActive == 0)
        //        {
        //            ViewBag.DisplayLinkStatus = "hidden";
        //            ViewBag.DisplayForAdmin = "hidden";
        //            ViewBag.DisplayForSuperAdmin = "hidden";
        //            TempData["Utype"] = 0;
        //            message = "Your User Has Disabled!, Please Contact Business Admin";
        //            return RedirectToAction("NotAuthorized", "Home");

        //        }
        //        else
        //        {
        //            ViewBag.DisplayLinkStatus = "hidden";
        //            ViewBag.DisplayForAdmin = "hidden";
        //            ViewBag.DisplayForSuperAdmin = "hidden";
        //            TempData["Utype"] = 0;
        //            message = "You  have no rights , please contact support.";
        //            return RedirectToAction("NotAuthorized", "Home");

        //        }

        //    }

        //    return View(Get_AllAccountComments());



        //}
        [HttpPost]
        public ActionResult HidedConsignment(FormCollection formCollection)
        {

            if (formCollection["ID"] == null && (formCollection["search"].ToString() == "" && formCollection["despatchdate"].ToString() == ""))
            {
                return RedirectToAction("HidedConsignment", new
                {
                    message = "Please Select Connote(s) First!!",
                    updatestatus = 2
                });
            }
                else if(formCollection["ID"] != null)
                    {
                string[] ids = formCollection["ID"].Split(new char[] { ',' });
                foreach (string id in ids)
                {
                    try
                    {


                        //  ConnoteToHide connotetohide = new ConnoteToHide();

                        string authinticated_user = (string)TempData["LoginName"];

                        if (authinticated_user == null)
                        {
                            authinticated_user = (string)Session["LoginName"];
                        }
                        if (id != null)
                        {
                            var hideid = Convert.ToInt64(id);
                            var hideconnote = usersEntities.ConnoteToHide.Where(a => a.id == hideid).FirstOrDefault();
                            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                            {
                                Obj.ConnoteToHide.Attach(hideconnote);
                                Obj.ConnoteToHide.Remove(hideconnote);
                                Obj.SaveChanges();
                                var loggeduser = authinticated_user.Split('\\').Last();

                                Obj.spIntegrityReportLog(loggeduser, " Connote " + hideconnote.Connote + " has been Un Hided ");
                                ViewBag.message = "Connote " + hideconnote.Connote + " has been un hided";


                            }
                        }
                        else
                        {

                            return RedirectToAction("HidedConsignments", new
                            {

                                message = " Failed to un hide consignment ",
                                updatestatus = 2
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        return RedirectToAction("HidedConsignments", new
                        {

                            message = "Failed to un hide consignment",
                            updatestatus = 2
                        }); ;
                    }

                }

                return RedirectToAction("HidedConsignment", new
                {
                    message = "Selected connotes have been un hided successfully",
                    updatestatus = 1
                });
            } // End of Else with selected IDs

            else if (formCollection["ID"] == null && (formCollection["search"] != null || formCollection["despatchdate"] != null))
            {
                return RedirectToAction("HidedConsignment" ,new
                {
                    search = formCollection["search"].ToString(),
                    despatchDate= formCollection["despatchdate"].ToString()
                   
                });
            }
            else
            {
                return RedirectToAction("HidedConsignment");
            }

        }
          
           
        public ActionResult HidedConsignment(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate)
        {
            ViewBag.DisplayForAdmin = "hidden";
            ViewBag.DisplayForSuperAdmin = "hidden";
            ViewBag.DisplayLinkStatus = "hidden";
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
                //    usersEntities.spIntegrityReportLog(loggeduser, "User  " + loggeduser + " has re-visited portal");
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

             
            ViewBag.LoggedUser = UserIdentity.CurrentLogin();
            ViewBag.LoggedUserStatus = "visible";

            var userinfo = usersEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
            ViewBag.UserName = loggeduser;

            string loginmessage = string.Empty;

            if (userinfo != null && userinfo.IsActive == 1)
            {
                if (userinfo.IsFirstLogin == true)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    TempData["UserName"] = userinfo.DWHEditor;
                    Session["Uname"] = userinfo.DWHEditor;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim();
                    Session["LoginName"] = loggeduser.Trim();
                    return RedirectToAction("ChangePassword");

                }
                else if (userinfo.UserType == 0 && userinfo.IsFirstLogin != true && userinfo.IsActive == 1 && (string)Session["LoggedAs"] == null && loggedas == null && MyGlobalVariables.loggedas != 1)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.DWHEditor;
                    Session["Uname"] = userinfo.DWHEditor;
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



                    AFSXchange1Entities Obj = new AFSXchange1Entities();
                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    // Assuming 'dbContext' is your Entity Framework context
                    var loginNameParam = new SqlParameter("@LoginName", loggeduser);
                    var searchParam = new SqlParameter("@Search", search ?? (object)DBNull.Value); // Handling null
                    var despatchDateParam = new SqlParameter("@DespatchDate", despatchDate.HasValue ? (object)despatchDate.Value : DBNull.Value);

                    var cons = Obj.Database.SqlQuery<vConnoteToHide>("AFSXchange1.dbo.GetIntegrityHiddenConnote @Search, @DespatchDate, @LoginName", searchParam, despatchDateParam, loginNameParam).ToList();



                 
                    // At this point the query has generated a SQL statement based on the conditions above,
                    // but it will not be executed until the until the next line - i.e. when calling .ToList()
                    return View(cons.ToList());
                }
                else if ((userinfo.UserType == 0 || userinfo.UserType == 2) && userinfo.IsFirstLogin != true && userinfo.IsActive == 1 && MyGlobalVariables.loggedas == 1)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "visible";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.DWHEditor;
                    Session["Uname"] = userinfo.DWHEditor;
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


                    AFSXchange1Entities Obj = new AFSXchange1Entities();
                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    var loginNameParam = new SqlParameter("@LoginName", loggeduser);
                    var searchParam = new SqlParameter("@Search", search ?? (object)DBNull.Value); // Handling null
                    var despatchDateParam = new SqlParameter("@DespatchDate", despatchDate.HasValue ? (object)despatchDate.Value : DBNull.Value);

                    var cons = Obj.Database.SqlQuery<vConnoteToHide>("AFSXchange1.dbo.GetIntegrityHiddenConnote @Search, @DespatchDate, @LoginName", searchParam, despatchDateParam, loginNameParam).ToList();


                    // At this point the query has generated a SQL statement based on the conditions above,
                    // but it will not be executed until the until the next line - i.e. when calling .ToList()
                    return View(cons.ToList());
                }
                else if ((userinfo.UserType == 1) && userinfo.IsFirstLogin != true)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.DWHEditor;
                    Session["Uname"] = userinfo.DWHEditor;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim().ToUpper();
                    Session["LoginName"] = loggeduser.Trim().ToUpper();

                    return RedirectToAction("UserList", "Home");

                }
                else if ((userinfo.UserType == 2) && userinfo.IsFirstLogin != true)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "visible";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.DWHEditor;
                    Session["Uname"] = userinfo.DWHEditor;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim().ToUpper();
                    Session["LoginName"] = loggeduser.Trim().ToUpper();

                    return RedirectToAction("UserList", "Home");

                }

            }
            else
            {
                // var checkdisable = usersEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
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

                ViewBag.Datetime = DateTime.UtcNow;
                if (despatchDate != null)
                {
                    ViewBag.despatchDate = Convert.ToDateTime(despatchDate).Date;
                    despatchDate = Convert.ToDateTime(despatchDate).Date;
                }
                else
                {
                    ViewBag.despatchDate = ViewBag.Datetime;
                }



                var DWHEditor = (string)Session["Uname"];
                AFSXchange1Entities Obj = new AFSXchange1Entities();
                //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                var loginNameParam = new SqlParameter("@LoginName", loggeduser);
                var searchParam = new SqlParameter("@Search", search ?? (object)DBNull.Value); // Handling null
                var despatchDateParam = new SqlParameter("@DespatchDate", despatchDate.HasValue ? (object)despatchDate.Value : DBNull.Value);

                var cons = Obj.Database.SqlQuery<vConnoteToHide>("AFSXchange1.dbo.GetIntegrityHiddenConnote @Search, @DespatchDate, @LoginName", searchParam, despatchDateParam, loginNameParam).ToList();


                // At this point the query has generated a SQL statement based on the conditions above,
                // but it will not be executed until the until the next line - i.e. when calling .ToList()
                return View(cons.ToList());
            }
            catch (Exception)
            {
                return RedirectToAction("TimeOut", "Home");
            }

        }

        public ActionResult DownloadCSV_HidedConsignment()
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


            AFSXchange1Entities Obj = new AFSXchange1Entities();
            IQueryable<vConnoteToHide> cons = Obj.vConnoteToHide
            .Where(x => x.HidedBy.Equals(loggeduser));
            // Retrieve data for CSV
            var data = cons.ToList();


            // Generate CSV content
            var csvContent = "Connote,DespatchDate,Reason,HidedBy,ActionTime\n";
            foreach (var item in data)
            {
                csvContent += $"{item.Connote},{item.DespatchDate},{item.Reason},{item.HidedBy},{item.ActionTime}\n";
            }

            // Return CSV file
            return File(new System.Text.UTF8Encoding().GetBytes(csvContent), "text/csv", "HiddenConsignments_Download.csv");
        }


        [HttpPost]
        public ActionResult SearchConsignments(FormCollection formCollection)
        {
          

            if (formCollection["ID"] == null && (formCollection["search"].ToString() == "" && formCollection["despatchdate"].ToString() == ""))
            {
                return RedirectToAction("SearchConsignments", new
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

                        var consid = Convert.ToInt64(id);
                        var connote = usersEntities.vIntegrityConnoteReadyToHide.Where(a => a.cId == consid).FirstOrDefault();

                        ConnoteToHide connotetohide = new ConnoteToHide();

                        string authinticated_user = (string)TempData["LoginName"];

                        if (authinticated_user == null)
                        {
                            authinticated_user = (string)Session["LoginName"];
                        }
                        if (connotetohide != null)
                        {

                            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                            {
                                connotetohide.Connote = connote.ConsignmentNo;
                                connotetohide.DespatchDate = connote.DespatchDate;
                                connotetohide.Reason = formCollection["Reason"].ToString();
                                connotetohide.HidedBy = authinticated_user.Split('\\').Last();
                                connotetohide.ActionTime = DateTime.Now;
                                Obj.ConnoteToHide.Add(connotetohide);
                                Obj.SaveChanges();
                                var loggeduser = authinticated_user.Split('\\').Last();

                                Obj.spIntegrityReportLog(loggeduser, "New Connote " + connotetohide.Connote + " has been hided");
                                ViewBag.message = "Connote " + connotetohide.Connote + " has been hided";


                            }
                        }
                        else
                        {

                            return RedirectToAction("SearchConsignments", new
                            {

                                message = " Failed to hide consignment - Already Hided ",
                                updatestatus = 2
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        return RedirectToAction("SearchConsignments", new
                        {

                            message = "Failed to  hide consignment",
                            updatestatus = 2
                        }); ;
                    }

                }

                return RedirectToAction("SearchConsignments", new
                {
                    message = "Selected connotes have been hided successfully",
                    updatestatus = 1
                });
            } // End of Else with selected IDs

            else if (formCollection["ID"] == null && (formCollection["search"] != null || formCollection["despatchdate"] != null))
            {
                return RedirectToAction("SearchConsignments", new
                {
                    search = formCollection["search"].ToString(),
                    despatchDate = formCollection["despatchdate"].ToString()

                });
            }
            else
            {
                return RedirectToAction("SearchConsignments");
            }
        }

        public ActionResult SearchConsignments(string message, int? updatestatus, string loginname, string loggedas,string search,DateTime? despatchDate)
        {
            ViewBag.LoggedUser = UserIdentity.CurrentLogin();
            ViewBag.LoggedUserStatus = "visible";

            ViewBag.DisplayForAdmin = "hidden";
            ViewBag.DisplayForSuperAdmin = "hidden";
            ViewBag.DisplayLinkStatus = "hidden";
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

                if (Session["NewVisit"] == null)
                {
                    usersEntities.spIntegrityReportLog(loggeduser, "User  " + loggeduser + " has re-visited portal");
                    Session["NewVisit"] = "Visited";
                }
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


            var userinfo = usersEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
            ViewBag.UserName = loggeduser;

            string loginmessage = string.Empty;

            if (userinfo != null && userinfo.IsActive == 1)
            {
                if (userinfo.IsFirstLogin == true)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    TempData["UserName"] = userinfo.DWHEditor;
                    Session["Uname"] = userinfo.DWHEditor;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim();
                    Session["LoginName"] = loggeduser.Trim();
                    return RedirectToAction("ChangePassword");

                }
                else if (userinfo.UserType == 0 && userinfo.IsFirstLogin != true && userinfo.IsActive == 1 && (string)Session["LoggedAs"] == null && loggedas == null && MyGlobalVariables.loggedas != 1)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.DWHEditor;
                    Session["Uname"] = userinfo.DWHEditor;
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



                    AFSXchange1Entities Obj = new AFSXchange1Entities();
                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    // Assuming 'dbContext' is your Entity Framework context
                    var loginNameParam = new SqlParameter("@LoginName", loggeduser);
                    var searchParam = new SqlParameter("@Search", search ?? (object)DBNull.Value); // Handling null
                    var despatchDateParam = new SqlParameter("@DespatchDate", despatchDate.HasValue ? (object)despatchDate.Value : DBNull.Value);

                    var cons = Obj.Database.SqlQuery<vIntegrityConnoteReadyToHide>("AFSXchange1.dbo.GetIntegrityReport @Search, @DespatchDate, @LoginName", searchParam, despatchDateParam, loginNameParam).ToList();

                    // At this point the query has generated a SQL statement based on the conditions above,
                    // but it will not be executed until the until the next line - i.e. when calling .ToList()
                    return View(cons.ToList());
                }
                else if ((userinfo.UserType == 0 || userinfo.UserType == 2) && userinfo.IsFirstLogin != true && userinfo.IsActive == 1 && MyGlobalVariables.loggedas == 1)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "visible";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.DWHEditor;
                    Session["Uname"] = userinfo.DWHEditor;
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


                        AFSXchange1Entities Obj = new AFSXchange1Entities();
                    
                        IQueryable < vIntegrityConnoteReadyToHide > cons = Obj.vIntegrityConnoteReadyToHide;

                    // Assuming 'dbContext' is your Entity Framework context
                    var loginNameParam = new SqlParameter("@LoginName", loggeduser);
                    var searchParam = new SqlParameter("@Search", search ?? (object)DBNull.Value); // Handling null
                    var despatchDateParam = new SqlParameter("@DespatchDate", despatchDate.HasValue ? (object)despatchDate.Value : DBNull.Value);

                    var cons2 = Obj.Database.SqlQuery<vIntegrityConnoteReadyToHide>("AFSXchange1.dbo.GetIntegrityReport @Search, @DespatchDate, @LoginName", searchParam, despatchDateParam, loginNameParam).ToList();

                    // At this point the query has generated a SQL statement based on the conditions above,
                    // but it will not be executed until the until the next line - i.e. when calling .ToList()
                   


                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();
                    return View(cons2.ToList());

                    // At this point the query has generated a SQL statement based on the conditions above,
                    // but it will not be executed until the until the next line - i.e. when calling .ToList()

                }
                else if ((userinfo.UserType == 1) && userinfo.IsFirstLogin != true)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.DWHEditor;
                    Session["Uname"] = userinfo.DWHEditor;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim().ToUpper();
                    Session["LoginName"] = loggeduser.Trim().ToUpper();

                    return RedirectToAction("UserList", "Home");

                }
                else if ((userinfo.UserType == 2) && userinfo.IsFirstLogin != true)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "visible";
                    FormsAuthentication.SetAuthCookie(loggeduser, true);
                    TempData["UserName"] = userinfo.DWHEditor;
                    Session["Uname"] = userinfo.DWHEditor;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = loggeduser.Trim().ToUpper();
                    Session["LoginName"] = loggeduser.Trim().ToUpper();

                    return RedirectToAction("UserList", "Home");

                }

            }
            else
            {
                // var checkdisable = usersEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
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

                ViewBag.Datetime = DateTime.UtcNow;
                if (despatchDate != null)
                {
                    ViewBag.despatchDate = Convert.ToDateTime(despatchDate).Date;
                    despatchDate = Convert.ToDateTime(despatchDate).Date;
                }
                else
                {
                    ViewBag.despatchDate = ViewBag.Datetime;
                }



                var DWHEditor = (string)Session["Uname"];

                AFSXchange1Entities Obj = new AFSXchange1Entities();
                //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                // Assuming 'dbContext' is your Entity Framework context
                var loginNameParam = new SqlParameter("@LoginName", loggeduser);
                var searchParam = new SqlParameter("@Search", search ?? (object)DBNull.Value); // Handling null
                var despatchDateParam = new SqlParameter("@DespatchDate", despatchDate.HasValue ? (object)despatchDate.Value : DBNull.Value);

                var cons = Obj.Database.SqlQuery<vIntegrityConnoteReadyToHide>("AFSXchange1.dbo.GetIntegrityReport @Search, @DespatchDate, @LoginName", searchParam, despatchDateParam, loginNameParam).ToList();

                // At this point the query has generated a SQL statement based on the conditions above,
                // but it will not be executed until the until the next line - i.e. when calling .ToList()
               
                // At this point the query has generated a SQL statement based on the conditions above,
                // but it will not be executed until the until the next line - i.e. when calling .ToList()
                return View(cons.ToList());

            }
            catch (Exception)
            {
                return RedirectToAction("TimeOut", "Home");
            }

        }



        public ActionResult DownloadCSV_SearchConsignments()
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


            AFSXchange1Entities Obj = new AFSXchange1Entities();
            IQueryable<vIntegrityConnoteReadyToHide> cons = Obj.vIntegrityConnoteReadyToHide
            .Where(x => x.LoginName.Equals(loggeduser));
            // Retrieve data for CSV
            var data = cons.ToList();


            // Generate CSV content
            var csvContent = "CatchFilter,PickupAddress1,CarrierBusinessName,ConsignmentNo,DespatchDate,DeliveryAddress1,FreightStatus,TotWeightKg ,RateIncSurch,TotVolM\n";
            foreach (var item in data)
            {
                // Replace commas with hyphens in each property
                string catchFilter = item.CatchFilter.Replace(",", "-");
                string pickupAddress1 = item.PickupAddress1.Replace(",", "-");
                string carrierBusinessName = item.CarrierBusinessName.Replace(",", "-");
                string consignmentNo = item.ConsignmentNo.Replace(",", "-");
                string despatchDate = item.DespatchDate.ToString();
                string deliveryAddress1 = item.DeliveryAddress1.Replace(",", "-");
                string freightStatus = item.FreightStatus.Replace(",", "-");
                string totWeightKg = item.TotWeightKg.ToString();
                string rateIncSurch = item.RateIncSurch.ToString();
                string totVolM = item.TotVolM.ToString();

                // Concatenate the modified values to the csvContent
                csvContent += $"{catchFilter},{pickupAddress1},{carrierBusinessName},{consignmentNo},{despatchDate},{deliveryAddress1},{freightStatus},{totWeightKg},{rateIncSurch},{totVolM}\n";
            }

            // Return CSV file
            return File(new System.Text.UTF8Encoding().GetBytes(csvContent), "text/csv", "IntegrityReport_Download.csv");
        }


     
        public ActionResult ViewConsignment(long Id)
        {
            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }

            AFSXchange1Entities Obj = new AFSXchange1Entities();




            var cons = Obj.vConsignments.Where(a => a.cId == Id).FirstOrDefault();


            // At this point the query has generated a SQL statement based on the conditions above,
            // but it will not be executed until the until the next line - i.e. when calling .ToList()
            return View(cons);
        }

        //public ActionResult SearchConsignments(IQueryable<vIntegrityConnoteReadyToHide> cons=null)
        //{
        //    if (cons == null)
        //    {
        //        var DWHEditor = (string)Session["Uname"];
        //        .Where(x => x.DWHEditor.ToUpper() == DWHEditor.ToUpper()).Take(100);
        //    }
        //    // but it will not be executed until the until the next line - i.e. when calling .ToList()
        //    return View(cons.ToList());
        //}

        /// <summary>  
        /// Get AccountComments_pre With Id  
        /// </summary>  
        /// <param name="Id"></param>  


        public ActionResult Edit(long Id)
        {
            ViewBag.LoggedUser = UserIdentity.CurrentLogin();
            ViewBag.LoggedUserStatus = "visible";

            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
                // int CommentId = int.Parse(Id);

                var con = Obj.vConsignments.Where(a => a.cId == Id).FirstOrDefault();

                return View(con);
            }
        }

        [HttpPost]

        public ActionResult Edit(ConsignmentIntegrityUpdate Updatedconnote)
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

                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {
                        DateTime myDateTime = DateTime.Now;
                        var Currentcon = Obj.vIntegrityConnoteReadyToHide.Where(a => a.cId == Updatedconnote.cId).FirstOrDefault();
                        ConsignmentIntegrityUpdate CIU =  new ConsignmentIntegrityUpdate();
                        // Save a copy --------
                        CIU.cId = Currentcon.cId;
                        CIU.ConsignmentNo = Currentcon.ConsignmentNo;
                        CIU.CurrentDetail = false;
                        CIU.TotWeightKg = Currentcon.TotWeightKg;
                        CIU.TotVolM = Currentcon.TotVolM;
                        CIU.RateIncSurch = Currentcon.RateIncSurch;
                        CIU.ActionTime = myDateTime;
                        CIU.InsertedBy = loggeduser;
                        Obj.ConsignmentIntegrityUpdate.Add(CIU);
                        Obj.SaveChanges();

                        // Save a updated copy --------
                        Obj.ConsignmentIntegrityUpdate.Where(a => a.cId == Updatedconnote.cId).ToList().ForEach(x => x.CurrentDetail = false);
                        CIU.cId = Currentcon.cId;
                        CIU.ConsignmentNo = Currentcon.ConsignmentNo;
                        CIU.CurrentDetail = true;
                        CIU.TotWeightKg = Updatedconnote.TotWeightKg;
                        CIU.TotVolM = Updatedconnote.TotVolM;
                        CIU.RateIncSurch = Updatedconnote.RateIncSurch;
                        CIU.ActionTime = myDateTime;
                        CIU.InsertedBy = loggeduser;
                        Obj.ConsignmentIntegrityUpdate.Add(CIU);
                        Obj.SaveChanges();

                        // Update Consignment Table;
                        var con = Obj.Consignment.Where(a => a.cId == Currentcon.cId).FirstOrDefault();
                        con.TotWeightKg = (decimal) Updatedconnote.TotWeightKg;
                        con.TotVolM = (decimal)Updatedconnote.TotVolM;
                        con.RateIncSurch = (decimal)Updatedconnote.RateIncSurch;
                        con.LastModifiedTime = myDateTime;
                        con.IsUpdated = true;
                         Obj.SaveChanges();

                        //Log----
                        Obj.spIntegrityReportLog(loggeduser, "Consignment "+Updatedconnote.ConsignmentNo+" Details Updated");

                        ViewBag.message = "Consignment " + Updatedconnote.ConsignmentNo + " Details Updated";
                        return RedirectToAction("SearchConsignments", new
                        {

                            message = "Consignment " + Updatedconnote.ConsignmentNo + " Details Updated",
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("SearchConsignments", new
                    {

                        message = " Failed to update Consignment Details " ,
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {

                return RedirectToAction("SearchConsignments", new
                {

                    message = " Failed to update Consignment Details",
                    updatestatus = 2
                }); ;
            }

        }



        public ActionResult HideUpdate(long Id)
        {
            ViewBag.LoggedUser = UserIdentity.CurrentLogin();
            ViewBag.LoggedUserStatus = "visible";

            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
                // int CommentId = int.Parse(Id);

                var con = Obj.vConnoteToHide.Where(a => a.id == Id).FirstOrDefault();

                return View(con);
            }
        }

        [HttpPost]

        public ActionResult HideUpdate(vConnoteToHide Updatedconnote)
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

                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {
                        DateTime myDateTime = DateTime.Now;
                       // var Currentcon = Obj.vConnoteToHide.Where(a => a.id == Updatedconnote.id).FirstOrDefault();
                        
                        var con = Obj.ConnoteToHide.Where(a => a.id == Updatedconnote.id).FirstOrDefault();
                        con.Reason = Updatedconnote.Reason;
                        con.ActionTime = myDateTime;
                        con.HidedBy = loggeduser;
                        Obj.SaveChanges();

                        //Log----
                        Obj.spIntegrityReportLog(loggeduser, "Consignment " + Updatedconnote.Connote + " Reasons of hide Updated");

                        ViewBag.message = "Consignment " + Updatedconnote.Connote + " Reasons of hide Updated";
                        return RedirectToAction("HidedConsignment", new
                        {

                            message = "Consignment Reasons of hide Updated",
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("HidedConsignment", new
                    {

                        message = "Consignment Reasons of hide Fail to be Updated",
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {

                return RedirectToAction("HidedConsignment", new
                {

                    message = "Consignment Reasons of hide Fail to be Updated",
                    updatestatus = 2
                }); ;
            }

        }



        public ActionResult UnHide(long Id)
        {
            ViewBag.LoggedUser = UserIdentity.CurrentLogin();
            ViewBag.LoggedUserStatus = "visible";

            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
                // int CommentId = int.Parse(Id);

                var con = Obj.vConnoteToHide.Where(a => a.id == Id).FirstOrDefault();

                return View(con);
            }
        }

        [HttpPost]

        public ActionResult UnHide(vConnoteToHide Updatedconnote)
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

                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {
                        DateTime myDateTime = DateTime.Now;
                        // var Currentcon = Obj.vConnoteToHide.Where(a => a.id == Updatedconnote.id).FirstOrDefault();

                        var con = Obj.ConnoteToHide.Where(a => a.id == Updatedconnote.id).FirstOrDefault();
                        Obj.ConnoteToHide.Remove(con);
                        Obj.SaveChanges();

                        //Log----
                        Obj.spIntegrityReportLog(loggeduser, "Consignment " + Updatedconnote.Connote + " has Un hided sucessfully");

                        ViewBag.message = "Consignment " + Updatedconnote.Connote + " has Un hided sucessfully";
                        return RedirectToAction("HidedConsignment", new
                        {

                            message = "Consignment has Un hided sucessfully",
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("HidedConsignment", new
                    {

                        message = "Consignment has failed to Un hided",
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {

                return RedirectToAction("HidedConsignment", new
                {

                    message = "Consignment has failed to Un hided",
                    updatestatus = 2
                }); ;
            }

        }
        public ActionResult HideConsignment(Int64 Id)
        {
            ViewBag.LoggedUser = UserIdentity.CurrentLogin();
            ViewBag.LoggedUserStatus = "visible";

            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }

            var connote = usersEntities.vIntegrityConnoteReadyToHide.Where(a => a.cId == Id).FirstOrDefault();

            ConnoteToHide obj = new ConnoteToHide();

            obj.Connote = connote.ConsignmentNo;
            obj.DespatchDate = Convert.ToDateTime(String.Format("{0:dd/MM/yyyy}", connote.DespatchDate));



            return View(obj);
        }
    

    [HttpPost]

    public ActionResult HideConsignment(ConnoteToHide connotetohide)
    {
        string customer = "";
        try
        {
            string authinticated_user = (string)TempData["LoginName"];

            if (authinticated_user == null)
            {
                authinticated_user = (string)Session["LoginName"];
            }
            if (connotetohide != null )
            {
                
                using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                {
                      
                        connotetohide.HidedBy = authinticated_user.Split('\\').Last();
                      connotetohide.ActionTime = DateTime.Now;
                    Obj.ConnoteToHide.Add(connotetohide);
                    Obj.SaveChanges();
                    var loggeduser = authinticated_user.Split('\\').Last();

                    Obj.spIntegrityReportLog(loggeduser, "New Connote " +connotetohide.Connote +" has been hided");
                    ViewBag.message = "Connote " + connotetohide.Connote + " has been hided";
                    return RedirectToAction("SearchConsignments", new
                    {
                        message = "Connote " + connotetohide.Connote + " has been hided",
                        updatestatus = 1
                    });

                }
            }
            else
            {

                return RedirectToAction("SearchConsignments", new
                {

                    message = " Failed to hide consignment - Already Hided ",
                    updatestatus = 2
                });
            }
        }
        catch (Exception ex)
        {
            return RedirectToAction("SearchConsignments", new
            {

                message = "Failed to hide consignment - Already Hided  ",
                updatestatus = 2
            }); ;
        }


    }


        //public ActionResult UnHide(long Id)
        //{
        //    if (Session["LoggedAs"] != null)
        //    {

        //        ViewBag.DisplayForSuperAdmin = "visible";
        //    }

        //    try
        //    {
        //        using (AFSXchange1Entities Obj = new AFSXchange1Entities())
        //        {
        //            int CommentId = int.Parse(Id);

        //            var comment = Obj.AccountComments_pre.Where(a => a.Id == Id).FirstOrDefault();

        //            return View(comment);
        //        }
        //    }
        //    catch (Exception ex)
        //    {

        //        throw;
        //    }

        //}
        /// <summary>  
        /// Delete AccountComments_pre Information  
        /// </summary>  
        /// <param name="comment"></param>  
        /// <returns></returns>  


        //[HttpPost]
        //public ActionResult UnHide(ConnoteToHide connote)
        //{
        //    try
        //    {
        //        string authinticated_user = (string)TempData["LoginName"];

        //        if (authinticated_user == null)
        //        {
        //            authinticated_user = (string)Session["LoginName"];
        //        }

        //        string customer = "";
        //        if (comment != null)
        //        {
        //            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
        //            {
        //                customer = comment.CustomerGroup;
        //                var Comment_ = Obj.Entry(comment);
        //                if (Comment_.State == System.Data.Entity.EntityState.Detached)
        //                {
        //                    Obj.AccountComments_pre.Attach(comment);
        //                    Obj.AccountComments_pre.Remove(comment);
        //                }
        //                Obj.SaveChanges();

        //                var loggeduser =authinticated_user.Split('\\').Last();

        //                Obj.spIntegrityReportLog(loggeduser, "Comment Deleted for The Customer " + customer + " Comment is: " + comment.CustomerComment);

        //                ViewBag.message = "Comment Deleted for The Customer " + customer;
        //                return RedirectToAction("Index", new
        //                {

        //                    message = "Comment Deleted for The Customer " + customer,
        //                    updatestatus = 1
        //                });

        //            }
        //        }
        //        else
        //        {

        //            return RedirectToAction("Index", new
        //            {

        //                message = " Failed to Delete comment for the Customer " + customer,
        //                updatestatus = 2
        //            });
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        return RedirectToAction("Index", new
        //        {

        //            message = " Failed to Delete comment ",
        //            updatestatus = 2
        //        });
        //    }
         
        //}


        public ActionResult ReportPortal()
        {
            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }
            else
            {
                ViewBag.DisplayForSuperAdmin = "hidden";
            }

            return Redirect("http://afs-sql01:8080/HomePage");
        }


        public ActionResult AM_Portal()
        {
            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }
            else
            {
                ViewBag.DisplayForSuperAdmin = "hidden";
            }

            return Redirect("http://afs-sql01:81/AccountManagerPortal/");
        }

        public ActionResult Moveit()
        {
            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }
            else
            {
                ViewBag.DisplayForSuperAdmin = "hidden";
            }

            return Redirect("http://afs.moveitnetexpress.com.au/moveit/");
        }


        public ActionResult Salesforce()
        {
            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }
            else
            {
                ViewBag.DisplayForSuperAdmin = "hidden";
            }

            return Redirect("https://altfs.my.salesforce.com/");
        }


        public ActionResult PowerBI()
        {
            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }
            else
            {
                ViewBag.DisplayForSuperAdmin = "hidden";
            }

            return Redirect("https://app.powerbi.com/home");
        }

        public ActionResult Help()
        {
            ViewBag.LoggedUser = UserIdentity.CurrentLogin();
            ViewBag.LoggedUserStatus = "visible";

            int utype = (int)Session["Utype"];

            if (utype == 2 || MyGlobalVariables.loggedas == 1)
            {
                ViewBag.DisplayForSuperAdmin = "visible";
            }
            else 
            {
                ViewBag.DisplayForSuperAdmin = "hidden";
            }

            if (Session["LoggedAs"] != null || MyGlobalVariables.loggedas == 1)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }
            else
            {
                ViewBag.DisplayForSuperAdmin = "hidden";
            }

            return View();

        }

        /// <summary>  
        /// Update AccountComments_pre Information  
        /// </summary>  
        /// <param name="comment"></param>  
        /// <returns></returns>  

    }
}