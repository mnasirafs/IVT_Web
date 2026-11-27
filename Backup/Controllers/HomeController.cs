using AFSDataGateway.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;

namespace AFSDataGateway.Controllers
{
    public class HomeController : Controller
    {
      
        public ActionResult Index()
        {
            return View();
        }

        
        public ActionResult Profile()
        {
            return View();
        }

      

       
        public ActionResult ReportPortal()
        {
            return Redirect("http://afs-sql01:8080/HomePage");
        }

        
        public ActionResult Moveit()
        {
            return Redirect("http://afs.moveitnetexpress.com.au/moveit/");
        }

      
        public ActionResult Salesforce()
        {
            return Redirect("https://altfs.my.salesforce.com/");
        }

       
        public ActionResult PowerBI()
        {
            return Redirect("https://app.powerbi.com/home");
        }

        public ActionResult Help()
        {
            int utype = (int)Session["Utype"];

            if (utype == 2)
            {
                ViewBag.DisplayForSuperAdmin = "visible";
            }
            else
            {
                ViewBag.DisplayForSuperAdmin = "hidden";
            }

            return View();

        }

        public ActionResult Login()
        {
            AFSXchange1Entities Obj = new AFSXchange1Entities();
            ViewBag.DisplayLinkStatus = "hidden";
            ViewBag.DisplayForAdmin = "hidden";
            ViewBag.UserName = HttpContext.User.Identity.Name.Split('\\').Last();
            ViewBag.LoginList =
          new SelectList(Obj.vAccountManagerLogin.Where(x=>x.UserType==0 && x.IsDeleted==0 && x.IsActive==1).ToList().
          Select(x => new { Id = x.LoginName, AccountManager = x.AccountManager }), "Id", "AccountManager ", "acamassa"); // Viewbag
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public ActionResult Login(User user)
        {
            ViewBag.DisplayForAdmin = "hidden";
            ViewBag.DisplayLinkStatus = "hidden";
            ViewBag.DisplayForSuperAdmin = "hidden";
            string loggeduser = user.Username;
            AFSXchange1Entities usersEntities = new AFSXchange1Entities();
            var userinfo = usersEntities.vAccountManagerLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
            ViewBag.UserName = loggeduser;
            TempData["LoggedAs"] = loggeduser;
            Session["LoggedAs"]= loggeduser; 
            string message = string.Empty;

            if (userinfo!=null && userinfo.IsActive == 1)
            {
                if(userinfo.IsFirstLogin==true)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    TempData["UserName"] = userinfo.AccountManager;
                    Session["Uname"] = userinfo.AccountManager;
                    TempData["Utype"]= userinfo.UserType;
                    TempData["LoginName"] = user.Username.Trim();
                    Session["LoginName"] = user.Username.Trim();
                    // return RedirectToAction("ChangePassword");
                    return RedirectToAction("Index", "CustomerAtRisk");

                }
                else if(!(userinfo.UserType==1) && userinfo.IsFirstLogin != true && userinfo.IsActive == 1)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "hidden";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(user.Username, user.RememberMe);
                    TempData["UserName"] = userinfo.AccountManager;
                    Session["Uname"] = userinfo.AccountManager;
                    Session["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = user.Username.Trim().ToUpper();
                    Session["LoginName"] = user.Username.Trim().ToUpper();
                    return RedirectToAction("Index", "CustomerAtRisk");
                }
                else if ((userinfo.UserType == 1) && userinfo.IsFirstLogin != true)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "hidden";
                    FormsAuthentication.SetAuthCookie(user.Username, user.RememberMe);
                    TempData["UserName"] = userinfo.AccountManager;
                    Session["Uname"] = userinfo.AccountManager;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = user.Username.Trim().ToUpper();
                    Session["LoginName"] = user.Username.Trim().ToUpper();

                    return RedirectToAction("AccountManagerList", "Home");

                }
                else if ((userinfo.UserType == 2) && userinfo.IsFirstLogin != true)
                {
                    ViewBag.DisplayLinkStatus = "visible";
                    ViewBag.DisplayForAdmin = "visible";
                    ViewBag.DisplayForSuperAdmin = "visible";
                    FormsAuthentication.SetAuthCookie(user.Username, user.RememberMe);
                    TempData["UserName"] = userinfo.AccountManager;
                    Session["Uname"] = userinfo.AccountManager;
                    Session["Utype"] = userinfo.UserType;
                    TempData["Utype"] = userinfo.UserType;
                    TempData["LoginName"] = user.Username.Trim().ToUpper();
                    Session["LoginName"] = user.Username.Trim().ToUpper();

                    return RedirectToAction("AccountManagerList", "Home");

                }

            }
            else
            {
               // var checkdisable = usersEntities.vAccountManagerLogin.Where(a => a.LoginName.ToUpper() == user.Username.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
                if(userinfo!=null && userinfo.IsActive == 0)
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

     
        public ActionResult DeleteCustomerAccountManager(long Id)
        {
            try
            {
                using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                {
                    // int CommentId = int.Parse(Id);

                    var cam = Obj.CustomerAccountManager.Where(a => a.Id == Id).FirstOrDefault();

                    return View(cam);
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
        
        public ActionResult DeleteCustomerAccountManager(CustomerAccountManager cam)
        {
            try
            {
                string customer = "";
                string accountmanager = "";
                if (cam != null)
                {
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {
                      
                        var camobject = Obj.CustomerAccountManager.Where(a => a.Id == cam.Id).FirstOrDefault();

                        customer = camobject.CustomerCode;
                        accountmanager = camobject.AccountManager;
                        var deleteOrderDetails =
                        from details in Obj.CustomerAccountManager
                        where details.Id == cam.Id
                        select details;

                        foreach (var detail in deleteOrderDetails)
                        {
                            Obj.CustomerAccountManager.Remove(detail);
                        }

                        Obj.SaveChanges();
                        var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();

                        Obj.spAmPortalActivityLog(loggeduser, "Account Manager " + accountmanager + " Deleted for The Customer " + customer);
                        ViewBag.message = "Account Manager "+ accountmanager+" Deleted for The Customer " + customer;
                        return RedirectToAction("AccountManagerList", new
                        {

                            message = "Account Manager " + accountmanager + " Deleted for The Customer " + customer,
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("AccountManagerList", new
                    {

                        message = " Failed to Delete Account Manager for the Customer " + customer,
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {
                return RedirectToAction("AccountManagerList", new
                {

                    message = " Failed to Delete Account Manager ",
                    updatestatus = 2
                });
            }

        }


        public ActionResult SynchronizePortfolios()
        {
            var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();

            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {



                Obj.SyncronizeAccountManagers();
                Obj.spAmPortalActivityLog(loggeduser, "Account Manger Portfolio has been Syncronized");
                ViewBag.message = "Account Managers Portfolio has been Syncronized. ";
              
            }

            return RedirectToAction("AccountManagerList"); 

        }

        [HttpPost]

        public ActionResult SynchronizePortfolios(string var) 
        {
            try
            {
                string customer = "";
                string accountmanager = "";
                var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();
              
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {



                        Obj.SyncronizeAccountManagers();
                        Obj.spAmPortalActivityLog(loggeduser, "Account Manger Portfolio has been Syncronized");
                        ViewBag.message = "Account Managers Portfolio has been Syncronized. " ;
                        return RedirectToAction("AccountManagerList", new
                        {

                            message = "Account Managers Portfolio has been Syncronized. ",
                            updatestatus = 1
                        });

                    }
               
            }
            catch (Exception ex)
            {
                return RedirectToAction("AccountManagerList", new
                {

                    message = " Failed to synchronize portfolio  ",
                    updatestatus = 2
                });
            }

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
            FormsAuthentication.SignOut();
            return RedirectToAction("Index", "CustomerAtRisk");
        }

        public ActionResult NotAuthorized()
        {

            return View();
        }

        [HttpPost]
    
        public ActionResult Logout(string user="NA")
        {
            ViewBag.DisplayForAdmin = "hidden";
            ViewBag.DisplayLinkStatus = "hidden";
            FormsAuthentication.SignOut();
            return RedirectToAction("Login");
        }
        
        public ActionResult ChangePassword()
        {
            //ViewBag.DisplayLinkStatus = "hidden";
            return View();
        }

        public ActionResult CustomerStat(DateTime? startdate, DateTime? enddate)
        {
          
                ViewBag.DisplayLinkStatus = "hidden";
                ViewBag.DisplayForAdmin = "hidden";
                TempData["Utype"] = 0;

                return RedirectToAction("NotAuthorized", "Home");

           

        }



        public ActionResult ManageAccountManager(long Id)
        {
            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
                var am = Obj.vCustomerAccountManager.Where(a => a.Id == Id).FirstOrDefault();
                var accountmanager = am.AccountManager;
                ViewBag.CurrentAM = accountmanager;
                ViewBag.AccountManagers =
              new SelectList(Obj.vAccountManagerLogin.GroupBy(x => x.AccountManager).Select(x => x.FirstOrDefault()).Where(x=>x.UserType!=1).ToList().
              Select(x => new { Id = x.AccountManager, AccountManager = x.AccountManager }), "Id", "AccountManager ", accountmanager.Trim()); // Viewbag
                return View(am);
            }
           
        }

        [HttpPost]

      
        public ActionResult ManageAccountManager(CustomerAccountManager cma)
        {
            try
            {
                string customer = "";
                if (cma != null)
                {
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {
                        var cma_ = Obj.Entry(cma);
                        CustomerAccountManager cmaObj = Obj.CustomerAccountManager.Where(x => x.Id == cma.Id).FirstOrDefault();

                        var LoginObj = Obj.vAccountManagerLogin.Where(x => x.AccountManager == cma.AccountManager).FirstOrDefault();
                        customer = cmaObj.CustomerCode;
                        cmaObj.AccountManager = cma.AccountManager == null ? null : cma.AccountManager;
                        cmaObj.LoginName = LoginObj.LoginName == null ? null : LoginObj.LoginName;


                        Obj.SaveChanges();
                        var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();

                        Obj.spAmPortalActivityLog(loggeduser, "New Account Manager  " + cma.AccountManager + " has assigned to The Customer " + customer);

                        ViewBag.message = "New Account Manager  "+ cma.AccountManager+" has assigned to The Customer " + customer;
                        return RedirectToAction("AccountManagerList", new
                        {
                            message = ViewBag.message = "New Account Manager " + cma.AccountManager + " has assigned to the customer " + customer,
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("AccountManagerList", new
                    {

                        message = " Failed to update Account manager for the Customer " + customer,
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {

                return RedirectToAction("AccountManagerList", new
                {

                    message = "  Failed to update Account manager for the Customer due to  " +ex.Message,
                    updatestatus = 2
                }); ;
            }

        }

      
        public ActionResult EditUser(long Id)
        {
            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
                var am = Obj.vAccountManagerLogin.Where(a => a.Id == Id).FirstOrDefault();
                var accountmanager = am.AccountManager;
                ViewBag.CurrentAM = accountmanager;
                ViewBag.UserStatus =
 new SelectList(
new List<SelectListItem>
{

        new SelectListItem { Selected = true, Text = "Account Manager", Value = "0"},
            new SelectListItem { Selected = false, Text = "Admin", Value = "1"},
         new SelectListItem { Selected = false, Text = "Super Admin", Value = "2"},

}, "Value", "Text", 0);
                // ViewBag.AccountManagers =
                //new SelectList(Obj.AccountManagerLogin.GroupBy(x => x.AccountManager).Select(x => x.FirstOrDefault()).ToList().
                //Select(x => new { Id = x.AccountManager, AccountManager = x.AccountManager }), "Id", "AccountManager ", accountmanager.Trim()); // Viewbag
                return View(am);
            }

        }

        [HttpPost]
        public ActionResult EditUser(AccountManagerLogin cma)
        {
            try
            {
                string customer = "";
                if (cma != null)
                {
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {
                        var cma_ = Obj.Entry(cma);
                        AccountManagerLogin cmaObj = Obj.AccountManagerLogin.Where(x => x.Id == cma.Id).FirstOrDefault();

                        cmaObj.AccountManager = cma.AccountManager;
                        cmaObj.LoginName = cma.LoginName;
                        cmaObj.UserType = cma.UserType;

                        Obj.SaveChanges();
                        var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();

                        Obj.spAmPortalActivityLog(loggeduser, "User  " + cma.AccountManager + " has been updated");

                        ViewBag.message = "User  " + cma.AccountManager + " has been updated";
                        return RedirectToAction("UserList", new
                        {

                            message = ViewBag.message = "User  " + cma.AccountManager + " has been updated",
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

        
        public ActionResult DeleteUser(long Id)
        {
            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
                var am = Obj.vAccountManagerLogin.Where(a => a.Id == Id).FirstOrDefault();
                var accountmanager = am.AccountManager;
                ViewBag.CurrentAM = accountmanager;
                // ViewBag.AccountManagers =
                //new SelectList(Obj.AccountManagerLogin.GroupBy(x => x.AccountManager).Select(x => x.FirstOrDefault()).ToList().
                //Select(x => new { Id = x.AccountManager, AccountManager = x.AccountManager }), "Id", "AccountManager ", accountmanager.Trim()); // Viewbag
                return View(am);
            }

        }

        [HttpPost]

        
        public ActionResult DeleteUser(AccountManagerLogin cma)
        {
            try
            {

                if (cma != null)
                {
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {

                        AccountManagerLogin cmaObj = Obj.AccountManagerLogin.Where(x => x.Id == cma.Id).FirstOrDefault();

                      
                            cmaObj.IsDeleted = 1;

                            Obj.SaveChanges();

                        var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();

                        Obj.spAmPortalActivityLog(loggeduser, "User  " + cmaObj.AccountManager + " has Deleted Sucessfully");


                        ViewBag.message = "User  " + cmaObj.AccountManager + " has Deleted Sucessfully";
                            return RedirectToAction("UserList", new
                            {

                                message = ViewBag.message = "User  " + cmaObj.AccountManager + " has Deleted Sucessfully",
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
           
                if (id != null)
                {
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {
                        
                        AccountManagerLogin cmaObj = Obj.AccountManagerLogin.Where(x => x.Id == id).FirstOrDefault();

                        if(cmaObj.IsActive==1)
                        {
                            cmaObj.IsActive = 0;

                            Obj.SaveChanges();

                            var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();

                            Obj.spAmPortalActivityLog(loggeduser, "User  " + cmaObj.AccountManager + " has Disabled Sucessfully");

                            ViewBag.message = "User  " + cmaObj.AccountManager + " has Disabled Sucessfully";
                            return RedirectToAction("UserList", new
                            {

                                message = ViewBag.message = "User  " + cmaObj.AccountManager + " has Disabled Sucessfully",
                                updatestatus = 1
                            });
                        }

                        else
                        {
                            cmaObj.IsActive = 1;

                            Obj.SaveChanges();

                            var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();

                            Obj.spAmPortalActivityLog(loggeduser, "User  " + cmaObj.AccountManager + " has Enabled Sucessfully");

                            ViewBag.message = "User  " + cmaObj.AccountManager + " has Enabled Sucessfully";
                            return RedirectToAction("UserList", new
                            {

                                message = ViewBag.message = "User  " + cmaObj.AccountManager + " has Enabled Sucessfully",
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

                        AccountManagerLogin cmaObj = Obj.AccountManagerLogin.Where(x => x.Id == id).FirstOrDefault();

                    
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
        
        public ActionResult CreateUser(AccountManagerLogin aml)
        {
            string customer = "";
            try
            {
                if (aml.LoginName != null && aml.AccountManager != null)
                {

                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {
                        var loginname = Obj.AccountManagerLogin.Where(x => x.LoginName == aml.LoginName).FirstOrDefault();
                        if (loginname==null)
                        {
                            Obj.AccountManagerLogin.Add(aml);
                            Obj.SaveChanges();
                            var user = Obj.AccountManagerLogin.Where(x => x.LoginName == aml.LoginName).FirstOrDefault();
                            user.IsActive = 1;
                            user.IsDeleted = 0;
                            user.IsFirstLogin = false;
                            user.IsFormLogin = false;
                            user.Password ="123A123a";
                            user.ModifiedDate = DateTime.Today;
                            user.UserType = aml.UserType;
                            Obj.SaveChanges();

                            var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();

                            Obj.spAmPortalActivityLog(loggeduser, "User Added by the name " + aml.AccountManager);

                            ViewBag.message = "User Added by the name " + aml.AccountManager;
                            return RedirectToAction("UserList", new
                            {
                                message = "User Added by the name " + aml.AccountManager,
                                updatestatus = 1
                            });
                        }
                        else
                        {

                            ViewBag.message = "User already exisits under the login " +loginname;
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

                    return RedirectToAction("AccountManagerList", new
                    {

                        message = " Failed to add account manager due to missing information",
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

        
        public ActionResult CreateAccountManager()
        {
            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
              
                ViewBag.AccountManagers =
              new SelectList(Obj.vAccountManagerLogin.GroupBy(x => x.AccountManager).Select(x => x.FirstOrDefault()).Where(a => a.UserType!=1).ToList().
              Select(x => new { Id = x.AccountManager, AccountManager = x.AccountManager }), "Id", "AccountManager ", ""); // Viewbag

                ViewBag.Customers =
              new SelectList(Obj.vCustomerWithoutManager.GroupBy(x => x.CustomerCode).Select(x => x.FirstOrDefault()).ToList().
              Select(x => new { Id = x.CustomerCode, CustomerCode = x.CustomerName }), "Id", "CustomerCode ", ""); // Viewbag

                return View();



            }

        }

        [HttpPost]
        
        public ActionResult CreateAccountManager(CustomerAccountManager cam)
        {
            string customer = "";
            try
            {
                if (cam.CustomerCode != null && cam.AccountManager != null)
                {
                   
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {
                        var loginname = Obj.vAccountManagerLogin.Where(x => x.AccountManager == cam.AccountManager).FirstOrDefault().LoginName;
                        cam.LoginName = loginname;
                        Obj.CustomerAccountManager.Add(cam);
                        Obj.SaveChanges();
                        var loggeduser = HttpContext.User.Identity.Name.Split('\\').Last();

                        Obj.spAmPortalActivityLog(loggeduser, "Account Manager Added for the Customer " + cam.CustomerCode + " Account Manager is: " + cam.AccountManager);

                        ViewBag.message = "Account Manager Added for the Customer " + cam.CustomerCode;
                        return RedirectToAction("AccountManagerList", new
                        {
                            message = "Account Manager Added for the Customer " + cam.CustomerCode,
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("AccountManagerList", new
                    {

                        message = " Failed to add account manager ",
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {
                return RedirectToAction("AccountManagerList", new
                {

                    message = " Failed to create customer's comment ",
                    updatestatus = 2
                }); ;
            }


        }

        public IEnumerable<vCustomerAccountManager> Get_AllAccountManagers()
        {
            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
                string username = (string)Session["Uname"];
                int utype = (int)Session["Utype"];

                if (utype==2)
                {
                    ViewBag.DisplayForSuperAdmin = "visible";
                }
                else
                {
                    ViewBag.DisplayForSuperAdmin = "hidden";
                }

              

                if (username != null && utype != 0)
                  
                   
                {
                    return Obj.vCustomerAccountManager.ToList<vCustomerAccountManager>();
                }
                else
                {
                    return Obj.vCustomerAccountManager.Where(a => a.AccountManager == "Test222").ToList<vCustomerAccountManager>();
                }

            }
        }

        
        public ActionResult AccountManagerList(string message, int? updatestatus)
        {
            
            AFSXchange1Entities usersEntities = new AFSXchange1Entities();
            ViewBag.DisplayForSuperAdmin = "hidden";
            string loggeduser = "";
            if (TempData["LoggedAs"] == null && Session["LoggedAs"] == null)
            {
                loggeduser = HttpContext.User.Identity.Name.Split('\\').Last(); // "sallan";
                if(Session["NewVisit"]==null)
                {
                    usersEntities.spAmPortalActivityLog(loggeduser, "User  " + loggeduser + " has re-visited portal");
                    Session["NewVisit"] = "Visited";
                }
             
            }
            else if (TempData["LoggedAs"] != null)
            {
                loggeduser = (string)TempData["LoggedAs"];
            }
            else
            {
                loggeduser = (string)Session["LoggedAs"];
            }

           
            var userinfo = usersEntities.vAccountManagerLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
            TempData["UserName"] = userinfo.AccountManager;
            if (DateTime.Now.Hour < 12)
            {
                ViewBag.lblGreetingAdmin = "Good Morning ";

            }
            else if (DateTime.Now.Hour < 17)
            {
                ViewBag.lblGreetingAdmin = "Good Afternoon ";

            }
            else
            {
                ViewBag.lblGreetingAdmin = "Good Evening ";

            }
            if (message==null)
            {
                ViewBag.ViewMessage = "hidden";
            }
            else if (updatestatus >= 1)
            {
                TempData["message"] = message;
                ViewBag.Message = message;
                ViewBag.messageStatus = updatestatus;
                ViewBag.ViewMessage="visible";
            }
            else
            {
                TempData["message"] = null;
                ViewBag.messageStatus = 0;
                ViewBag.Message = null;
                ViewBag.ViewMessage = "visible";
            }

           

            return View(Get_AllAccountManagers());
           
        }

        
        public ActionResult UserList(string message, int? updatestatus)
        {
            if (message == null)
            {
                ViewBag.ViewMessage = "hidden";
            }
            else if(updatestatus >= 1)
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

        public IEnumerable<vAccountManagerLogin> Get_AllUsers()
        {
            using (AFSXchange1Entities Obj = new AFSXchange1Entities())
            {
                string username = (string)Session["Uname"];
               
                int utype = (int)Session["Utype"];

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
                    return Obj.vAccountManagerLogin.Where(a=>a.UserType==0).ToList<vAccountManagerLogin>();
                }

                else if (username != null && utype == 2)
                {
                    return Obj.vAccountManagerLogin.ToList<vAccountManagerLogin>();
                }
                else
                {
                    return Obj.vAccountManagerLogin.Where(a => a.AccountManager == "Test222").ToList<vAccountManagerLogin>();
                }

            }
        }

        [HttpPost]
        
        public ActionResult ChangePassword(User user)
        {
            string LoginName = (string)Session["LoginName"];

            try
            {
                if(user.Password=="123A123a")
                {
                    ViewBag.messageStatus = 2;

                    TempData["chngpwdmessage"] = "Sorry - Please Select another Password!!";
                    return View();

                }
                else if(!(user.Password ==user.ConfirmPassword))
                {
                    ViewBag.messageStatus = 2;

                    TempData["chngpwdmessage"] = "Sorry - Passwords are not matched!!";
                    return View();
                }
                else
                {
                    using (AFSXchange1Entities Obj = new AFSXchange1Entities())
                    {

                        // Obj.AccountManagerLogin.Where(x => x.LoginName.Trim() == LoginName).FirstOrDefault();

                        AccountManagerLogin AccountManagerLoginObj = Obj.AccountManagerLogin.Where(x => x.LoginName.Trim() == LoginName).FirstOrDefault();
                        AccountManagerLoginObj.Password = user.Password;
                        AccountManagerLoginObj.IsFirstLogin = false;
                        Obj.SaveChanges();
                        FormsAuthentication.SetAuthCookie(LoginName, user.RememberMe);
                        //ViewBag.messageStatus = 1;
                        //TempData["chngpwdmessage"] = "Password Changed Successfully ";
                        //return View();

                        ViewBag.message = "Password Changed Successfully";
                        return RedirectToAction("Index", "CustomerAtRisk", new
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
    }
}