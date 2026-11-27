using AFSDataGateway.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.Data.Entity;

namespace AFSDataGateway.Controllers
{

    public class IVTController : Controller
    {
        // GET: IVT
        IVTEntities ivtEntities = new IVTEntities();

        AFSXchange1Entities usersEntities = new AFSXchange1Entities();

    
        [HttpPost]
        public async Task<ActionResult> CurrentConsignment(FormCollection formCollection)
        {
            string loggedUser = GetLoggedUser() ?? WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            var comment = formCollection["Reason"]?.ToString();

            if (string.IsNullOrEmpty(loggedUser))
            {
                return RedirectToAction("Home", "Dashboard"); 
            }

            if (formCollection["ID"] == null && string.IsNullOrEmpty(formCollection["search"]))
            {
                return RedirectToAction("CurrentConsignment", new { message = "Please Enter Search word OR Select Connote(s) First!!", updatestatus = 2 });
            }

            if (formCollection["ID"] != null)
            {
                string[] ids = formCollection["ID"].Split(new char[] { ',' });
               

                var selectedCustomerGroupValue = formCollection["selectedCustomerGroupValue"].ToString();
                var selectedCustomerValues = formCollection["selectedCustomerValue"]; // This might be a collection
                var selectedCarrierValues = formCollection["selectedCarrierValue"]; // This might be a collection
                var selectedWorkCodeValues = formCollection["selectedWorkCodeValue"]; // This might be a collection


                ViewBag.selectedCustomerGroup = selectedCustomerGroupValue;

                // For multiple value selections, converting them to arrays or lists as necessary
                // Ensure you check for null or use ?. operator to avoid NullReferenceException
                string[] customerFilter = selectedCustomerValues.ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                string[] carrierFilter = selectedCarrierValues.ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                string[] workcodeFilter = selectedWorkCodeValues.ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                // Then, you can use these arrays as needed, for example, setting them in ViewBag
                ViewBag.selectedCustomer = string.Join(", ", customerFilter);
                ViewBag.selectedCarrier = string.Join(", ", carrierFilter);
                ViewBag.selectedWorkCode = string.Join(", ", workcodeFilter);


                using (var dbContext = new IVTEntities())
                {
                    var heldConnoteIds = ids.Select(long.Parse).ToList();
                    var heldConnotes = await dbContext.vIVT_Result
                                                     .Where(c => heldConnoteIds.Contains(c.Id))
                                                     .ToListAsync();

                    if (!heldConnotes.Any())
                    {
                        return RedirectToAction("CurrentConsignment", new { message = "No connotes found to hold", updatestatus = 2 });
                    }

                    foreach (var heldConnote in heldConnotes)
                    {
                        dbContext.ConsignmentForReview.Add(new ConsignmentForReview
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
                            Comment = comment,
                            ActionedBy = loggedUser,
                            ActionTime = DateTime.Now,
                        });
                    }

                    await dbContext.SaveChangesAsync();
               

                    // Send notification and log activity
                    string subject = "IVT Hold Notification - " + loggedUser.ToUpper();
                    string emailBody = "Hi IVT Admins,<br><br>" + loggedUser.ToUpper() + " has <b>HELD</b> the following connotes:<br><br>";
                    string activityComment = "Held Details - ";
                    // Start the table with headers
                    emailBody += "<table style='border-collapse: collapse; width: 100%;'>" +
                                 "<tr style='background-color: #f2f2f2;'>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Connote</th>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Held By</th>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Customer</th>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Comment</th>" +
                                 "</tr>";

                    // Assuming consignmentsToHold is obtained earlier in your method
                    foreach (var detail in heldConnotes)
                    {
                        emailBody += $"<tr>" +
                                     $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{detail.Connote}</td>" +
                                     $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{loggedUser}</td>" +
                                     $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{detail.customer}</td>" +
                                     $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{comment}</td>" +
                                     $"</tr>";
                        activityComment += $"Connote: {detail.Connote}, Held By: {loggedUser}, Customer: {detail.customer}, Comment: {heldConnotes}; ";
                    }

                    // Close the table
                    emailBody += "</table>";

                    emailBody += "<br><br>Please make sure to reflect the changes in the billing system and <b>ALWAYS</b> check the complete list in IVT2.0 System.<br><br>This is an Automated System Notification.<br><br>Invoice Vetting Tool (IVT2.0) <br>";

                    await SendNotificationAndLogActivity(subject, emailBody, activityComment, loggedUser, "Held Connote");

                    // await SendHeldNotificationAndLogActivity(loggedUser, ids, comment);
                }

             



                return RedirectToAction("CurrentConsignment", new { message = "Selected connotes have been held successfully", updatestatus = 1, customerGroupFilter = selectedCustomerGroupValue, customerFilter = selectedCustomerValues, carrierFilter = selectedCarrierValues, workcodeFilter = selectedWorkCodeValues });
            }
            else if (!string.IsNullOrEmpty(formCollection["search"]))
            {
                return RedirectToAction("CurrentConsignment", new { search = formCollection["search"] });
            }

            return RedirectToAction("CurrentConsignment");
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



        [HttpPost]
        public async Task<JsonResult> AddLogComment(string JobNumber, string LogText, string LogType)
        {
            try
            {
                string loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();

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


        [HttpPost]
        public async Task<ActionResult> VettingCompleted(string Comment)
        {
            try
            {
                string loggeduser = GetLoggedUser();
                if (string.IsNullOrEmpty(loggeduser))
                {
                    loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
                }

                // var ActionedBy = WindowsIdentity.GetCurrent().Name.Split('\\').Last().ToUpper();
                var ActionedBy = loggeduser.ToUpper();

                 IVTEntities Obj = new IVTEntities();

                using (var context = new IVTEntities()) // Assuming IVTEntities is your DbContext
                {
                    var activityParam = new SqlParameter("@Activity", "Vetting Completed");
                    var loginNameParam = new SqlParameter("@LoginName", ActionedBy ?? (object)DBNull.Value); // Handling null
                    var commentParam = new SqlParameter("@Comment", Comment ?? (object)DBNull.Value); // Handling null

                    // Execute the stored procedure
                    int result = context.Database.ExecuteSqlCommand("EXEC [WebIVT].[sp_New_AM_Activity] @Activity, @LoginName, @Comment", activityParam, loginNameParam, commentParam);

                    // result holds the number of rows affected, if needed
                }

               // Obj.sp_New_AM_Activity("Vetting Completed", ActionedBy,Comment);

                //-----------Send Notification------------------------------------
               

                string body = "Hi IVT Admins,<br><br>";

            

                body += ActionedBy + " has <b>Completed</b> the vetting process.<br><br>";
               
                body += "<br>Account Manager Comment: " +Comment +".<br><br><br><br><br><br><br> ";

                body += "This is an Automated System Notification.<br><br> ";
                body += "Invoice Vetting Tool (IVT2.0) <br> ";

                await EmailNotification.SendNotificationEmailAsync("Vetting Completetion Notification - " + ActionedBy, body, loggeduser);

                return RedirectToAction("CurrentConsignment", new
                {
                    message = "Your Confirmation has been sent successfully",
                    updatestatus = 1
                });

            }
            catch (Exception ex)
            {

                return RedirectToAction("CurrentConsignment", new
                {
                    message = "Failed to send message - Error: " + ex.Message,
                    updatestatus = 2
                });
            }
        }

        //IVT Result
        public ActionResult CurrentConsignment(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate,
                                          string customerGroupFilter, string[] customerFilter, string[] carrierFilter, string[] workcodeFilter, string minMargin, string maxMargin)
        {
            try
            {

                string loggedUser = GetLoggedUser();

                if (string.IsNullOrEmpty(loggedUser))
                    loggedUser = GetWindowsIdentityName();

                SetViewBagOptions(loggedUser,message,updatestatus);

                var userinfo = GetUserInformation(loggedUser);

                if (userinfo == null || userinfo.IsActive != 1)
                    return HandleInactiveUser(userinfo);

                SetViewBagOptionsForActiveUser(userinfo.LoginName);
                IVTEntities Obj = new IVTEntities();

                IQueryable<vIVT_Result> cons = GetCurrentConsignmentResults(loggedUser, search, customerGroupFilter, customerFilter, carrierFilter, workcodeFilter, minMargin, maxMargin);

                SetUniqueValuesInViewBag_CurrentConsignment(Obj, loggedUser, cons);

                return View(cons.ToList());
            }
            catch (Exception ex)
            {
                // Log the exception and handle it appropriately
                return RedirectToAction("TimeOut", "Home");
            }
        }


        public ActionResult AncillarySummary(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
             , string customerGroupFilter, string customerFilter, string carrierFilter, string workcodeFilter, string minMargin, string maxMargin)
        {
            try
            {

                string loggedUser = GetLoggedUser();

                if (string.IsNullOrEmpty(loggedUser))
                    loggedUser = GetWindowsIdentityName();

                SetViewBagOptions(loggedUser, message, updatestatus);

                var userinfo = GetUserInformation(loggedUser);

                if (userinfo == null || userinfo.IsActive != 1)
                    return HandleInactiveUser(userinfo);

                SetViewBagOptionsForActiveUser(userinfo.LoginName);
                IVTEntities Obj = new IVTEntities();

                IQueryable<vAllCurrentAncillarySummary> cons = GetAncillarySummaryResults(loggedUser,  message, updatestatus,  loginname,  loggedas,  search,  despatchDate
             ,  customerGroupFilter,  customerFilter,  carrierFilter,  workcodeFilter,  minMargin,  maxMargin);

                SetUniqueValuesInViewBag_AncillarySummary(Obj, loggedUser);

                return View(cons.ToList());
            }
            catch (Exception ex)
            {
                // Log the exception and handle it appropriately
                return RedirectToAction("TimeOut", "Home");
            }
        }



        public ActionResult ViewAncillarySummaryDetails(string customer, string carrier, string workcode,string message,int? updatestatus)
        {
            try
            {

                string loggedUser = GetLoggedUser();

                if (string.IsNullOrEmpty(loggedUser))
                    loggedUser = GetWindowsIdentityName();

                SetViewBagOptions(loggedUser, message, updatestatus);

                var userinfo = GetUserInformation(loggedUser);

                if (userinfo == null || userinfo.IsActive != 1)
                    return HandleInactiveUser(userinfo);

                SetViewBagOptionsForActiveUser(userinfo.LoginName);
  

                // Use using statement to ensure proper disposal of DbContext
                using (IVTEntities Obj = new IVTEntities())
                {
                    IQueryable<vAllCurrentDetails> cons = GetAncillarySummaryDetailResults(customer, carrier, workcode, Obj);

                    return View(cons.ToList());
                }

                // SetUniqueValuesInViewBag_AncillarySummary(Obj, loggedUser);

            
            }
            catch (Exception ex)
            {
                // Log the exception and handle it appropriately
                return RedirectToAction("TimeOut", "Home");
            }
        }

        public  ActionResult TradingData(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
            , string customerGroupFilter, string[] customerFilter, string[] carrierFilter, string[] workcodeFilter, string SupplierRef, string SellNett)
        {

            try
            {

                string loggedUser = GetLoggedUser();
                //if (Session["LoggedAs"] != null)
                //{
                //    ViewBag.DisplayForSuperAdmin = "visible";
                //}
                //else
                //{
                //    ViewBag.DisplayForSuperAdmin = "hidden";
                //}

                //ViewBag.LoggedUser = loggedUser;
                //ViewBag.LoggedUserStatus = "visible";


                if (string.IsNullOrEmpty(loggedUser))
                    loggedUser = GetWindowsIdentityName();

                SetViewBagOptions(loggedUser, message, updatestatus);

                var userinfo = GetUserInformation(loggedUser);

                if (userinfo == null || userinfo.IsActive != 1)
                    return HandleInactiveUser(userinfo);

                SetViewBagOptionsForActiveUser(userinfo.LoginName);
                IVTEntities Obj = new IVTEntities();


                IEnumerable<vAllCurrentDetails> cons =  GetTradingDataResults(loggedUser, message, updatestatus, loginname, loggedas, search, despatchDate,
                                        customerGroupFilter, customerFilter, carrierFilter, workcodeFilter, SupplierRef, SellNett);

                SetUniqueValuesInViewBag_TradingData(Obj, loggedUser, cons);

                return View(cons.ToList());
            }
            catch (Exception ex)
            {
                // Log the exception and handle it appropriately
                return RedirectToAction("TimeOut", "Home");
            }

        }

        private void SetUniqueValuesInViewBag_CurrentConsignment(IVTEntities Obj, string loggedUser, IQueryable<vIVT_Result> cons)
        {
            IQueryable<vIVT_Result> cons2 = Obj.vIVT_Result;
            var UniqueCustomerGroup = cons2
                                       .Where(x => x.LoginName.Equals(loggedUser))
                                       .Select(x => x.FinanceGroup)
                                       .Distinct().OrderBy(x => x)
                                       .ToList();

            var uniqueCustomers = cons
                                   .Where(x => x.LoginName.Equals(loggedUser))
                                   .Select(x => x.customer)
                                   .Distinct().OrderBy(x => x)
                                   .ToList();

            var uniqueCarriers = cons
                                  .Where(x => x.LoginName.Equals(loggedUser))
                                  .Select(x => x.carrier)
                                  .Distinct().OrderBy(x => x)
                                  .ToList();

            var uniqueWorkcodes = cons
                                    .Where(x => x.LoginName.Equals(loggedUser))
                                    .Select(x => x.work_code)
                                    .Distinct().OrderBy(x => x)
                                    .ToList();

            ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
            ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
            ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
            ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);
        }


        private void SetUniqueValuesInViewBag_TradingData(IVTEntities Obj, string loggedUser, IEnumerable<vAllCurrentDetails> cons)
        {
            IQueryable<vAllCurrentDetails> cons2 = Obj.vAllCurrentDetails;

            var UniqueCustomerGroup = cons2
           .Where(x => x.LoginName.Equals(loggedUser))
           .Select(x => x.FinanceGroup)
           .Distinct().OrderBy(x => x)
           .ToList();


            var uniqueCustomers = cons
              .Where(x => x.LoginName.Equals(loggedUser))
              .Select(x => x.Customer)
              .Distinct().OrderBy(x => x)
              .ToList();

            var uniqueCarriers = cons.Where(x => x.LoginName.Equals(loggedUser)).Select(x => x.Carrier).Distinct().OrderBy(x => x).ToList();
            var uniqueWorkcodes = cons.Where(x => x.LoginName.Equals(loggedUser)).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();


            ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
            ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
            ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
            ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);
        }


        private void SetUniqueValuesInViewBag_AncillarySummary(IVTEntities Obj, string loggedUser)
        {
            IQueryable<vAllCurrentAncillarySummary> cons = Obj.vAllCurrentAncillarySummary;

            var uniqueCustomers = Obj.vAllCurrentAncillarySummary
                  .Where(x => x.LoginName.Equals(loggedUser))
                  .Select(x => x.CustomerCode)
                  .Distinct().OrderBy(x => x)
                  .ToList();

            var uniqueCarriers = Obj.vAllCurrentAncillarySummary.Where(x => x.LoginName.Equals(loggedUser)).Select(x => x.Carrier).Distinct().OrderBy(x => x).ToList();
            var uniqueWorkcodes = Obj.vAllCurrentAncillarySummary.Where(x => x.LoginName.Equals(loggedUser)).Select(x => x.WorkCode).Distinct().OrderBy(x => x).ToList();



            var UniqueCustomerGroup = Obj.vAllCurrentAncillarySummary
            .Where(x => x.LoginName.Equals(loggedUser))
            .Select(x => x.FinanceGroup)
            .Distinct().OrderBy(x => x)
            .ToList();




            ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
            ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
            ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
            ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);
        }



        private void SetUniqueValuesInViewBag_HeldConsignment(IVTEntities Obj, string loggedUser)
        {
            IQueryable<vConsignmentForReview> cons = Obj.vConsignmentForReview;

            var uniqueCustomers = Obj.vConsignmentForReview
                  .Where(x => x.ActionedBy.Equals(loggedUser))
                  .Select(x => x.Customer)
                  .Distinct().OrderBy(x => x)
                  .ToList();

            var uniqueCarriers = Obj.vConsignmentForReview.Where(x => x.ActionedBy.Equals(loggedUser)).Select(x => x.Carrier).Distinct().OrderBy(x => x).ToList();
           


            var UniqueCustomerGroup = Obj.vConsignmentForReview
            .Where(x => x.ActionedBy.Equals(loggedUser))
            .Select(x => x.FinanceGroup)
            .Distinct().OrderBy(x => x)
            .ToList();




            ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
            ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
            ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
         
        }


        private string GetWindowsIdentityName()
        {
            return WindowsIdentity.GetCurrent().Name.Split('\\').Last();
        }

        private void SetViewBagOptions(string loggedUser,string message,int? updatestatus)
        {
            ViewBag.LoggedUser = loggedUser;
            ViewBag.LoggedUserStatus = "visible";

            if (DateTime.Now.Hour < 12)
                ViewBag.lblGreeting = "Good Morning ";
            else if (DateTime.Now.Hour < 17)
                ViewBag.lblGreeting = "Good Afternoon ";
            else
                ViewBag.lblGreeting = "Good Evening ";

            TempData["message"] = message;
            ViewBag.message = message;
            ViewBag.messageStatus = updatestatus;
            TempData["messageStatus"] = updatestatus; // 1 for success, 2 for failure

           



        }

        private vDWHEditorLogin GetUserInformation(string loggedUser)
        {
            return usersEntities.vDWHEditorLogin.FirstOrDefault(a => a.LoginName.ToUpper() == loggedUser.Trim().ToUpper());
        }

        private ActionResult HandleInactiveUser(vDWHEditorLogin userinfo)
        {
            if (userinfo != null && userinfo.IsActive == 0)
                return RedirectToAction("NotAuthorized", "Home", new { message = "Your user has been disabled. Please contact the administrator." });
            else
                return RedirectToAction("NotAuthorized", "Home", new { message = "You have no rights. Please contact support." });
        }

        private void SetViewBagOptionsForActiveUser(string username)
        {
            var userinfo = GetUserInformation(username);
            ViewBag.DisplayLinkStatus = "visible";
            ViewBag.DisplayForAdmin = userinfo.UserType == 1 ? "visible" : "hidden";
            ViewBag.DisplayForSuperAdmin = userinfo.UserType == 2 || (Session["LoggedAs"] != null ) ? "visible" : "hidden";

            FormsAuthentication.SetAuthCookie(username, true);
            TempData["UserName"] = username;
            Session["Uname"] = username;
            Session["Utype"] = userinfo.UserType;
            TempData["LoginName"] = username.Trim().ToUpper();
            Session["LoginName"] = username.Trim().ToUpper();
        }

        private IQueryable<vIVT_Result> GetCurrentConsignmentResults(string loggedUser, string search, string customerGroupFilter, string[] customerFilter, string[] carrierFilter, string[] workcodeFilter, string minMargin, string maxMargin)
        {
            ViewBag.selectedCustomerGroup = customerGroupFilter;
            ViewBag.selectedCustomer = customerFilter;
            ViewBag.selectedCarrier = carrierFilter;
            ViewBag.selectedWorkCode = workcodeFilter;

            IVTEntities Obj = new IVTEntities();
            IQueryable<vIVT_Result> cons = Obj.vIVT_Result.Where(x => x.LoginName.Equals(loggedUser));

            if (!string.IsNullOrEmpty(search))
                cons = cons.Where(x => x.con_note.Contains(search) || x.Reason.Contains(search) || x.customer.Contains(search) || x.ErrCode.Contains(search));

            if (!string.IsNullOrEmpty(customerGroupFilter))
                cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter));

            if (customerFilter!=null && customerFilter.Any())
                cons = cons.Where(x => customerFilter.Contains(x.customer));

          
            if (carrierFilter != null && carrierFilter.Any())
                cons = cons.Where(x => carrierFilter.Contains(x.carrier));


            if (workcodeFilter != null && workcodeFilter.Any())
                cons = cons.Where(x => workcodeFilter.Contains(x.work_code)); 




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

            if (string.IsNullOrEmpty(search) && customerFilter == null &&  carrierFilter == null &&  workcodeFilter == null
                && string.IsNullOrEmpty(minMargin) && string.IsNullOrEmpty(maxMargin) && string.IsNullOrEmpty(customerGroupFilter))
            {
                cons = cons.Where(x => x.HeldBy.Equals(loggedUser));
            }

            return cons.OrderByDescending(x => x.total_nett);
        }


        private IQueryable<vAllCurrentAncillarySummary> GetAncillarySummaryResults(string loggedUser ,string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
             , string customerGroupFilter, string customerFilter, string carrierFilter, string workcodeFilter, string minMargin, string maxMargin)
            { 
            ViewBag.selectedCustomerGroup = customerGroupFilter;
            ViewBag.selectedCustomer = customerFilter;
            ViewBag.selectedCarrier = carrierFilter;
            ViewBag.selectedWorkCode = workcodeFilter;

            IVTEntities Obj = new IVTEntities();
            IQueryable<vAllCurrentAncillarySummary> cons = Obj.vAllCurrentAncillarySummary.Where(x => x.LoginName.Equals(loggedUser));


            if (!string.IsNullOrEmpty(customerGroupFilter))
            {
                cons = cons.Where(x => x.LoginName.Equals(loggedUser) && x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
            }


            if (!string.IsNullOrEmpty(search))
            {
                cons = cons.Where(x => x.LoginName.Equals(loggedUser) && x.Carrier.Contains(search) || x.CustomerCode.Contains(search) || x.WorkCode.Contains(search));
            }

            // New filters
            if (!string.IsNullOrEmpty(customerFilter))
            {
                cons = cons.Where(x => x.LoginName.Equals(loggedUser) && x.CustomerCode.Contains(customerFilter));
            }

            if (!string.IsNullOrEmpty(carrierFilter))
            {
                cons = cons.Where(x => x.LoginName.Equals(loggedUser) && x.Carrier.Equals(carrierFilter));
            }

            if (!string.IsNullOrEmpty(workcodeFilter))
            {
                cons = cons.Where(x => x.LoginName.Equals(loggedUser) && x.WorkCode.Contains(workcodeFilter));
            }



            if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
               )
            {
                cons = cons.Where(x => x.LoginName.Equals(loggedUser)).OrderByDescending(x => x.CustomerCode);
            }

            cons = cons.OrderByDescending(x => x.CustomerCode);


            return cons;
        }




        private IQueryable<vConsignmentForReview> GetHeldConsignmentsResults(string loggedUser, string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
             , string customerGroupFilter, string customerFilter, string carrierFilter)
        {
            ViewBag.selectedCustomerGroup = customerGroupFilter;
            ViewBag.selectedCustomer = customerFilter;
            ViewBag.selectedCarrier = carrierFilter;
          

            IVTEntities Obj = new IVTEntities();
            IQueryable<vConsignmentForReview> cons = Obj.vConsignmentForReview.Where(x => x.ActionedBy.Equals(loggedUser));


            if (!string.IsNullOrEmpty(customerGroupFilter))
            {
                cons = cons.Where(x => x.ActionedBy.Equals(loggedUser) && x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
            }


            if (!string.IsNullOrEmpty(search))
            {
                cons = cons.Where(x => x.ActionedBy.Equals(loggedUser) && x.Carrier.Contains(search) || x.Customer.Contains(search) || x.Connote.Contains(search));
            }

            // New filters
            if (!string.IsNullOrEmpty(customerFilter))
            {
                cons = cons.Where(x => x.ActionedBy.Equals(loggedUser) && x.Customer.Contains(customerFilter));
            }

            if (!string.IsNullOrEmpty(carrierFilter))
            {
                cons = cons.Where(x => x.ActionedBy.Equals(loggedUser) && x.Carrier.Equals(carrierFilter));
            }

         



            if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter))
               
            {
                cons = cons.Where(x => x.ActionedBy.Equals(loggedUser)).OrderByDescending(x => x.ActionTime);
            }

            cons = cons.OrderByDescending(x => x.ActionTime);


            return cons;
        }



        public IQueryable<vAllCurrentDetails> GetAncillarySummaryDetailResults(string customer, string carrier, string workcode,IVTEntities Obj)
        {
            try
            {
                ViewBag.Customer = customer;
                ViewBag.Carrier = carrier;
                ViewBag.Workcode = workcode;

              
                    var matchedRecords = Obj.vAllCurrentDetails
                         .Where(a => a.Customer == customer && a.Carrier == carrier && a.work_code == workcode)
                         .ToList();

                    var matchedConnotes = matchedRecords.Select(a => a.ConsignmentNote).ToList();

                    var connotesWithoutSuffixes = matchedConnotes.Select(consignmentNote =>
                    {
                        int index = consignmentNote.Length - 1;
                        while (index >= 0 && char.IsLetter(consignmentNote[index]))
                        {
                            index--;
                        }
                        if (index < consignmentNote.Length - 1)
                        {
                            return consignmentNote.Substring(0, index + 1);
                        }
                        return consignmentNote;
                    });

                    var connotesWithoutSuffixesRecords = connotesWithoutSuffixes.Select(consignmentNote =>
                    {
                        var originalRecord = Obj.vAllCurrentDetails.FirstOrDefault(r => r.ConsignmentNote == consignmentNote);
                        if (originalRecord != null)
                        {
                            return new vAllCurrentDetails
                            {
                                PrimaryKeyField = originalRecord.PrimaryKeyField,
                                CurrentWeek = originalRecord.CurrentWeek,
                                AccountManager = originalRecord.AccountManager,
                                LoginName = originalRecord.LoginName,
                                ContactEmail = originalRecord.ContactEmail,
                                CustomerGroup = originalRecord.CustomerGroup,
                                Customer = originalRecord.Customer,
                                CarrierGroup = originalRecord.CarrierGroup,
                                Carrier = originalRecord.Carrier,
                                SupplierRef = originalRecord.SupplierRef,
                                ConsignmentNote = consignmentNote,
                                JobNumber = originalRecord.JobNumber,
                                col_post = originalRecord.col_post,
                                del_post = originalRecord.del_post,
                                Col_loc = originalRecord.Col_loc,
                                Del_loc = originalRecord.Del_loc,
                                Cons_Date = originalRecord.Cons_Date,
                                Moveit_DespatchDate = originalRecord.Moveit_DespatchDate,
                                Moveit_FreightStatus = originalRecord.Moveit_FreightStatus,
                                Moveit_LineOfBusiness = originalRecord.Moveit_LineOfBusiness,
                                Moveit_SpecialIns = originalRecord.Moveit_SpecialIns,
                                Moveit_ActualDeliveryDate = originalRecord.Moveit_ActualDeliveryDate,
                                Moveit_TotWeightKg = originalRecord.Moveit_TotWeightKg,
                                Moveit_TotVolM = originalRecord.Moveit_TotVolM,
                                Moveit_RateIncSurch = originalRecord.Moveit_RateIncSurch,
                                from = originalRecord.from,
                                to = originalRecord.to,
                                option_code = originalRecord.option_code,
                                work_code = originalRecord.work_code,
                                quantity = originalRecord.quantity,
                                space = originalRecord.space,
                                lift = originalRecord.lift,
                                weight = originalRecord.weight,
                                chg_kg = originalRecord.chg_kg,
                                pallet = originalRecord.pallet,
                                hours = originalRecord.hours,
                                cubic = originalRecord.cubic,
                                cus_ref = originalRecord.cus_ref,
                                Sell_Freight_Charges = originalRecord.Sell_Freight_Charges,
                                Sell_Other_Charges = originalRecord.Sell_Other_Charges,
                                Sell_Fuel_Surch = originalRecord.Sell_Fuel_Surch,
                                Sell_Total_Nett = originalRecord.Sell_Total_Nett,
                                Buy_Freight_Charges = originalRecord.Buy_Freight_Charges,
                                Buy_Other_Charges = originalRecord.Buy_Other_Charges,
                                Buy_Fuel_Surcharge = originalRecord.Buy_Fuel_Surcharge,
                                Buy_Total_Nett = originalRecord.Buy_Total_Nett,
                                Nett_Margin = originalRecord.Nett_Margin,
                                FSL___Sell = originalRecord.FSL___Sell,
                                C_FSL_Buy = originalRecord.C_FSL_Buy,
                                FSL___Var = originalRecord.FSL___Var,
                                ExternalComments = originalRecord.ExternalComments
                            };
                        }
                        return null;
                    }).Where(record => record != null);

                    var uniqueConnotes = matchedRecords.Concat(connotesWithoutSuffixesRecords).Distinct().AsQueryable();
                    return uniqueConnotes;
               
            }
            catch (Exception ex)
            {
                // Log the exception and handle it appropriately
                throw;
            }
        }


        private IEnumerable<vAllCurrentDetails> GetTradingDataResults(string loggedUser, string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate,
                                                                 string customerGroupFilter, string[] customerFilter, string[] carrierFilter, string[] workcodeFilter, string SupplierRef, string SellNett)
        {
           


            IVTEntities Obj = new IVTEntities();
            IQueryable<vAllCurrentDetails> cons = Obj.vAllCurrentDetails.Where(x => x.LoginName.Equals(loggedUser));

            ViewBag.selectedCustomerGroup = customerGroupFilter;
            ViewBag.selectedCustomer = customerFilter;
            ViewBag.selectedCarrier = carrierFilter;
            ViewBag.selectedWorkCode = workcodeFilter;

            if (!string.IsNullOrEmpty(customerGroupFilter))
            {
                cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter));
            }

            if (!string.IsNullOrEmpty(search))
            {
                cons = cons.Where(x => x.ConsignmentNote.Contains(search) || x.Carrier.Contains(search) || x.Customer.Contains(search) || x.work_code.Contains(search));
            }

            if (customerFilter != null && customerFilter.Any(s => !string.IsNullOrWhiteSpace(s)))
                cons = cons.Where(x => customerFilter.Contains(x.Customer));


            if (carrierFilter != null && carrierFilter.Any(s => !string.IsNullOrWhiteSpace(s)))
                cons = cons.Where(x => carrierFilter.Contains(x.Carrier));


            if (workcodeFilter != null && workcodeFilter.Any(s => !string.IsNullOrWhiteSpace(s)))
                cons = cons.Where(x => workcodeFilter.Contains(x.work_code));

            if (!string.IsNullOrEmpty(SupplierRef))
            {
                cons = cons.Where(x => x.SupplierRef.Contains(SupplierRef));
            }

            if (!string.IsNullOrEmpty(SellNett))
            {
                decimal SellNettValue = string.IsNullOrEmpty(SellNett) ? decimal.MaxValue : Convert.ToDecimal(SellNett);
                cons = cons.Where(x => x.Sell_Total_Nett >= SellNettValue);
            }

            bool isSearchEmpty = string.IsNullOrEmpty(search);
            bool isSupplierRefEmpty = string.IsNullOrEmpty(SupplierRef);
            bool isSellNettEmpty = string.IsNullOrEmpty(SellNett);
            bool isCustomerGroupFilterEmpty = string.IsNullOrEmpty(customerGroupFilter);

            bool areCustomerFiltersInvalid = customerFilter == null || !customerFilter.Any(s => !string.IsNullOrWhiteSpace(s));
            bool areCarrierFiltersInvalid = carrierFilter == null || !carrierFilter.Any(s => !string.IsNullOrWhiteSpace(s));
            bool areWorkcodeFiltersInvalid = workcodeFilter == null || !workcodeFilter.Any(s => !string.IsNullOrWhiteSpace(s));

            if (isSearchEmpty && areCustomerFiltersInvalid && areCarrierFiltersInvalid && areWorkcodeFiltersInvalid
                && isSupplierRefEmpty && isSellNettEmpty && isCustomerGroupFilterEmpty)
            {
                cons = cons.Where(x => x.LoginName.Equals(loggedUser)).OrderByDescending(x => x.Sell_Total_Nett).Take(500);
            }


            cons = cons.OrderByDescending(x => x.Sell_Total_Nett).Take(2000);

            // Pagination
           // cons = cons.Skip((pageNumber - 1) * pageSize).Take(pageSize);

            return cons.ToList();
        }



        private IEnumerable<vAllCurrentDetails> GetTradingDataResults_CSVDownload(string loggedUser, string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate,
                                                              string customerGroupFilter, string[] customerFilter, string[] carrierFilter, string[] workcodeFilter, string SupplierRef, string SellNett)
        {



            IVTEntities Obj = new IVTEntities();
            IQueryable<vAllCurrentDetails> cons = Obj.vAllCurrentDetails.Where(x => x.LoginName.Equals(loggedUser));

            ViewBag.selectedCustomerGroup = customerGroupFilter;
            ViewBag.selectedCustomer = customerFilter;
            ViewBag.selectedCarrier = carrierFilter;
            ViewBag.selectedWorkCode = workcodeFilter;

            if (!string.IsNullOrEmpty(customerGroupFilter))
            {
                cons = cons.Where(x => x.FinanceGroup.Equals(customerGroupFilter));
            }

            if (!string.IsNullOrEmpty(search))
            {
                cons = cons.Where(x => x.ConsignmentNote.Contains(search) || x.Carrier.Contains(search) || x.Customer.Contains(search) || x.work_code.Contains(search));
            }

            if (customerFilter != null && customerFilter.Any(s => !string.IsNullOrWhiteSpace(s)))
                cons = cons.Where(x => customerFilter.Contains(x.Customer));


            if (carrierFilter != null && carrierFilter.Any(s => !string.IsNullOrWhiteSpace(s)))
                cons = cons.Where(x => carrierFilter.Contains(x.Carrier));


            if (workcodeFilter != null && workcodeFilter.Any(s => !string.IsNullOrWhiteSpace(s)))
                cons = cons.Where(x => workcodeFilter.Contains(x.work_code));

            if (!string.IsNullOrEmpty(SupplierRef))
            {
                cons = cons.Where(x => x.SupplierRef.Contains(SupplierRef));
            }

            if (!string.IsNullOrEmpty(SellNett))
            {
                decimal SellNettValue = string.IsNullOrEmpty(SellNett) ? decimal.MaxValue : Convert.ToDecimal(SellNett);
                cons = cons.Where(x => x.Sell_Total_Nett >= SellNettValue);
            }

            bool isSearchEmpty = string.IsNullOrEmpty(search);
            bool isSupplierRefEmpty = string.IsNullOrEmpty(SupplierRef);
            bool isSellNettEmpty = string.IsNullOrEmpty(SellNett);
            bool isCustomerGroupFilterEmpty = string.IsNullOrEmpty(customerGroupFilter);

            bool areCustomerFiltersInvalid = customerFilter == null || !customerFilter.Any(s => !string.IsNullOrWhiteSpace(s));
            bool areCarrierFiltersInvalid = carrierFilter == null || !carrierFilter.Any(s => !string.IsNullOrWhiteSpace(s));
            bool areWorkcodeFiltersInvalid = workcodeFilter == null || !workcodeFilter.Any(s => !string.IsNullOrWhiteSpace(s));

            if (isSearchEmpty && areCustomerFiltersInvalid && areCarrierFiltersInvalid && areWorkcodeFiltersInvalid
                && isSupplierRefEmpty && isSellNettEmpty && isCustomerGroupFilterEmpty)
            {
                cons = cons.Where(x => x.LoginName.Equals(loggedUser)).OrderByDescending(x => x.Sell_Total_Nett);
            }


            cons = cons.OrderByDescending(x => x.Sell_Total_Nett);

            // Pagination
            // cons = cons.Skip((pageNumber - 1) * pageSize).Take(pageSize);

            return cons.ToList();
        }

        public ActionResult GetCarriers(string[] customers, string viewname)

        {
            string loggeduser = GetLoggedUser();
            IVTEntities Obj = new IVTEntities();

            if (viewname == "CurrentConsignment")
            {
                var carriers = Obj.vIVT_Result.Where(x => x.LoginName.Equals(loggeduser));

               

                if (customers != null && customers.Any())
                {
                    carriers = carriers.Where(x => customers.Contains(x.customer));
                }

                var result = carriers.Select(x => x.carrier)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();

               
                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname=="AncillarySummary")
            {
                var carriers = Obj.AncillarySummary.Where(x => x.LoginName.Equals(loggeduser));

                if (customers != null && customers.Any())
                {
                    carriers = carriers.Where(x => customers.Contains(x.CustomerCode));
                }

                var result = carriers.Select(x => x.Carrier)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "TradingData")
            {
                var carriers = Obj.vAllCurrentDetails.Where(x => x.LoginName.Equals(loggeduser));

                if (customers != null && customers.Any())
                {
                    carriers = carriers.Where(x => customers.Contains(x.Customer));
                }

                var result = carriers.Select(x => x.Carrier)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();

                return Json(result, JsonRequestBehavior.AllowGet);

            }

            else if (viewname == "HeldConsignment")
            {
                var carriers = Obj.vConsignmentForReview.Where(x => x.ActionedBy.Equals(loggeduser));

                if (customers != null && customers.Any())
                {
                    carriers = carriers.Where(x => customers.Contains(x.Customer));
                }

                var result = carriers.Select(x => x.Carrier)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();

                return Json(result, JsonRequestBehavior.AllowGet);

            }

            else if (viewname == "TradingDataHistory")
            {
                var carriers = Obj.IVT_TradingDataHistory.Where(x => x.LoginName.Equals(loggeduser));

                if (customers != null && customers.Any())
                {
                    carriers = carriers.Where(x => customers.Contains(x.Customer));
                }

                var result = carriers.Select(x => x.Customer)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else if (viewname == "ExceptionHistory")
            {
                var carriers = Obj.IVT_ResultHistory.Where(x => x.LoginName.Equals(loggeduser));

                if (customers != null && customers.Any())
                {
                    carriers = carriers.Where(x => customers.Contains(x.customer));
                }

                var result = carriers.Select(x => x.carrier)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();

                return Json(result, JsonRequestBehavior.AllowGet);

            }
            else
            {
                var carriers = Obj.vIVT_Result.Where(x => x.LoginName.Equals(loggeduser));

                if (customers != null && customers.Any())
                {
                    carriers = carriers.Where(x => customers.Contains(x.customer));
                }

                var result = carriers.Select(x => x.carrier)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();

                return Json(result, JsonRequestBehavior.AllowGet);

            }
               

         

           
        }

        public ActionResult GetCustomers(string customergroup, string viewname)
        {
            string loggeduser = GetLoggedUser();
            IVTEntities Obj = new IVTEntities();

            if (viewname == "CurrentConsignment")
            {
                var customers = Obj.vIVT_Result.Where(x => x.LoginName.Equals(loggeduser));

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
                var customers = Obj.vAllCurrentAncillarySummary.Where(x => x.LoginName.Equals(loggeduser));

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
                var customers = Obj.vAllCurrentDetails.Where(x => x.LoginName.Equals(loggeduser));

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
                var customers = Obj.vIVT_TradingDataHistory.Where(x => x.LoginName.Equals(loggeduser));

                if (!string.IsNullOrEmpty(customergroup))
                {
                    customers = customers.Where(x => x.FinanceGroup == customergroup);
                }

                var distinctcustomers = customers.Select(x => x.Customer).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }

            else if (viewname == "HeldConsignment")
            {
                var customers = Obj.vConsignmentForReview.Where(x => x.ActionedBy.Equals(loggeduser));

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
                var customers = Obj.vIVT_AllResultHistory.Where(x => x.LoginName.Equals(loggeduser));

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
                var customers = Obj.vIVT_Result.Where(x => x.LoginName.Equals(loggeduser));

                if (!string.IsNullOrEmpty(customergroup))
                {
                    customers = customers.Where(x => x.FinanceGroup == customergroup);
                }

                var distinctcustomers = customers.Select(x => x.customer).Distinct().OrderBy(x => x);

                var result = distinctcustomers.Select(c => new { value = c, text = c });

                return Json(result, JsonRequestBehavior.AllowGet);

            }





        }

        public ActionResult GetWorkcodes(string[] customer, string[] carrier,string viewname)
        {
       
            
           string loggeduser = GetLoggedUser();
            IVTEntities Obj = new IVTEntities();


            if (viewname == "CurrentConsignment") // Current Consignment
            {
                var workcodes = Obj.vIVT_Result.Where(x => x.LoginName.Equals(loggeduser) && !string.IsNullOrEmpty(x.work_code));
                // If both customer and carrier are null or empty, return unique workcodes for the logged user


                if (customer != null && customer.Any() && carrier != null && carrier.Any())
                {
                    // If both customer and carrier are provided, filter based on both

                    workcodes = workcodes.Where(x => customer.Contains(x.customer) && carrier.Contains(x.carrier) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);


                }
                else if (customer != null && customer.Any() && carrier == null)
                {
                    workcodes = workcodes.Where(x => customer.Contains(x.customer) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
                else if (carrier != null && carrier.Any() && customer == null)
                {
                    workcodes = workcodes.Where(x => carrier.Contains(x.carrier) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
                else
                {
                    // If none of the parameters are provided, return unique workcodes for the logged user
                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                 .Distinct()
                                 .OrderBy(x => x)
                                 .Select(c => new { value = c, text = c })
                                 .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);

                }


            }
            else if (viewname == "AncillarySummary")
            {
                var workcodes = Obj.AncillarySummary.Where(x => x.LoginName.Equals(loggeduser));
                // If both customer and carrier are null or empty, return unique workcodes for the logged user


                if (customer != null && customer.Any() && carrier != null && carrier.Any())
                {
                    // If both customer and carrier are provided, filter based on both

                    workcodes = workcodes.Where(x => customer.Contains(x.CustomerCode) && carrier.Contains(x.Carrier) && !string.IsNullOrEmpty(x.WorkCode));


                    var workcodes_Result = workcodes.Select(x => x.WorkCode)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);


                }
                else if (customer != null && customer.Any() && carrier == null)
                {
                    workcodes = workcodes.Where(x => customer.Contains(x.CustomerCode) && !string.IsNullOrEmpty(x.WorkCode));


                    var workcodes_Result = workcodes.Select(x => x.WorkCode)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
                else if (carrier != null && carrier.Any() && customer == null)
                {
                    workcodes = workcodes.Where(x => carrier.Contains(x.Carrier) && !string.IsNullOrEmpty(x.WorkCode));


                    var workcodes_Result = workcodes.Select(x => x.WorkCode)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
                else
                {
                    // If none of the parameters are provided, return unique workcodes for the logged user
                    var workcodes_Result = workcodes.Select(x => x.WorkCode)
                                 .Distinct()
                                 .OrderBy(x => x)
                                 .Select(c => new { value = c, text = c })
                                 .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);

                }

            }


            else if (viewname == "TradingData")
            {
                var workcodes = Obj.vAllCurrentDetails.Where(x => x.LoginName.Equals(loggeduser));
                // If both customer and carrier are null or empty, return unique workcodes for the logged user


                if (customer != null && customer.Any() && carrier != null && carrier.Any())
                {
                    // If both customer and carrier are provided, filter based on both

                    workcodes = workcodes.Where(x => customer.Contains(x.Customer) && carrier.Contains(x.Carrier) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);


                }
                else if (customer != null && customer.Any() && carrier == null)
                {
                    workcodes = workcodes.Where(x => customer.Contains(x.Customer) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
                else if (carrier != null && carrier.Any() && customer == null)
                {
                    workcodes = workcodes.Where(x => carrier.Contains(x.Carrier) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
                else
                {
                    // If none of the parameters are provided, return unique workcodes for the logged user
                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                 .Distinct()
                                 .OrderBy(x => x)
                                 .Select(c => new { value = c, text = c })
                                 .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);

                }

            }
            else if (viewname == "TradingDataHistory")
            {
                var workcodes = Obj.IVT_TradingDataHistory.Where(x => x.LoginName.Equals(loggeduser));
                // If both customer and carrier are null or empty, return unique workcodes for the logged user


                if (customer != null && customer.Any() && carrier != null && carrier.Any())
                {
                    // If both customer and carrier are provided, filter based on both

                    workcodes = workcodes.Where(x => customer.Contains(x.Customer) && carrier.Contains(x.Carrier) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);


                }
                else if (customer != null && customer.Any() && carrier == null)
                {
                    workcodes = workcodes.Where(x => customer.Contains(x.Customer) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
                else if (carrier != null && carrier.Any() && customer == null)
                {
                    workcodes = workcodes.Where(x => carrier.Contains(x.Carrier) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
                else
                {
                    // If none of the parameters are provided, return unique workcodes for the logged user
                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                 .Distinct()
                                 .OrderBy(x => x)
                                 .Select(c => new { value = c, text = c })
                                 .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
            }
            else if (viewname == "ExceptionHistory")
            {
                var workcodes = Obj.IVT_ResultHistory.Where(x => x.LoginName.Equals(loggeduser));
                // If both customer and carrier are null or empty, return unique workcodes for the logged user


                if (customer != null && customer.Any() && carrier != null && carrier.Any())
                {
                    // If both customer and carrier are provided, filter based on both

                    workcodes = workcodes.Where(x => customer.Contains(x.customer) && carrier.Contains(x.carrier) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);


                }
                else if (customer != null && customer.Any() && carrier == null)
                {
                    workcodes = workcodes.Where(x => customer.Contains(x.customer) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
                else if (carrier != null && carrier.Any() && customer == null)
                {
                    workcodes = workcodes.Where(x => carrier.Contains(x.carrier) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
                else
                {
                    // If none of the parameters are provided, return unique workcodes for the logged user
                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                 .Distinct()
                                 .OrderBy(x => x)
                                 .Select(c => new { value = c, text = c })
                                 .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }

            }
            else // by default return Current Consignment Results
            {
                var workcodes = Obj.vIVT_Result.Where(x => x.LoginName.Equals(loggeduser) && !string.IsNullOrEmpty(x.work_code));
                // If both customer and carrier are null or empty, return unique workcodes for the logged user


                if (customer != null && customer.Any() && carrier != null && carrier.Any())
                {
                    // If both customer and carrier are provided, filter based on both

                    workcodes = workcodes.Where(x => customer.Contains(x.customer) && carrier.Contains(x.carrier) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);


                }
                else if (customer != null && customer.Any() && carrier == null)
                {
                    workcodes = workcodes.Where(x => customer.Contains(x.customer) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
                else if (carrier != null && carrier.Any() && customer == null)
                {
                    workcodes = workcodes.Where(x => carrier.Contains(x.carrier) && !string.IsNullOrEmpty(x.work_code));


                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                  .Distinct()
                                  .OrderBy(x => x)
                                  .Select(c => new { value = c, text = c })
                                  .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);
                }
                else
                {
                    // If none of the parameters are provided, return unique workcodes for the logged user
                    var workcodes_Result = workcodes.Select(x => x.work_code)
                                 .Distinct()
                                 .OrderBy(x => x)
                                 .Select(c => new { value = c, text = c })
                                 .ToList();
                    return Json(workcodes_Result, JsonRequestBehavior.AllowGet);

                }


            }
        }



        private string GetLoggedUser()
        {
            string loggeduser = "";

            if (TempData["LoggedAs"] == null && Session["LoggedAs"] == null)
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
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
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last(); // "sallan";
                Session["Uname"] = loggeduser;
                Session["LoginName"] = loggeduser;
            }

         

            return loggeduser;
        }

        public ActionResult ExceptionHistory(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
          , string customerGroupFilter, string customerFilter, string carrierFilter, string workcodeFilter, string minMargin, string maxMargin, DateTime? invoiceWeek)
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
                string loggeduser = GetLoggedUser();
                if (string.IsNullOrEmpty(loggeduser))
                {
                    loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
                }

                ViewBag.DisplayForAdmin = "hidden";
                ViewBag.DisplayForSuperAdmin = "hidden";
                ViewBag.DisplayLinkStatus = "hidden";
               
                ViewBag.LoggedUser = loggeduser;
                ViewBag.LoggedUserStatus = "visible";

                var userinfo = usersEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
                ViewBag.UserName = loggeduser;

                string loginmessage = string.Empty;

                if (userinfo != null && userinfo.IsActive == 1)
                {
                    if (userinfo.UserType == 0 && userinfo.IsActive == 1 && (string)Session["LoggedAs"] == null && loggedas == null && MyGlobalVariables.loggedas != 1) // Original AM Login
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


                       



                        IVTEntities Obj = new IVTEntities();
                        //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();
                        //Obj.sp_IVT_Portal_Log(loggeduser, "User has viewed IVT Result");
                        IQueryable<vIVT_ResultHistory> cons = Obj.vIVT_ResultHistory;




                        var UniqueCustomerGroup = Obj.vIVT_ResultHistory
                            .Where(x => x.LoginName.Equals(loggeduser))
                            .Select(x => x.FinanceGroup)
                            .Distinct().OrderBy(x => x)
                            .ToList();


                        var uniqueCustomers = Obj.IVT_ResultHistory
                                  .Where(x => x.LoginName.Equals(loggeduser))
                                  .Select(x => x.customer)
                                  .Distinct().OrderBy(x => x)
                                  .ToList();

                        var uniqueCarriers = Obj.IVT_ResultHistory.Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value).Select(x => x.carrier).Distinct().OrderBy(x => x).ToList();
                        var uniqueWorkcodes = Obj.IVT_ResultHistory.Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();

                        ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                        ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                        ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                        ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);




                        // New filters

                        if (!string.IsNullOrEmpty(customerGroupFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                        }



                        if (!string.IsNullOrEmpty(search))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.con_note.Contains(search) || x.Reason.Contains(search) || x.customer.Contains(search) || x.ErrCode.Contains(search)).Take(1000);
                        }

                        // New filters
                        if (!string.IsNullOrEmpty(customerFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.customer.Equals(customerFilter));
                        }

                        if (!string.IsNullOrEmpty(carrierFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.carrier.Equals(carrierFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(workcodeFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.work_code.Equals(workcodeFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(minMargin))
                        {
                            decimal minMarginValue = string.IsNullOrEmpty(minMargin) ? 0 : Convert.ToDecimal(minMargin);

                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.GrossMargin >= minMarginValue).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(maxMargin))
                        {
                            decimal maxMarginValue = string.IsNullOrEmpty(maxMargin) ? decimal.MaxValue : Convert.ToDecimal(maxMargin);
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.GrossMargin <= maxMarginValue).Take(1000);
                        }


                        if (invoiceWeek != null)
                        {


                            cons = cons
                                .Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value)
                                .Take(1000);
                        }

                        if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                            && string.IsNullOrEmpty(minMargin) && string.IsNullOrEmpty(maxMargin) && string.IsNullOrEmpty(customerGroupFilter) && invoiceWeek == null)
                        {
                            cons = cons.Where(x => x.HeldBy.Equals(loggeduser)).Take(200);
                        }

                      

                        return View(cons.ToList().Take(200));
                    }
                    else if ((userinfo.UserType == 0 || userinfo.UserType == 2) && userinfo.IsActive == 1 && MyGlobalVariables.loggedas == 1 && (string)Session["LoggedAs"] != null) // logged as DWH editor / AM
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


                      

                        string authenticatedUser = (string)TempData["LoginName"];

                        if (authenticatedUser == null)
                        {
                            authenticatedUser = (string)Session["LoginName"];
                        }
                        IVTEntities Obj = new IVTEntities();
                        // Obj.sp_IVT_Portal_Log(WindowsIdentity.GetCurrent().Name.Split('\\').Last(), "User has viewed IVT Result On behalf of AM: " + loggedUser.ToUpper());
                        //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                        //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                        IQueryable<vIVT_ResultHistory> cons = Obj.vIVT_ResultHistory;




                        var UniqueCustomerGroup = Obj.vIVT_ResultHistory
                            .Where(x => x.LoginName.Equals(loggeduser))
                            .Select(x => x.FinanceGroup)
                            .Distinct().OrderBy(x => x)
                            .ToList();


                        var uniqueCustomers = Obj.IVT_ResultHistory
                                  .Where(x => x.LoginName.Equals(loggeduser))
                                  .Select(x => x.customer)
                                  .Distinct().OrderBy(x => x)
                                  .ToList();

                        var uniqueCarriers = Obj.IVT_ResultHistory.Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value).Select(x => x.carrier).Distinct().OrderBy(x => x).ToList();
                        var uniqueWorkcodes = Obj.IVT_ResultHistory.Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();

                        ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                        ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                        ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                        ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);




                        // New filters

                        if (!string.IsNullOrEmpty(customerGroupFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                        }



                        if (!string.IsNullOrEmpty(search))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.con_note.Contains(search) || x.Reason.Contains(search) || x.customer.Contains(search) || x.ErrCode.Contains(search)).Take(1000);
                        }

                        // New filters
                        if (!string.IsNullOrEmpty(customerFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.customer.Equals(customerFilter));
                        }

                        if (!string.IsNullOrEmpty(carrierFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.carrier.Equals(carrierFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(workcodeFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.work_code.Equals(workcodeFilter)).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(minMargin))
                        {
                            decimal minMarginValue = string.IsNullOrEmpty(minMargin) ? 0 : Convert.ToDecimal(minMargin);

                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.GrossMargin >= minMarginValue).Take(1000);
                        }

                        if (!string.IsNullOrEmpty(maxMargin))
                        {
                            decimal maxMarginValue = string.IsNullOrEmpty(maxMargin) ? decimal.MaxValue : Convert.ToDecimal(maxMargin);
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.GrossMargin <= maxMarginValue).Take(1000);
                        }


                        if (invoiceWeek != null)
                        {


                            cons = cons
                                .Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value)
                                .Take(1000);
                        }

                        if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                            && string.IsNullOrEmpty(minMargin) && string.IsNullOrEmpty(maxMargin) && string.IsNullOrEmpty(customerGroupFilter) && invoiceWeek == null)
                        {
                            cons = cons.Where(x => x.HeldBy.Equals(loggeduser)).Take(200);
                        }



                        return View(cons.ToList().Take(200));

                    }
                    else if ((userinfo.UserType == 1))
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
                    else if ((userinfo.UserType == 2))
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
                    // var checkdisable = ivtEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
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


                    var DWHEditor = (string)Session["Uname"];
                    IVTEntities Obj = new IVTEntities();
                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                    IQueryable<vIVT_ResultHistory> cons = Obj.vIVT_ResultHistory;




                    var UniqueCustomerGroup = Obj.vIVT_ResultHistory
                        .Where(x => x.LoginName.Equals(loggeduser))
                        .Select(x => x.FinanceGroup)
                        .Distinct().OrderBy(x => x)
                        .ToList();


                    var uniqueCustomers = Obj.IVT_ResultHistory
                              .Where(x => x.LoginName.Equals(loggeduser))
                              .Select(x => x.customer)
                              .Distinct().OrderBy(x => x)
                              .ToList();

                    var uniqueCarriers = Obj.IVT_ResultHistory.Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value).Select(x => x.carrier).Distinct().OrderBy(x => x).ToList();
                    var uniqueWorkcodes = Obj.IVT_ResultHistory.Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();

                    ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                    ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                    ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                    ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);




                    // New filters

                    if (!string.IsNullOrEmpty(customerGroupFilter))
                    {
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                    }



                    if (!string.IsNullOrEmpty(search))
                    {
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.con_note.Contains(search) || x.Reason.Contains(search) || x.customer.Contains(search) || x.ErrCode.Contains(search)).Take(1000);
                    }

                    // New filters
                    if (!string.IsNullOrEmpty(customerFilter))
                    {
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.customer.Equals(customerFilter));
                    }

                    if (!string.IsNullOrEmpty(carrierFilter))
                    {
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.carrier.Equals(carrierFilter)).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(workcodeFilter))
                    {
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.work_code.Equals(workcodeFilter)).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(minMargin))
                    {
                        decimal minMarginValue = string.IsNullOrEmpty(minMargin) ? 0 : Convert.ToDecimal(minMargin);

                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.GrossMargin >= minMarginValue).Take(1000);
                    }

                    if (!string.IsNullOrEmpty(maxMargin))
                    {
                        decimal maxMarginValue = string.IsNullOrEmpty(maxMargin) ? decimal.MaxValue : Convert.ToDecimal(maxMargin);
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.GrossMargin <= maxMarginValue).Take(1000);
                    }


                    if (invoiceWeek != null)
                    {


                        cons = cons
                            .Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value)
                            .Take(1000);
                    }

                    if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                        && string.IsNullOrEmpty(minMargin) && string.IsNullOrEmpty(maxMargin) && string.IsNullOrEmpty(customerGroupFilter) && invoiceWeek == null)
                    {
                        cons = cons.Where(x => x.HeldBy.Equals(loggeduser)).Take(200);
                    }




                    return View(cons.ToList().Take(200));
                }
                catch (Exception)
                {
                    return RedirectToAction("TimeOut", "Home");
                }

            }
            catch (Exception ex)
            {

                return RedirectToAction("TimeOut", "Home");
            }

        }





        //public ActionResult AncillarySummary(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
        //     ,string customerGroupFilter, string customerFilter, string carrierFilter, string workcodeFilter, string minMargin, string maxMargin)
        //{
        //    try
        //    {
        //        ViewBag.DisplayForAdmin = "hidden";
        //        ViewBag.DisplayForSuperAdmin = "hidden";
        //        ViewBag.DisplayLinkStatus = "hidden";
        //          string loggeduser = GetLoggedUser();
        //        if (string.IsNullOrEmpty(loggeduser))
        //        {
        //            loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
        //        }
            
        //        ViewBag.LoggedUser = loggeduser;
        //        ViewBag.LoggedUserStatus = "visible";

        //        //Selected values for default DL list 
        //        ViewBag.selectedCustomerGroup = customerGroupFilter;
        //        ViewBag.selectedCustomer = customerFilter;
        //        ViewBag.selectedCarrier = carrierFilter;
        //        ViewBag.selectedWorkCode = workcodeFilter;

               

              


        //        var userinfo = usersEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
        //        ViewBag.UserName = loggeduser;

        //        string loginmessage = string.Empty;

        //        if (userinfo != null && userinfo.IsActive == 1)
        //        {
        //            ViewBag.DisplayLinkStatus = "visible";
              
        //             if (userinfo.UserType == 0  && userinfo.IsActive == 1 && (string)Session["LoggedAs"] == null && loggedas == null && MyGlobalVariables.loggedas != 1) // Original AM Login
        //            {
        //                ViewBag.DisplayLinkStatus = "visible";
        //                ViewBag.DisplayForAdmin = "hidden";
        //                ViewBag.DisplayForSuperAdmin = "hidden";
        //                FormsAuthentication.SetAuthCookie(loggeduser, true);
        //                TempData["UserName"] = userinfo.DWHEditor;
        //                Session["Uname"] = userinfo.DWHEditor;
        //                Session["Utype"] = userinfo.UserType;
        //                TempData["LoginName"] = loggeduser.Trim().ToUpper();
        //                Session["LoginName"] = loggeduser.Trim().ToUpper();


        //                if (message == null)
        //                {
        //                    ViewBag.ViewMessage = "hidden";
        //                    ViewBag.LastUpdateViewMessage = "visible";
        //                }
        //                else if (updatestatus >= 1)
        //                {
        //                    TempData["message"] = message;
        //                    ViewBag.Message = message;
        //                    ViewBag.messageStatus = updatestatus;
        //                    ViewBag.LastUpdateViewMessage = "hidden";
        //                }
        //                else
        //                {
        //                    TempData["message"] = null;
        //                    ViewBag.messageStatus = 0;
        //                    ViewBag.Message = null;
        //                    ViewBag.LastUpdateViewMessage = "visible";
        //                }



        //                IVTEntities Obj = new IVTEntities();
        //                //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();
                       
        //                IQueryable<vAllCurrentAncillarySummary> cons = Obj.vAllCurrentAncillarySummary;

        //                var uniqueCustomers = Obj.vAllCurrentAncillarySummary
        //                      .Where(x => x.LoginName.Equals(loggeduser))
        //                      .Select(x => x.CustomerCode)
        //                      .Distinct().OrderBy(x => x)
        //                      .ToList();

        //                var uniqueCarriers = Obj.vAllCurrentAncillarySummary.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.Carrier).Distinct().OrderBy(x => x).ToList();
        //                var uniqueWorkcodes = Obj.vAllCurrentAncillarySummary.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.WorkCode).Distinct().OrderBy(x => x).ToList();

            

        //                var UniqueCustomerGroup = Obj.vAllCurrentAncillarySummary
        //                .Where(x => x.LoginName.Equals(loggeduser))
        //                .Select(x => x.FinanceGroup)
        //                .Distinct().OrderBy(x => x)
        //                .ToList();



               
        //                ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
        //                ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
        //                ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
        //                ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);

                    

        //                // New filters

        //                if (!string.IsNullOrEmpty(customerGroupFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
        //                }


        //                if (!string.IsNullOrEmpty(search))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Carrier.Contains(search) || x.CustomerCode.Contains(search) || x.WorkCode.Contains(search));
        //                }

        //                // New filters
        //                if (!string.IsNullOrEmpty(customerFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.CustomerCode.Contains(customerFilter));
        //                }

        //                if (!string.IsNullOrEmpty(carrierFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Carrier.Equals(carrierFilter));
        //                }

        //                if (!string.IsNullOrEmpty(workcodeFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.WorkCode.Contains(workcodeFilter));
        //                }

                     

        //                if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
        //                   )
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser)).OrderByDescending(x => x.CustomerCode);
        //                }

        //                cons = cons.OrderByDescending(x => x.CustomerCode);
        //                return View(cons.ToList());
        //            }
        //            else if ((userinfo.UserType == 0 || userinfo.UserType == 2) && userinfo.IsActive == 1 && MyGlobalVariables.loggedas == 1 ) // logged as DWH editor / AM
        //            {
        //                ViewBag.DisplayLinkStatus = "visible";
        //                ViewBag.DisplayForAdmin = "hidden";
        //                ViewBag.DisplayForSuperAdmin = "visible";
        //                FormsAuthentication.SetAuthCookie(loggeduser, true);
        //                TempData["UserName"] = userinfo.DWHEditor;
        //                Session["Uname"] = userinfo.DWHEditor;
        //                Session["Utype"] = userinfo.UserType;
        //                TempData["LoginName"] = loggeduser.Trim().ToUpper();
        //                Session["LoginName"] = loggeduser.Trim().ToUpper();


        //                if (message == null)
        //                {
        //                    ViewBag.ViewMessage = "hidden";
        //                    ViewBag.LastUpdateViewMessage = "visible";
        //                }
        //                else if (updatestatus >= 1)
        //                {
        //                    TempData["message"] = message;
        //                    ViewBag.Message = message;
        //                    ViewBag.messageStatus = updatestatus;
        //                    ViewBag.LastUpdateViewMessage = "hidden";
        //                }
        //                else
        //                {
        //                    TempData["message"] = null;
        //                    ViewBag.messageStatus = 0;
        //                    ViewBag.Message = null;
        //                    ViewBag.LastUpdateViewMessage = "visible";
        //                }

        //                string authenticatedUser = (string)TempData["LoginName"];

        //                if (authenticatedUser == null)
        //                {
        //                    authenticatedUser = (string)Session["LoginName"];
        //                }
        //                IVTEntities Obj = new IVTEntities();

        //                //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

        //                //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

        //                IQueryable<vAllCurrentAncillarySummary> cons = Obj.vAllCurrentAncillarySummary;

        //                var uniqueCustomers = Obj.vAllCurrentAncillarySummary
        //                      .Where(x => x.LoginName.Equals(loggeduser))
        //                      .Select(x => x.CustomerCode)
        //                      .Distinct().OrderBy(x => x)
        //                      .ToList();

        //                var uniqueCarriers = Obj.vAllCurrentAncillarySummary.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.Carrier).Distinct().OrderBy(x => x).ToList();
        //                var uniqueWorkcodes = Obj.vAllCurrentAncillarySummary.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.WorkCode).Distinct().OrderBy(x => x).ToList();



        //                var UniqueCustomerGroup = Obj.vAllCurrentAncillarySummary
        //                .Where(x => x.LoginName.Equals(loggeduser))
        //                .Select(x => x.FinanceGroup)
        //                .Distinct().OrderBy(x => x)
        //                .ToList();




        //                ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
        //                ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
        //                ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
        //                ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);



        //                // New filters

        //                if (!string.IsNullOrEmpty(customerGroupFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
        //                }



        //                if (!string.IsNullOrEmpty(search))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Carrier.Contains(search) || x.CustomerCode.Contains(search) || x.WorkCode.Contains(search));
        //                }

        //                // New filters
        //                if (!string.IsNullOrEmpty(customerFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.CustomerCode.Contains(customerFilter));
        //                }

        //                if (!string.IsNullOrEmpty(carrierFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Carrier.Equals(carrierFilter));
        //                }

        //                if (!string.IsNullOrEmpty(workcodeFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.WorkCode.Contains(workcodeFilter));
        //                }



        //                if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
        //                   )
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser)).OrderByDescending(x => x.CustomerCode);
        //                }

        //                cons = cons.OrderByDescending(x => x.CustomerCode);
        //                return View(cons.ToList());


        //            }
        //            else if ((userinfo.UserType == 1) )
        //            {
        //                ViewBag.DisplayLinkStatus = "visible";
        //                ViewBag.DisplayForAdmin = "visible";
        //                ViewBag.DisplayForSuperAdmin = "hidden";
        //                FormsAuthentication.SetAuthCookie(loggeduser, true);
        //                TempData["UserName"] = userinfo.DWHEditor;
        //                Session["Uname"] = userinfo.DWHEditor;
        //                Session["Utype"] = userinfo.UserType;
        //                TempData["Utype"] = userinfo.UserType;
        //                TempData["LoginName"] = loggeduser.Trim().ToUpper();
        //                Session["LoginName"] = loggeduser.Trim().ToUpper();

        //                return RedirectToAction("UserList", "Home");

        //            }
        //            else if ((userinfo.UserType == 2) )
        //            {
        //                ViewBag.DisplayLinkStatus = "visible";
        //                ViewBag.DisplayForAdmin = "visible";
        //                ViewBag.DisplayForSuperAdmin = "visible";
        //                FormsAuthentication.SetAuthCookie(loggeduser, true);
        //                TempData["UserName"] = userinfo.DWHEditor;
        //                Session["Uname"] = userinfo.DWHEditor;
        //                Session["Utype"] = userinfo.UserType;
        //                TempData["Utype"] = userinfo.UserType;
        //                TempData["LoginName"] = loggeduser.Trim().ToUpper();
        //                Session["LoginName"] = loggeduser.Trim().ToUpper();

        //                return RedirectToAction("UserList", "Home");

        //            }

        //        }
        //        else
        //        {
        //            // var checkdisable = ivtEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
        //            if (userinfo != null && userinfo.IsActive == 0)
        //            {
        //                ViewBag.DisplayLinkStatus = "hidden";
        //                ViewBag.DisplayForAdmin = "hidden";
        //                ViewBag.DisplayForSuperAdmin = "hidden";
        //                TempData["Utype"] = 0;
        //                message = "Your User Has Disabled!, Please Contact Business Admin";
        //                return RedirectToAction("NotAuthorized", "Home");

        //            }
        //            else
        //            {
        //                ViewBag.DisplayLinkStatus = "hidden";
        //                ViewBag.DisplayForAdmin = "hidden";
        //                ViewBag.DisplayForSuperAdmin = "hidden";
        //                TempData["Utype"] = 0;
        //                message = "You  have no rights , please contact support.";
        //                return RedirectToAction("NotAuthorized", "Home");

        //            }

                

        //        }
        //        try
        //        {


                    

        //            var DWHEditor = (string)Session["Uname"];
        //            IVTEntities Obj = new IVTEntities();
        //            //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

        //            //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

        //            IQueryable<vAllCurrentAncillarySummary> cons = Obj.vAllCurrentAncillarySummary;

        //            var uniqueCustomers = Obj.vAllCurrentAncillarySummary
        //                  .Where(x => x.LoginName.Equals(loggeduser))
        //                  .Select(x => x.CustomerCode)
        //                  .Distinct().OrderBy(x => x)
        //                  .ToList();

        //            var uniqueCarriers = Obj.vAllCurrentAncillarySummary.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.Carrier).Distinct().OrderBy(x => x).ToList();
        //            var uniqueWorkcodes = Obj.vAllCurrentAncillarySummary.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.WorkCode).Distinct().OrderBy(x => x).ToList();



        //            var UniqueCustomerGroup = Obj.vAllCurrentAncillarySummary
        //            .Where(x => x.LoginName.Equals(loggeduser))
        //            .Select(x => x.FinanceGroup)
        //            .Distinct().OrderBy(x => x)
        //            .ToList();




        //            ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
        //            ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
        //            ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
        //            ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);



        //            // New filters

        //            if (!string.IsNullOrEmpty(customerGroupFilter))
        //            {
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
        //            }



        //            if (!string.IsNullOrEmpty(search))
        //            {
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Carrier.Contains(search) || x.CustomerCode.Contains(search) || x.WorkCode.Contains(search));
        //            }

        //            // New filters
        //            if (!string.IsNullOrEmpty(customerFilter))
        //            {
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser) &&  x.CustomerCode.Contains(customerFilter));
        //            }

        //            if (!string.IsNullOrEmpty(carrierFilter))
        //            {
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Carrier.Equals(carrierFilter));
        //            }

        //            if (!string.IsNullOrEmpty(workcodeFilter))
        //            {
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.WorkCode.Contains(workcodeFilter));
        //            }



        //            if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
        //               )
        //            {
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser)).OrderByDescending(x => x.CustomerCode);
        //            }

        //            cons = cons.OrderByDescending(x => x.CustomerCode);
        //            return View(cons.ToList());
        //        }
        //        catch (Exception)
        //        {
        //            return RedirectToAction("TimeOut", "Home");
        //        }

        //    }
        //    catch (Exception)
        //    {

        //        return RedirectToAction("TimeOut", "Home");
        //    }

        //}




        //public ActionResult TradingData(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate
        //    ,string customerGroupFilter,  string customerFilter, string carrierFilter, string workcodeFilter, string SupplierRef, string SellNett)
        //{
        //    ViewBag.Search = search;
        //    ViewBag.CustomerGroupFilter = customerGroupFilter;
        //    ViewBag.CustomerFilter = customerFilter;
        //    ViewBag.CarrierFilter = carrierFilter;
        //    ViewBag.WorkcodeFilter = workcodeFilter;
        //    ViewBag.SupplierRef = SupplierRef;
        //    ViewBag.SellNett = SellNett;

        //    //Selected values for default DL list 
        //    ViewBag.selectedCustomerGroup = customerGroupFilter;
        //    ViewBag.selectedCustomer = customerFilter;
        //    ViewBag.selectedCarrier = carrierFilter;
        //    ViewBag.selectedWorkCode = workcodeFilter;

        //    try
        //    {
        //        string loggeduser = GetLoggedUser();
        //        if (string.IsNullOrEmpty(loggeduser))
        //        {
        //            loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
        //        }

        //        ViewBag.DisplayForAdmin = "hidden";
        //        ViewBag.DisplayForSuperAdmin = "hidden";
        //        ViewBag.DisplayLinkStatus = "hidden";
               
        //        ViewBag.LoggedUser = loggeduser;
        //        ViewBag.LoggedUserStatus = "visible";




        //        var userinfo = usersEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
        //        ViewBag.UserName = loggeduser;

        //        string loginmessage = string.Empty;

        //        if (userinfo != null && userinfo.IsActive == 1)
        //        {
        //            ViewBag.DisplayLinkStatus = "visible";
              
        //             if (userinfo.UserType == 0  && userinfo.IsActive == 1 && (string)Session["LoggedAs"] == null && loggedas == null && MyGlobalVariables.loggedas != 1) // Original AM Login
        //            {
        //                ViewBag.DisplayLinkStatus = "visible";
        //                ViewBag.DisplayForAdmin = "hidden";
        //                ViewBag.DisplayForSuperAdmin = "hidden";
        //                FormsAuthentication.SetAuthCookie(loggeduser, true);
        //                TempData["UserName"] = userinfo.DWHEditor;
        //                Session["Uname"] = userinfo.DWHEditor;
        //                Session["Utype"] = userinfo.UserType;
        //                TempData["LoginName"] = loggeduser.Trim().ToUpper();
        //                Session["LoginName"] = loggeduser.Trim().ToUpper();


        //                if (message == null)
        //                {
        //                    ViewBag.ViewMessage = "hidden";
        //                    ViewBag.LastUpdateViewMessage = "visible";
        //                }
        //                else if (updatestatus >= 1)
        //                {
        //                    TempData["message"] = message;
        //                    ViewBag.Message = message;
        //                    ViewBag.messageStatus = updatestatus;
        //                    ViewBag.LastUpdateViewMessage = "hidden";
        //                }
        //                else
        //                {
        //                    TempData["message"] = null;
        //                    ViewBag.messageStatus = 0;
        //                    ViewBag.Message = null;
        //                    ViewBag.LastUpdateViewMessage = "visible";
        //                }



        //                IVTEntities Obj = new IVTEntities();
        //                //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

        //                IQueryable<vAllCurrentDetails> cons = Obj.vAllCurrentDetails;

        //                var UniqueCustomerGroup = Obj.vAllCurrentDetails
        //               .Where(x => x.LoginName.Equals(loggeduser))
        //               .Select(x => x.FinanceGroup)
        //               .Distinct().OrderBy(x => x)
        //               .ToList();


        //                var uniqueCustomers = Obj.vAllCurrentDetails
        //                  .Where(x => x.LoginName.Equals(loggeduser))
        //                  .Select(x => x.Customer)
        //                  .Distinct().OrderBy(x => x)
        //                  .ToList();

        //                var uniqueCarriers = Obj.vAllCurrentDetails.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.Carrier).Distinct().OrderBy(x => x).ToList();
        //                var uniqueWorkcodes = Obj.vAllCurrentDetails.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();


        //                ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
        //                ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
        //                ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
        //                ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);



                       
          

        //                // New filters

        //                if (!string.IsNullOrEmpty(customerGroupFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.FinanceGroup.Equals(customerGroupFilter));
        //                }



        //                if (!string.IsNullOrEmpty(search))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.ConsignmentNote.Contains(search) || x.Carrier.Contains(search) || x.Customer.Contains(search) || x.work_code.Contains(search));
        //                }

        //                // New filters
        //                if (!string.IsNullOrEmpty(customerFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Customer.Equals(customerFilter));
        //                }

        //                if (!string.IsNullOrEmpty(carrierFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Carrier.Equals(carrierFilter));
        //                }

        //                if (!string.IsNullOrEmpty(workcodeFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.work_code.Equals(workcodeFilter));
        //                }

        //                if (!string.IsNullOrEmpty(SupplierRef))
        //                {
                          
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) &&  x.SupplierRef.Contains(SupplierRef));
        //                }

        //                if (!string.IsNullOrEmpty(SellNett))
        //                {
        //                    decimal SellNettValue = string.IsNullOrEmpty(SellNett) ? decimal.MaxValue : Convert.ToDecimal(SellNett);
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Sell_Total_Nett >= SellNettValue);
        //                }

        //                if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
        //                    && string.IsNullOrEmpty(SupplierRef) && string.IsNullOrEmpty(SellNett) && string.IsNullOrEmpty(customerGroupFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser)).Take(200);
        //                }

        //                cons = cons.OrderByDescending(x => x.Sell_Total_Nett);

        //                // At this point the query has generated a SQL statement based on the conditions above,
        //                // but it will not be executed until the until the next line - i.e. when calling .ToList()
        //                return View(cons.ToList());
        //            }
        //            else if ((userinfo.UserType == 0 || userinfo.UserType == 2) && userinfo.IsActive == 1 && MyGlobalVariables.loggedas == 1 ) // logged as DWH editor / AM
        //            {
        //                ViewBag.DisplayLinkStatus = "visible";
        //                ViewBag.DisplayForAdmin = "hidden";
        //                ViewBag.DisplayForSuperAdmin = "visible";
        //                FormsAuthentication.SetAuthCookie(loggeduser, true);
        //                TempData["UserName"] = userinfo.DWHEditor;
        //                Session["Uname"] = userinfo.DWHEditor;
        //                Session["Utype"] = userinfo.UserType;
        //                TempData["LoginName"] = loggeduser.Trim().ToUpper();
        //                Session["LoginName"] = loggeduser.Trim().ToUpper();


        //                if (message == null)
        //                {
        //                    ViewBag.ViewMessage = "hidden";
        //                    ViewBag.LastUpdateViewMessage = "visible";
        //                }
        //                else if (updatestatus >= 1)
        //                {
        //                    TempData["message"] = message;
        //                    ViewBag.Message = message;
        //                    ViewBag.messageStatus = updatestatus;
        //                    ViewBag.LastUpdateViewMessage = "hidden";
        //                }
        //                else
        //                {
        //                    TempData["message"] = null;
        //                    ViewBag.messageStatus = 0;
        //                    ViewBag.Message = null;
        //                    ViewBag.LastUpdateViewMessage = "visible";
        //                }

                   
        //                IVTEntities Obj = new IVTEntities();

        //                //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

        //                //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

        //                IQueryable<vAllCurrentDetails> cons = Obj.vAllCurrentDetails;

        //                var UniqueCustomerGroup = Obj.vAllCurrentDetails
        //              .Where(x => x.LoginName.Equals(loggeduser))
        //              .Select(x => x.FinanceGroup)
        //              .Distinct().OrderBy(x => x)
        //              .ToList();


        //                var uniqueCustomers = Obj.vAllCurrentDetails
        //                  .Where(x => x.LoginName.Equals(loggeduser))
        //                  .Select(x => x.Customer)
        //                  .Distinct().OrderBy(x => x)
        //                  .ToList();

        //                var uniqueCarriers = Obj.vAllCurrentDetails.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.Carrier).Distinct().OrderBy(x => x).ToList();
        //                var uniqueWorkcodes = Obj.vAllCurrentDetails.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();


        //                ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
        //                ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
        //                ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
        //                ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);


        //                // New filters

        //                if (!string.IsNullOrEmpty(customerGroupFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.FinanceGroup.Equals(customerGroupFilter));
        //                }


        //                if (!string.IsNullOrEmpty(search))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.ConsignmentNote.Contains(search) || x.Carrier.Contains(search) || x.Customer.Contains(search) || x.work_code.Contains(search));
        //                }

        //                // New filters
        //                if (!string.IsNullOrEmpty(customerFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Customer.Equals(customerFilter));
        //                }

        //                if (!string.IsNullOrEmpty(carrierFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) &&  x.Carrier.Equals(carrierFilter));
        //                }

        //                if (!string.IsNullOrEmpty(workcodeFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.work_code.Equals(workcodeFilter));
        //                }

        //                if (!string.IsNullOrEmpty(SupplierRef))
        //                {

        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.SupplierRef.Contains(SupplierRef));
        //                }

        //                if (!string.IsNullOrEmpty(SellNett))
        //                {
        //                    decimal SellNettValue = string.IsNullOrEmpty(SellNett) ? decimal.MaxValue : Convert.ToDecimal(SellNett);
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Sell_Total_Nett >= SellNettValue);
        //                }

        //                if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
        //                    && string.IsNullOrEmpty(SupplierRef) && string.IsNullOrEmpty(SellNett) && string.IsNullOrEmpty(customerGroupFilter))
        //                {
        //                    cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.LoginName.Equals(loggeduser)).Take(200);
        //                }

        //                cons = cons.OrderByDescending(x => x.Sell_Total_Nett);
        //                // At this point the query has generated a SQL statement based on the conditions above,
        //                // but it will not be executed until the until the next line - i.e. when calling .ToList()
        //                return View(cons.ToList());
        //                // At this point the query has generated a SQL statement based on the conditions above,
        //                // but it will not be executed until the until the next line - i.e. when calling .ToList()
                      
        //            }
        //            else if ((userinfo.UserType == 1) )
        //            {
        //                ViewBag.DisplayLinkStatus = "visible";
        //                ViewBag.DisplayForAdmin = "visible";
        //                ViewBag.DisplayForSuperAdmin = "hidden";
        //                FormsAuthentication.SetAuthCookie(loggeduser, true);
        //                TempData["UserName"] = userinfo.DWHEditor;
        //                Session["Uname"] = userinfo.DWHEditor;
        //                Session["Utype"] = userinfo.UserType;
        //                TempData["Utype"] = userinfo.UserType;
        //                TempData["LoginName"] = loggeduser.Trim().ToUpper();
        //                Session["LoginName"] = loggeduser.Trim().ToUpper();

        //                return RedirectToAction("Dashboard", "Home");

        //            }
        //            else if ((userinfo.UserType == 2) )
        //            {
        //                ViewBag.DisplayLinkStatus = "visible";
        //                ViewBag.DisplayForAdmin = "visible";
        //                ViewBag.DisplayForSuperAdmin = "visible";
        //                FormsAuthentication.SetAuthCookie(loggeduser, true);
        //                TempData["UserName"] = userinfo.DWHEditor;
        //                Session["Uname"] = userinfo.DWHEditor;
        //                Session["Utype"] = userinfo.UserType;
        //                TempData["Utype"] = userinfo.UserType;
        //                TempData["LoginName"] = loggeduser.Trim().ToUpper();
        //                Session["LoginName"] = loggeduser.Trim().ToUpper();

        //                return RedirectToAction("UserList", "Home");

        //            }

        //        }
        //        else
        //        {
        //            // var checkdisable = ivtEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
        //            if (userinfo != null && userinfo.IsActive == 0)
        //            {
        //                ViewBag.DisplayLinkStatus = "hidden";
        //                ViewBag.DisplayForAdmin = "hidden";
        //                ViewBag.DisplayForSuperAdmin = "hidden";
        //                TempData["Utype"] = 0;
        //                message = "Your User Has Disabled!, Please Contact Business Admin";
        //                return RedirectToAction("NotAuthorized", "Home");

        //            }
        //            else
        //            {
        //                ViewBag.DisplayLinkStatus = "hidden";
        //                ViewBag.DisplayForAdmin = "hidden";
        //                ViewBag.DisplayForSuperAdmin = "hidden";
        //                TempData["Utype"] = 0;
        //                message = "You  have no rights , please contact support.";
        //                return RedirectToAction("NotAuthorized", "Home");

        //            }

        //        }
        //        try
        //        {
 
        //            var DWHEditor = (string)Session["Uname"];
        //            IVTEntities Obj = new IVTEntities();
        //            //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

        //            //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

        //            IQueryable<vAllCurrentDetails> cons = Obj.vAllCurrentDetails;

        //            var UniqueCustomerGroup = Obj.vAllCurrentDetails
        //               .Where(x => x.LoginName.Equals(loggeduser))
        //               .Select(x => x.FinanceGroup)
        //               .Distinct().OrderBy(x => x)
        //               .ToList();


        //            var uniqueCustomers = Obj.vAllCurrentDetails
        //              .Where(x => x.LoginName.Equals(loggeduser))
        //              .Select(x => x.Customer)
        //              .Distinct().OrderBy(x => x)
        //              .ToList();

        //            var uniqueCarriers = Obj.vAllCurrentDetails.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.Carrier).Distinct().OrderBy(x => x).ToList();
        //            var uniqueWorkcodes = Obj.vAllCurrentDetails.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();


        //            ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
        //            ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
        //            ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
        //            ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);



        //            // New filters

        //            if (!string.IsNullOrEmpty(customerGroupFilter))
        //            {
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.FinanceGroup.Equals(customerGroupFilter));
        //            }


        //            if (!string.IsNullOrEmpty(search))
        //            {
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.ConsignmentNote.Contains(search) || x.Carrier.Contains(search) || x.Customer.Contains(search) || x.work_code.Contains(search));
        //            }

        //            // New filters
        //            if (!string.IsNullOrEmpty(customerFilter))
        //            {
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Customer.Equals(customerFilter));
        //            }

        //            if (!string.IsNullOrEmpty(carrierFilter))
        //            {
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Carrier.Equals(carrierFilter));
        //            }

        //            if (!string.IsNullOrEmpty(workcodeFilter))
        //            {
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.work_code.Equals(workcodeFilter));
        //            }

        //            if (!string.IsNullOrEmpty(SupplierRef))
        //            {

        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.SupplierRef.Contains(SupplierRef));
        //            }

        //            if (!string.IsNullOrEmpty(SellNett))
        //            {
        //                decimal SellNettValue = string.IsNullOrEmpty(SellNett) ? decimal.MaxValue : Convert.ToDecimal(SellNett);
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Sell_Total_Nett >= SellNettValue);
        //            }

        //            if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
        //                && string.IsNullOrEmpty(SupplierRef) && string.IsNullOrEmpty(SellNett) && string.IsNullOrEmpty(customerGroupFilter))
        //            {
        //                cons = cons.Where(x => x.LoginName.Equals(loggeduser)).Take(200);
        //            }

        //            cons = cons.OrderByDescending(x => x.Sell_Total_Nett);

        //            //cons = cons.;
        //            // At this point the query has generated a SQL statement based on the conditions above,
        //            // but it will not be executed until the until the next line - i.e. when calling .ToList()
        //            return View(cons.ToList());
        //            // At this point the query has generated a SQL statement based on the conditions above,
        //            // but it will not be executed until the until the next line - i.e. when calling .ToList()


                 
        //        }
        //        catch (Exception ex)
        //        {
        //            return RedirectToAction("TimeOut", "Home");
        //        }

        //    }
        //    catch (Exception)
        //    {

        //        return RedirectToAction("TimeOut", "Home");
        //    }

        //}



        public ActionResult TradingDataHistory(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate,
      string customerGroupFilter,string customerFilter, string carrierFilter, string workcodeFilter, string SupplierRef, string SellNett,DateTime? invoiceWeek)
        {
            ViewBag.Search = search;
            ViewBag.CustomerFilter = customerFilter;
            ViewBag.CarrierFilter = carrierFilter;
            ViewBag.WorkcodeFilter = workcodeFilter;
            ViewBag.SupplierRef = SupplierRef;
            ViewBag.SellNett = SellNett;

            //Selected values for default DL list 
            ViewBag.selectedCustomerGroup = customerGroupFilter;
            ViewBag.selectedCustomer = customerFilter;
            ViewBag.selectedCarrier = carrierFilter;
            ViewBag.selectedWorkCode = workcodeFilter;
            ViewBag.InvoiceWeek = invoiceWeek;

        
            try
            {
                string loggeduser = GetLoggedUser();
                if (string.IsNullOrEmpty(loggeduser))
                {
                    loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
                }

                ViewBag.DisplayForAdmin = "hidden";
                ViewBag.DisplayForSuperAdmin = "hidden";
                ViewBag.DisplayLinkStatus = "hidden";
                string authinticated_user = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
                ViewBag.LoggedUser = authinticated_user;
                ViewBag.LoggedUserStatus = "visible";

                


                var userinfo = usersEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper()).FirstOrDefault();
                ViewBag.UserName = loggeduser;

                string loginmessage = string.Empty;

                if (userinfo != null && userinfo.IsActive == 1)
                {
                    ViewBag.DisplayLinkStatus = "visible";

                    if (userinfo.UserType == 0 && userinfo.IsActive == 1 && (string)Session["LoggedAs"] == null && loggedas == null && MyGlobalVariables.loggedas != 1) // Original AM Login
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



                        IVTEntities Obj = new IVTEntities();
                        //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                        IQueryable<vIVT_TradingDataHistory> cons = Obj.vIVT_TradingDataHistory;


                        var UniqueCustomerGroup = Obj.vIVT_TradingDataHistory
                       .Where(x => x.LoginName.Equals(loggeduser))
                       .Select(x => x.FinanceGroup)
                       .Distinct().OrderBy(x => x)
                       .ToList();


                        var uniqueCustomers = Obj.vIVT_TradingDataHistory
                           .Where(x => x.LoginName.Equals(loggeduser))
                           .Select(x => x.Customer)
                           .Distinct().OrderBy(x => x)
                           .ToList();

                        var uniqueCarriers = Obj.vIVT_TradingDataHistory.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.Carrier).Distinct().OrderBy(x => x).ToList();
                        var uniqueWorkcodes = Obj.vIVT_TradingDataHistory.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();

                        ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                        ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                        ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                        ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);


                        if (invoiceWeek != null)
                        {


                            cons = cons
                                .Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value)
                                ;
                        }

                        if (!string.IsNullOrEmpty(search))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.ConsignmentNote.Contains(search) || x.Carrier.Contains(search) || x.Customer.Contains(search) || x.work_code.Contains(search));
                        }

                        // New filters

                        if (!string.IsNullOrEmpty(customerGroupFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                        }

                        // New filters
                        if (!string.IsNullOrEmpty(customerFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Customer.Equals(customerFilter));
                        }

                        if (!string.IsNullOrEmpty(carrierFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Carrier.Equals(carrierFilter));
                        }

                        if (!string.IsNullOrEmpty(workcodeFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.work_code.Equals(workcodeFilter));
                        }

                        if (!string.IsNullOrEmpty(SupplierRef))
                        {

                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.SupplierRef.Contains(SupplierRef));
                        }

                        if (!string.IsNullOrEmpty(SellNett))
                        {
                            decimal SellNettValue = string.IsNullOrEmpty(SellNett) ? decimal.MaxValue : Convert.ToDecimal(SellNett);
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Sell_Total_Nett >= SellNettValue);
                        }


                     

                        if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                            && string.IsNullOrEmpty(SupplierRef) && string.IsNullOrEmpty(customerGroupFilter) && string.IsNullOrEmpty(SellNett) && invoiceWeek == null)
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser)).Take(1000);
                        }

                        cons = cons.Take(1000);
                        // At this point the query has generated a SQL statement based on the conditions above,
                        // but it will not be executed until the until the next line - i.e. when calling .ToList()
                        return View(cons.ToList().Take(200));
                    }
                    else if ((userinfo.UserType == 0 || userinfo.UserType == 2) && userinfo.IsActive == 1 && MyGlobalVariables.loggedas == 1) // logged as DWH editor / AM
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

                        string authenticatedUser = (string)TempData["LoginName"];

                        if (authenticatedUser == null)
                        {
                            authenticatedUser = (string)Session["LoginName"];
                        }
                        IVTEntities Obj = new IVTEntities();

                        //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                        //var delg = Obj.vDWHEditorDelegate.Where(k => k.Delegate_AM == DWHEditor).Select(a => a.Original_AM).ToList();

                        IQueryable<vIVT_TradingDataHistory> cons = Obj.vIVT_TradingDataHistory;


                        var UniqueCustomerGroup = Obj.vIVT_TradingDataHistory
                       .Where(x => x.LoginName.Equals(loggeduser))
                       .Select(x => x.FinanceGroup)
                       .Distinct().OrderBy(x => x)
                       .ToList();


                        var uniqueCustomers = Obj.vIVT_TradingDataHistory
                           .Where(x => x.LoginName.Equals(loggeduser))
                           .Select(x => x.Customer)
                           .Distinct().OrderBy(x => x)
                           .ToList();

                        var uniqueCarriers = Obj.vIVT_TradingDataHistory.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.Carrier).Distinct().OrderBy(x => x).ToList();
                        var uniqueWorkcodes = Obj.vIVT_TradingDataHistory.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();

                        ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                        ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                        ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                        ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);


                        if (invoiceWeek != null)
                        {


                            cons = cons
                                .Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value)
                                ;
                        }

                        if (!string.IsNullOrEmpty(search))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.ConsignmentNote.Contains(search) || x.Carrier.Contains(search) || x.Customer.Contains(search) || x.work_code.Contains(search));
                        }

                        // New filters

                        if (!string.IsNullOrEmpty(customerGroupFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                        }

                        // New filters
                        if (!string.IsNullOrEmpty(customerFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Customer.Equals(customerFilter));
                        }

                        if (!string.IsNullOrEmpty(carrierFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Carrier.Equals(carrierFilter));
                        }

                        if (!string.IsNullOrEmpty(workcodeFilter))
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.work_code.Equals(workcodeFilter));
                        }

                        if (!string.IsNullOrEmpty(SupplierRef))
                        {

                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.SupplierRef.Contains(SupplierRef));
                        }

                        if (!string.IsNullOrEmpty(SellNett))
                        {
                            decimal SellNettValue = string.IsNullOrEmpty(SellNett) ? decimal.MaxValue : Convert.ToDecimal(SellNett);
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Sell_Total_Nett >= SellNettValue);
                        }




                        if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                            && string.IsNullOrEmpty(SupplierRef) && string.IsNullOrEmpty(customerGroupFilter) && string.IsNullOrEmpty(SellNett) && invoiceWeek == null)
                        {
                            cons = cons.Where(x => x.LoginName.Equals(loggeduser)).Take(1000);
                        }


                       
                        return View(cons.ToList().Take(200));
                       

                    }
                    else if ((userinfo.UserType == 1))
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
                    else if ((userinfo.UserType == 2))
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
                    // var checkdisable = ivtEntities.vDWHEditorLogin.Where(a => a.LoginName.ToUpper() == loggeduser.Trim().ToUpper() && a.Password == user.Password.Trim()).FirstOrDefault();
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
                





                    var DWHEditor = (string)Session["Uname"];
                    IVTEntities Obj = new IVTEntities();


                    IQueryable<vIVT_TradingDataHistory> cons = Obj.vIVT_TradingDataHistory;


                    var UniqueCustomerGroup = Obj.vIVT_TradingDataHistory
                   .Where(x => x.LoginName.Equals(loggeduser))
                   .Select(x => x.FinanceGroup)
                   .Distinct().OrderBy(x => x)
                   .ToList();


                    var uniqueCustomers = Obj.vIVT_TradingDataHistory
                       .Where(x => x.LoginName.Equals(loggeduser))
                       .Select(x => x.Customer)
                       .Distinct().OrderBy(x => x)
                       .ToList();

                    var uniqueCarriers = Obj.vIVT_TradingDataHistory.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.Carrier).Distinct().OrderBy(x => x).ToList();
                    var uniqueWorkcodes = Obj.vIVT_TradingDataHistory.Where(x => x.LoginName.Equals(loggeduser)).Select(x => x.work_code).Distinct().OrderBy(x => x).ToList();

                    ViewBag.UniqueCustomerGroup = new SelectList(UniqueCustomerGroup);
                    ViewBag.UniqueCustomers = new SelectList(uniqueCustomers);
                    ViewBag.UniqueCarriers = new SelectList(uniqueCarriers);
                    ViewBag.UniqueWorkcodes = new SelectList(uniqueWorkcodes);


                    if (invoiceWeek != null)
                    {


                        cons = cons
                            .Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value)
                            ;
                    }

                    if (!string.IsNullOrEmpty(search))
                    {
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.ConsignmentNote.Contains(search) || x.Carrier.Contains(search) || x.Customer.Contains(search) || x.work_code.Contains(search));
                    }

                    // New filters

                    if (!string.IsNullOrEmpty(customerGroupFilter))
                    {
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.FinanceGroup.Equals(customerGroupFilter)).Take(1000);
                    }

                    // New filters
                    if (!string.IsNullOrEmpty(customerFilter))
                    {
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Customer.Equals(customerFilter));
                    }

                    if (!string.IsNullOrEmpty(carrierFilter))
                    {
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Carrier.Equals(carrierFilter));
                    }

                    if (!string.IsNullOrEmpty(workcodeFilter))
                    {
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.work_code.Equals(workcodeFilter));
                    }

                    if (!string.IsNullOrEmpty(SupplierRef))
                    {

                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.SupplierRef.Contains(SupplierRef));
                    }

                    if (!string.IsNullOrEmpty(SellNett))
                    {
                        decimal SellNettValue = string.IsNullOrEmpty(SellNett) ? decimal.MaxValue : Convert.ToDecimal(SellNett);
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Sell_Total_Nett >= SellNettValue);
                    }




                    if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                        && string.IsNullOrEmpty(SupplierRef) && string.IsNullOrEmpty(customerGroupFilter) && string.IsNullOrEmpty(SellNett) && invoiceWeek == null)
                    {
                        cons = cons.Where(x => x.LoginName.Equals(loggeduser)).Take(1000);
                    }

                    cons = cons.Take(1000);
                  
                    return View(cons.ToList().Take(200));
                



                }
                catch (Exception ex)
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
        public async Task<ActionResult> TradingData(FormCollection formCollection)
        {
            string loggedUser = GetLoggedUser() ?? WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            if (string.IsNullOrEmpty(loggedUser))
            {
                return RedirectToAction("Login", "Account"); // Redirect if user not found
            }

            if (formCollection["ID"] == null && string.IsNullOrEmpty(formCollection["search"]))
            {
                return RedirectToAction("TradingData", new { message = "Please enter a search word or select connote(s) first!!", updatestatus = 2 });
            }

                
                
                
         
            var ids = formCollection["ID"]?.Split(new char[] { ',' });
            var comment = formCollection["Reason"]?.ToString();
         
            var selectedCustomerGroupValue = formCollection["selectedCustomerGroupValue"].ToString();
            var selectedCustomerValues = formCollection["selectedCustomerValue"]; // This might be a collection
            var selectedCarrierValues = formCollection["selectedCarrierValue"]; // This might be a collection
            var selectedWorkCodeValues = formCollection["selectedWorkCodeValue"]; // This might be a collection

          
            ViewBag.selectedCustomerGroup = selectedCustomerGroupValue;

            // For multiple value selections, converting them to arrays or lists as necessary
            // Ensure you check for null or use ?. operator to avoid NullReferenceException
            string[] customerFilter = selectedCustomerValues.ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            string[] carrierFilter = selectedCarrierValues.ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            string[] workcodeFilter = selectedWorkCodeValues.ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            // Then, you can use these arrays as needed, for example, setting them in ViewBag
            ViewBag.selectedCustomer = string.Join(", ", customerFilter);
            ViewBag.selectedCarrier = string.Join(", ", carrierFilter);
            ViewBag.selectedWorkCode = string.Join(", ", workcodeFilter);

            if (ids != null && ids.Any())
            {
                var heldIds = ids.Select(id => long.Parse(id)).ToList();
                using (var dbContext = new IVTEntities())
                {
                    var heldConnotes = await dbContext.vAllCurrentDetails
                                                     .Where(c => heldIds.Contains(c.PrimaryKeyField))
                                                     .ToListAsync();

                    if (!heldConnotes.Any())
                    {
                        return RedirectToAction("TradingData", new { message = "No connotes found to hold.", updatestatus = 2 });
                    }

                    var consignmentsToHold = heldConnotes.Select(heldConnote => new ConsignmentForReview
                    {
                        Connote = heldConnote.ConsignmentNote,
                        jno = heldConnote.JobNumber,
                        Reason = "AM Custom Held From Trading Data",
                        cons_date = heldConnote.Cons_Date.ToString(),
                        Customer = heldConnote.Customer,
                        Carrier = heldConnote.Carrier,
                        Sub_Reason = comment,
                        ErrCode = "9999",
                        ErrPriority = "P9999",
                        ShortErrDesc = "Custom Rule",
                        ReleasedBy = loggedUser,
                        InvoiceWeek = heldConnote.CurrentWeek,
                        Comment = comment,
                        ActionedBy = loggedUser,
                        ActionTime = DateTime.Now,
                    }).ToList();

                    dbContext.ConsignmentForReview.AddRange(consignmentsToHold);
                    await dbContext.SaveChangesAsync();

                    // Send notification and log activity
                    string subject = "IVT Hold Notification - " + loggedUser.ToUpper();
                    string emailBody = "Hi IVT Admins,<br><br>" + loggedUser.ToUpper() + " has <b>HELD</b> the following connotes:<br><br>";
                    string activityComment = "Held Details - ";
                    // Start the table with headers
                    emailBody += "<table style='border-collapse: collapse; width: 100%;'>" +
                                 "<tr style='background-color: #f2f2f2;'>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Connote</th>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Held By</th>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Customer</th>" +
                                 "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Comment</th>" +
                                 "</tr>";

                    // Assuming consignmentsToHold is obtained earlier in your method
                    foreach (var detail in consignmentsToHold)
                    {
                        emailBody += $"<tr>" +
                                     $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{detail.Connote}</td>" +
                                     $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{loggedUser}</td>" +
                                     $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{detail.Customer}</td>" +
                                     $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{detail.Comment}</td>" +
                                     $"</tr>";
                        activityComment += $"Connote: {detail.Connote}, Held By: {loggedUser}, Customer: {detail.Customer}, Comment: {detail.Comment}; ";
                    }

                    // Close the table
                    emailBody += "</table>";

                    emailBody += "<br><br>Please make sure to reflect the changes in the billing system and <b>ALWAYS</b> check the complete list in IVT2.0 System.<br><br>This is an Automated System Notification.<br><br>Invoice Vetting Tool (IVT2.0) <br>";

                    await SendNotificationAndLogActivity(subject, emailBody, activityComment, loggedUser, "Held Connote");



                    return RedirectToAction("TradingData", new { message = "Selected connotes have been held successfully", updatestatus = 1, customerGroupFilter =selectedCustomerGroupValue, customerFilter = selectedCustomerValues, carrierFilter = selectedCarrierValues, workcodeFilter=selectedWorkCodeValues});
                }
            }
            else if (!string.IsNullOrEmpty(formCollection["search"]))
            {
                return RedirectToAction("TradingData", new { search = formCollection["search"] });
            }

            return RedirectToAction("TradingData");
        }




        private async Task RefreshDashboardAsync()
        {
            using (IVTEntities obj = new IVTEntities())
            {
                await Task.Run(() => obj.Database.ExecuteSqlCommand("EXEC [WebIVT].[sp_IVTDashboardRefresh_TotalMeasure]"));
            }
        }
        public ActionResult HeldConsignment(string message, int? updatestatus, string loginname, string loggedas, string search, DateTime? despatchDate,
            string customerGroupFilter, string customerFilter, string carrierFilter)
        {
            try
            {

                string loggedUser = GetLoggedUser();

                if (string.IsNullOrEmpty(loggedUser))
                    loggedUser = GetWindowsIdentityName();

                SetViewBagOptions(loggedUser, message, updatestatus);

                var userinfo = GetUserInformation(loggedUser);

                if (userinfo == null || userinfo.IsActive != 1)
                    return HandleInactiveUser(userinfo);

                SetViewBagOptionsForActiveUser(userinfo.LoginName);
                IVTEntities Obj = new IVTEntities();

                IQueryable<vConsignmentForReview> cons = GetHeldConsignmentsResults(loggedUser, message, updatestatus, loginname, loggedas, search, despatchDate
             , customerGroupFilter, customerFilter, carrierFilter);

                SetUniqueValuesInViewBag_HeldConsignment(Obj, loggedUser);

                return View(cons.ToList());
            }
            catch (Exception ex)
            {
                // Log the exception and handle it appropriately
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
                    loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
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


        public ActionResult ViewReleasedConsignment(long Id)
        {
            string loggeduser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggeduser))
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            }
            ViewBag.LoggedUser = loggeduser;
            ViewBag.LoggedUserStatus = "visible";

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

        public ActionResult ViewTradingConsignment(string jno)
        {
            string loggeduser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggeduser))
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            }

            ViewBag.LoggedUser = loggeduser;
            ViewBag.LoggedUserStatus = "visible";

            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }

            IVTEntities Obj = new IVTEntities();




            var cons = Obj.vSellSide.Where(a => a.jno.Equals(jno)).FirstOrDefault();


            // At this point the query has generated a SQL statement based on the conditions above,
            // but it will not be executed until the until the next line - i.e. when calling .ToList()
            return View(cons);
        }

        public ActionResult ViewHistoricalConsignment(long Id)
        {
            string loggeduser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggeduser))
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            }

            ViewBag.LoggedUser = loggeduser;
            ViewBag.LoggedUserStatus = "visible";

            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }

            IVTEntities Obj = new IVTEntities();


            var cons = Obj.IVT_ResultHistory.Where(a => a.Id == Id).FirstOrDefault();


            // At this point the query has generated a SQL statement based on the conditions above,
            // but it will not be executed until the until the next line - i.e. when calling .ToList()
            return View(cons);
        }

        //       public ActionResult ViewAncillarySummaryDetails(string customer , string carrier, string workcode)
        //       { 
        //           try 
        //{
        //               string loggeduser = GetLoggedUser();
        //               if (string.IsNullOrEmpty(loggeduser))
        //               {
        //                   loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
        //               }

        //               ViewBag.LoggedUser = loggeduser;
        //           ViewBag.LoggedUserStatus = "visible";

        //           if (Session["LoggedAs"] != null)
        //           {

        //               ViewBag.DisplayForSuperAdmin = "visible";
        //           }

        //           IVTEntities Obj = new IVTEntities();


        //               //var cons = Obj.vAllCurrentDetails.Where(a => a.Customer == customer && a.Carrier==carrier && a.work_code==workcode).ToList();
        //               ViewBag.Customer = customer;
        //               ViewBag.Carrier = carrier;
        //               ViewBag.Workcode = workcode;

        //               //    // At this point the query has generated a SQL statement based on the conditions above,
        //               //    // but it will not be executed until the until the next line - i.e. when calling .ToList()
        //               //    return View(cons);

        //               // Retrieve records that match initial criteria

        //               var matchedRecords = Obj.vAllCurrentDetails
        //                    .Where(a => a.Customer == customer && a.Carrier == carrier && a.work_code == workcode)
        //                    .ToList();

        //               // Extract connotes from matched records
        //               var matchedConnotes = matchedRecords.Select(a => a.ConsignmentNote).ToList();

        //               // Remove suffixes from connotes
        //               var connotesWithoutSuffixes = matchedConnotes.Select(consignmentNote =>
        //               {
        //                   // Find the last index of a non-digit character
        //                   int index = consignmentNote.Length - 1;
        //                   while (index >= 0 && char.IsLetter(consignmentNote[index]))
        //                   {
        //                       index--;
        //                   }

        //                   // If a suffix is found, remove it; otherwise, return the original consignment note
        //                   if (index < consignmentNote.Length - 1)
        //                   {
        //                       return consignmentNote.Substring(0, index + 1);
        //                   }
        //                   return consignmentNote;
        //               });


        //               // Combine original connotes with connotesWithoutSuffixes
        //               // Extract connotes without suffixes and create new vAllCurrentDetails instances
        //               var connotesWithoutSuffixesRecords = connotesWithoutSuffixes.Select(consignmentNote =>
        //               {
        //                   var originalRecord = Obj.vAllCurrentDetails.FirstOrDefault(r => r.ConsignmentNote == consignmentNote);
        //                   if (originalRecord != null)
        //                   {
        //                       // Create a new instance of vAllCurrentDetails with the same properties as the original record
        //                       return new vAllCurrentDetails
        //                       {
        //                           PrimaryKeyField = originalRecord.PrimaryKeyField,
        //                           CurrentWeek = originalRecord.CurrentWeek,
        //                           AccountManager = originalRecord.AccountManager,
        //                           LoginName = originalRecord.LoginName,
        //                           ContactEmail = originalRecord.ContactEmail,
        //                           CustomerGroup = originalRecord.CustomerGroup,
        //                           Customer = originalRecord.Customer,
        //                           CarrierGroup = originalRecord.CarrierGroup,
        //                           Carrier = originalRecord.Carrier,
        //                           SupplierRef = originalRecord.SupplierRef,
        //                           ConsignmentNote = consignmentNote, // Override the ConsignmentNote with the modified one
        //                           JobNumber = originalRecord.JobNumber,
        //                           col_post = originalRecord.col_post,
        //                           del_post = originalRecord.del_post,
        //                           Col_loc = originalRecord.Col_loc,
        //                           Del_loc = originalRecord.Del_loc,
        //                           Cons_Date = originalRecord.Cons_Date,
        //                           Moveit_DespatchDate = originalRecord.Moveit_DespatchDate,
        //                           Moveit_FreightStatus = originalRecord.Moveit_FreightStatus,
        //                           Moveit_LineOfBusiness = originalRecord.Moveit_LineOfBusiness,
        //                           Moveit_SpecialIns = originalRecord.Moveit_SpecialIns,
        //                           Moveit_ActualDeliveryDate = originalRecord.Moveit_ActualDeliveryDate,
        //                           Moveit_TotWeightKg = originalRecord.Moveit_TotWeightKg,
        //                           Moveit_TotVolM = originalRecord.Moveit_TotVolM,
        //                           Moveit_RateIncSurch = originalRecord.Moveit_RateIncSurch,
        //                           from = originalRecord.from,
        //                           to = originalRecord.to,
        //                           option_code = originalRecord.option_code,
        //                           work_code = originalRecord.work_code,
        //                           quantity = originalRecord.quantity,
        //                           space = originalRecord.space,
        //                           lift = originalRecord.lift,
        //                           weight = originalRecord.weight,
        //                           chg_kg = originalRecord.chg_kg,
        //                           pallet = originalRecord.pallet,
        //                           hours = originalRecord.hours,
        //                           cubic = originalRecord.cubic,
        //                           cus_ref = originalRecord.cus_ref,
        //                           Sell_Freight_Charges = originalRecord.Sell_Freight_Charges,
        //                           Sell_Other_Charges = originalRecord.Sell_Other_Charges,
        //                           Sell_Fuel_Surch = originalRecord.Sell_Fuel_Surch,
        //                           Sell_Total_Nett = originalRecord.Sell_Total_Nett,
        //                           Buy_Freight_Charges = originalRecord.Buy_Freight_Charges,
        //                           Buy_Other_Charges = originalRecord.Buy_Other_Charges,
        //                           Buy_Fuel_Surcharge = originalRecord.Buy_Fuel_Surcharge,
        //                           Buy_Total_Nett = originalRecord.Buy_Total_Nett,
        //                           Nett_Margin = originalRecord.Nett_Margin,
        //                           FSL___Sell = originalRecord.FSL___Sell,
        //                           C_FSL_Buy = originalRecord.C_FSL_Buy,
        //                           FSL___Var = originalRecord.FSL___Var,
        //                           ExternalComments = originalRecord.ExternalComments
        //                       };
        //                   }
        //                   return null;
        //               }).Where(record => record != null).ToList();

        //               // Combine matched records with connotesWithoutSuffixesRecords
        //               var uniqueConnotes = matchedRecords.Concat(connotesWithoutSuffixesRecords).Distinct().ToList();


        //               // Pass the data to the ViewBag or any other appropriate method


        //               // At this point the query has generated a SQL statement based on the conditions above,
        //               // but it will not be executed until the until the next line - i.e. when calling .ToList()
        //               return View(uniqueConnotes);
        //           }
        //            catch (Exception ex)
        //            {

        //               return RedirectToAction("TimeOut", "Home");
        //           }

        //       }


        [HttpPost]
        public async Task<ActionResult> ViewAncillarySummaryDetails(FormCollection formCollection)
        {
            string loggedUser = GetLoggedUser() ?? WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            if (string.IsNullOrEmpty(loggedUser))
            {
                return RedirectToAction("Login", "Account"); // Ensure user is logged in
            }

            var ids = formCollection["ID"]?.Split(new char[] { ',' });
            var comment = formCollection["Reason"]?.ToString();

            if (ids == null || !ids.Any())
            {
                return RedirectToAction("ViewAncillarySummaryDetails", new { message = "Please select connote(s) first!!", updatestatus = 2 });
            }

            using (var dbContext = new IVTEntities())
            {
                var heldIds = ids.Select(long.Parse).ToList();
                var heldConnotes = await dbContext.vAllCurrentDetails
                                                 .Where(c => heldIds.Contains(c.PrimaryKeyField))
                                                 .ToListAsync();

                if (!heldConnotes.Any())
                {
                    return RedirectToAction("ViewAncillarySummaryDetails", new { message = "No connotes found to hold.", updatestatus = 2 });
                }

                foreach (var heldConnote in heldConnotes)
                {
                    dbContext.ConsignmentForReview.Add(new ConsignmentForReview
                    {
                        Connote = heldConnote.ConsignmentNote,
                        jno = heldConnote.JobNumber,
                        Reason = "AM Custom Held From Ancillary Detail Data",
                        cons_date = heldConnote.Cons_Date.ToString(),
                        Customer = heldConnote.Customer,
                        Carrier = heldConnote.Carrier,
                        Sub_Reason = comment,
                        ErrCode = "9999",
                        ErrPriority = "P9999",
                        ShortErrDesc = "Custom Rule",
                        ReleasedBy = loggedUser,
                        InvoiceWeek = heldConnote.CurrentWeek,
                        Comment = comment,
                        ActionedBy = loggedUser,
                        ActionTime = DateTime.Now,
                    });
                }

                await dbContext.SaveChangesAsync();


                // Send notification and log activity
                string subject = "IVT Hold Notification - " + loggedUser.ToUpper();
                string emailBody = "Hi IVT Admins,<br><br>" + loggedUser.ToUpper() + " has <b>HELD</b> the following connotes:<br><br>";
                string activityComment = "Held Details - ";
                // Start the table with headers
                emailBody += "<table style='border-collapse: collapse; width: 100%;'>" +
                             "<tr style='background-color: #f2f2f2;'>" +
                             "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Connote</th>" +
                             "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Held By</th>" +
                             "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Customer</th>" +
                             "<th style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>Comment</th>" +
                             "</tr>";

                // Assuming consignmentsToHold is obtained earlier in your method
                foreach (var detail in heldConnotes)
                {
                    emailBody += $"<tr>" +
                                 $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{detail.ConsignmentNote}</td>" +
                                 $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{loggedUser}</td>" +
                                 $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{detail.Customer}</td>" +
                                 $"<td style='border: 1px solid #dddddd; text-align: left; padding: 8px;'>{comment}</td>" +
                                 $"</tr>";
                    activityComment += $"Connote: {detail.ConsignmentNote}, Held By: {loggedUser}, Customer: {detail.Customer}, Comment: {comment}; ";
                }

                // Close the table
                emailBody += "</table>";

                emailBody += "<br><br>Please make sure to reflect the changes in the billing system and <b>ALWAYS</b> check the complete list in IVT2.0 System.<br><br>This is an Automated System Notification.<br><br>Invoice Vetting Tool (IVT2.0) <br>";

                await SendNotificationAndLogActivity(subject, emailBody, activityComment, loggedUser, "Held Connote");
                //  await SendHeldNotificationAndLogActivity(loggedUser, ids, comment);

                return RedirectToAction("ViewAncillarySummaryDetails", new
                {
                    message = "Selected connotes have been held successfully",
                    updatestatus = 1,
                    customer = heldConnotes.First().Customer, // Assuming you want to redirect with the first held connote's details
                    carrier = heldConnotes.First().Carrier,
                    workcode = heldConnotes.First().work_code
                });
            }
        }
        public ActionResult ViewHeldConsignment(long Id)
        {
            string loggeduser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggeduser))
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            }

            ViewBag.LoggedUser = loggeduser;
            ViewBag.LoggedUserStatus = "visible";

            using (IVTEntities dbContext = new IVTEntities())
            {
                var cons = dbContext.vConsignmentForReview.Where(a => a.Id == Id).FirstOrDefault();

                if (cons == null)
                {
                    return RedirectToAction("HeldConsignment", new { message = "Consignment not found!", updatestatus = 2 });
                }

                // Retrieve logs for this consignment
                var consignmentLogs = dbContext.Consignment_LogBook
                                        .Where(l => l.JobNumber == cons.jno && !l.IsDeleted)
                                        .OrderByDescending(l => l.ActionTime)
                                        .ToList();

                ViewBag.ConsignmentLogs = consignmentLogs;

                return View(cons);
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

        public ActionResult HoldTradingConsignment(string jno)
        {
            using (IVTEntities Obj = new IVTEntities())
            {
                // int CommentId = int.Parse(Id);

                var con = Obj.vAllCurrentDetails.Where(a => a.JobNumber == jno).FirstOrDefault();

                return View(con);
            }
        }



        [HttpPost]

        public async Task<ActionResult> HoldTradingConsignment(vAllCurrentDetails Updatedconnote, FormCollection formCollection)
        {
            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }

            try
            {
                string loggeduser = GetLoggedUser();
                if (string.IsNullOrEmpty(loggeduser))
                {
                    loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
                }

                string customer = "";
                if (Updatedconnote != null)
                {
                 


                    DateTime myDateTime = DateTime.Now;
                    // var Currentcon = Obj.vConnoteToRelease.Where(a => a.id == Updatedconnote.id).FirstOrDefault();

                    var heldId = Updatedconnote.JobNumber;
                    var heldConnote = ivtEntities.vAllCurrentDetails.Where(a => a.JobNumber == heldId).FirstOrDefault();

                    using (IVTEntities Obj = new IVTEntities())
                    {
                        // Copy relevant properties from HeldConsignment to ConsignmentForReview
                        ConsignmentForReview connotetoHold = new ConsignmentForReview
                        {
                            Connote = heldConnote.ConsignmentNote,
                            jno = heldConnote.JobNumber,
                            Reason ="AM Custom Hold From Trading Data",
                            cons_date = heldConnote.Cons_Date.ToString(),
                            Customer = heldConnote.Customer,
                            Carrier = heldConnote.Carrier,
                            Sub_Reason = "AM Custom Hold From Trading Data",
                            ErrCode = "9999",
                            ErrPriority ="P9999",
                            ShortErrDesc = "Custom Rule",
                            ReleasedBy = heldConnote.AccountManager,
                            InvoiceWeek = heldConnote.CurrentWeek,
                            Comment = formCollection["HoldReason"].ToString(),
                            // Copy other relevant properties from ConsignmentForReview to ReleasedConsignment
                            ActionedBy = loggeduser,
                            ActionTime = DateTime.Now,
                        };
                      //  Obj.sp_IVT_Portal_Log(authinticated_user.Split('\\').Last(), "Connote:" + connotetoHold.Connote + " has been held");
                        Obj.ConsignmentForReview.Add(connotetoHold);
                        // Obj.ReleasedConsignment.Remove(heldConnote);


                        Obj.SaveChanges();


                        //-----------Send Notification------------------------------------
                        var ActionedBy = loggeduser.ToUpper();

                        string body = "Hi IVT Admins,<br><br>";
                        string ActivityComment = "Held Details - ";
                        body += ActionedBy + " has <b>Held</b> Connotes <br><br>";

                        if (heldConnote != null)
                        {
                            body += "The following connote has been <b>HELD</b>:<br><br>";
                            var connote = heldConnote.ConsignmentNote;
                            var AccountManager = ActionedBy;
                            var Customer = heldConnote.Customer;
                            var Comment = formCollection["Reason"].ToString();

                            body += $"Connote: {connote},  Held By: {AccountManager},  Customer: {Customer},  Comment: {Comment}<br>";

                            ActivityComment += $"Connote: {connote},  Held By: {AccountManager},  Customer: {Customer},  Comment: {Comment}<br>";
                        }



                        body += "<br><br>Please make sure to reflect the changes on billing system and <b>ALWAYS</b> check  the complete list in IVT2.0 System.<br><br><br><br><br><br><br> ";

                        body += "This is an Automated System Notification.<br><br> ";
                        body += "Invoice Vetting Tool (IVT2.0) <br> ";

                        await EmailNotification.SendNotificationEmailAsync("IVT Hold Notification - " + ActionedBy, body, loggeduser);

                        await EmailNotification.AM_Activity(ActivityComment, "Held Connote", ActionedBy);


                        ViewBag.message = "Connote " + heldConnote.ConsignmentNote + " has been held";

                        //Log----

                        using (IVTEntities Obj2 = new IVTEntities())
                        {
                            // Copy relevant properties from ConsignmentForReview to ReleasedConsignment

                            await Task.Run(() => Obj.Database.ExecuteSqlCommand("EXEC [WebIVT].[sp_IVTDashboardRefresh_TotalMeasure]"));

                        }
                        // ViewBag.message = "Consignment " + Updatedconnote.Connote + " hold reasons updated";
                        return RedirectToAction("TradingData", new
                        {

                            message = "Consignment has been held sucessfully",
                            updatestatus = 1
                        });

                    }
                }
                else
                {

                    return RedirectToAction("TradingData", new
                    {

                        message = "Consignment has failed to be held",
                        updatestatus = 2
                    });
                }
            }
            catch (Exception ex)
            {

                return RedirectToAction("TradingData", new
                {

                    message = "Consignment has failed to be held",
                    updatestatus = 2
                }); ;
            }

        }

        [HttpPost]

        public async Task<ActionResult> HoldConsignment(vIVT_Result Updatedconnote, FormCollection formCollection)
        {
            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }

            try
            {
                string loggeduser = GetLoggedUser();
                if (string.IsNullOrEmpty(loggeduser))
                {
                    loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
                }

                string customer = "";
                if (Updatedconnote != null)
                {
                  
                   
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
                                ActionedBy = loggeduser,
                                ActionTime = DateTime.Now,
                            };
                      //  Obj.sp_IVT_Portal_Log(authinticated_user.Split('\\').Last(), "Connote:" + connotetoHold.Connote + " has been held");
                        Obj.ConsignmentForReview.Add(connotetoHold);
                        // Obj.ReleasedConsignment.Remove(heldConnote);

                        
                        Obj.SaveChanges();

                      
                        //-----------Send Notification------------------------------------
                        var ActionedBy = loggeduser.ToUpper();

                        string body = "Hi IVT Admins,<br><br>";
                        string ActivityComment = "Held Details - ";
                        body += "The following connote has been <b>HELD</b>:<br><br>";

                        if (heldConnote != null)
                        {
                            var connote = heldConnote.Connote;
                            var AccountManager = ActionedBy;
                            var Customer = heldConnote.customer;
                            var Comment = formCollection["Reason"].ToString();

                            body += $"Connote: {connote},  Held By: {AccountManager},  Customer: {Customer},  Comment: {Comment}<br>";
                            ActivityComment += $"Connote: {connote},  Held By: {AccountManager},  Customer: {Customer},  Comment: {Comment}<br>";
                        }



                        body += "<br><br><br>Please make sure to reflect the changes on billing system and <b>ALWAYS</b> check  the complete list in IVT2.0 System.<br><br><br><br><br><br><br> ";

                        body += "This is an Automated System Notification.<br><br> ";
                        body += "Invoice Vetting Tool (IVT2.0) <br> ";

                        await EmailNotification.SendNotificationEmailAsync("IVT Hold Notification - " + ActionedBy, body, loggeduser);

                        await EmailNotification.AM_Activity(ActivityComment, "Held Connote", ActionedBy);

                        ViewBag.message = "Connote " + heldConnote.Connote + " has been held";

                        //Log----

                        using (IVTEntities Obj2 = new IVTEntities())
                        {
                            // Copy relevant properties from ConsignmentForReview to ReleasedConsignment
                            await Task.Run(() => Obj2.Database.ExecuteSqlCommand("EXEC [WebIVT].[sp_IVTDashboardRefresh_TotalMeasure]"));
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
            string loggeduser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggeduser))
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            }

            ViewBag.LoggedUser = loggeduser;
            ViewBag.LoggedUserStatus = "visible";
            using (IVTEntities Obj = new IVTEntities())
            {
                // int CommentId = int.Parse(Id);

                var con = Obj.vConsignmentForReview.Where(a => a.Id == Id).FirstOrDefault();

                return View(con);
            }
        }

        

            public ActionResult DownloadCSV_AncillarySummary()
        {


            string loggeduser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggeduser))
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            }



            IVTEntities Obj = new IVTEntities();
            IQueryable<AncillarySummary> held_cons = Obj.AncillarySummary;
            held_cons = held_cons.Where(x => x.LoginName.Equals(loggeduser));
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



        public ActionResult DownloadCSV_TradingData(string search, string customerGroupFilter, string customerFilter, string carrierFilter, string workcodeFilter, string SupplierRef, string SellNett)
        {


            string loggedUser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggedUser))
            {
                loggedUser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            }

            string[] customerFilters = string.IsNullOrWhiteSpace(customerFilter) ? null : customerFilter.Split(',');
            string[] carrierFilters = string.IsNullOrWhiteSpace(carrierFilter) ? null : carrierFilter.Split(',');
            string[] workcodeFilters = string.IsNullOrWhiteSpace(workcodeFilter) ? null : workcodeFilter.Split(',');


            IVTEntities Obj = new IVTEntities();
        

            IEnumerable<vAllCurrentDetails> cons = GetTradingDataResults_CSVDownload(loggedUser, null, null, loggedUser, null, search, null,
                                    customerGroupFilter, customerFilters, carrierFilters, workcodeFilters, SupplierRef, SellNett);

            // Retrieve data for CSV
            var data = cons.ToList();


            var csvContent = new StringBuilder();
            csvContent.AppendLine("CurrentWeek,CustomerGroup,Customer,CarrierGroup,Carrier,SupplierRef,ConsignmentNote,JobNumber,Cons_Date,cus_ref,Moveit_DespatchDate,Moveit_FreightStatus,from,to,option_code,work_code,AncillaryDescription,quantity,space,lift,weight,chg_kg,pallet,hours,cubic,Sell_Freight_Charges,Sell_Other_Charges,Sell_Fuel_Surch,Sell_Total_Nett,Buy_Freight_Charges,Buy_Other_Charges,Buy_Fuel_Surcharge,Buy_Total_Nett,Nett_Margin,FSL_%_Sell,FSL_%_Buy,FSL_%_Var,ExternalComments,Moveit_TotWeightKg,Moveit_TotVolM,Moveit_RateIncSurch,Moveit_LineOfBusiness,Moveit_ActualDeliveryDate");

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
                        cus_ref = item.cus_ref,
                        Moveit_DespatchDate = item.Moveit_DespatchDate.ToString(),
                        Moveit_FreightStatus = item.Moveit_FreightStatus,
                        from = item.from,
                        to = item.to,
                        option_code = item.option_code,
                        work_code = item.work_code,
                        AncillaryDescription = item.AncillaryDescription,
                        quantity = item.quantity,
                        space = item.space,
                        lift = item.lift,
                        weight = item.weight,
                        chg_kg = item.chg_kg,
                        pallet = item.pallet,
                        hours = item.hours,
                        cubic = item.cubic,
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
                return File(memoryStream.ToArray(), "text/csv", "IVT_TradingData_Download_" + loggedUser.ToUpper() + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv");
            }


            // Return CSV file

        }

        public ActionResult DownloadCSV_TradingDataHistory(string customerFilter, string carrierFilter, string workcodeFilter, string SupplierRef, string SellNett, DateTime? invoiceWeek)
        {


            string loggeduser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggeduser))
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            }



            IVTEntities Obj = new IVTEntities();
            IQueryable<IVT_TradingDataHistory> cons = Obj.IVT_TradingDataHistory;

            if (invoiceWeek != null)
            {


                cons = cons
                    .Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value);
                    
            }
            // New filters
            if (!string.IsNullOrEmpty(customerFilter))
            {
                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Customer.Equals(customerFilter));
            }

            if (!string.IsNullOrEmpty(carrierFilter))
            {
                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Carrier.Equals(carrierFilter));
            }

            if (!string.IsNullOrEmpty(workcodeFilter))
            {
                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.work_code.Equals(workcodeFilter));
            }

            if (!string.IsNullOrEmpty(SupplierRef))
            {

                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.SupplierRef.Contains(SupplierRef));
            }

            if (!string.IsNullOrEmpty(SellNett))
            {
                decimal SellNettValue = string.IsNullOrEmpty(SellNett) ? decimal.MaxValue : Convert.ToDecimal(SellNett);
                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.Sell_Total_Nett >= SellNettValue);
            }


           

            if ( string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                && string.IsNullOrEmpty(SupplierRef) && string.IsNullOrEmpty(SellNett) && invoiceWeek == null)
            {
                cons = cons.Where(x => x.LoginName.Equals(loggeduser));
            }

         
            var data = cons.ToList();


            // Generate CSV content
            var csvContent = "CurrentWeek,CustomerGroup,Customer,CarrierGroup,Carrier,SupplierRef,ConsignmentNote,JobNumber,Cons_Date,Moveit_DespatchDate,Moveit_FreightStatus,from,to,option_code,work_code,quantity,space,lift,weight,chg_kg,pallet,hours,cubic,cus_ref,Sell_Freight_Charges,Sell_Other_Charges,Sell_Fuel_Surch,Sell_Total_Nett,Buy_Freight_Charges,Buy_Other_Charges,Buy_Fuel_Surcharge,Buy_Total_Nett,Nett_Margin,FSL_%_Sell,FSL_%_Buy,FSL_%_Var,ExternalComments\n";

            foreach (var item in data)
            {
                csvContent += $"{item.CurrentWeek},{item.CustomerGroup},{item.Customer},{item.CarrierGroup},{item.Carrier},{item.SupplierRef},{item.ConsignmentNote},{item.JobNumber},{item.Cons_Date},{item.Moveit_DespatchDate},{item.Moveit_FreightStatus},{item.from},{item.to},{item.option_code},{item.work_code},{item.quantity},{item.space},{item.lift},{item.weight},{item.chg_kg},{item.pallet},{item.hours},{item.cubic},{item.cus_ref},{item.Sell_Freight_Charges},{item.Sell_Other_Charges},{item.Sell_Fuel_Surch},{item.Sell_Total_Nett},{item.Buy_Freight_Charges},{item.Buy_Other_Charges},{item.Buy_Fuel_Surch},{item.Buy_Total_Nett},{item.Nett_Margin},{item.FSL___Sell},{item.Buy_Fuel_Surch},{item.FSL___Var},{item.ExternalComments}\n";
            }


            // Return CSV file
            return File(new System.Text.UTF8Encoding().GetBytes(csvContent), "text/csv", "IVT_TradingDataHistory_Download.csv");
        }

        public ActionResult DownloadCSV_AncillaryDetailsData(string customer, string carrier, string workcode)
        {


            string loggeduser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggeduser))
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
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

        public ActionResult DownloadCSV_HeldConsignment()
        {


            string loggeduser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggeduser))
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            }



            IVTEntities Obj = new IVTEntities();
            IQueryable<vConsignmentForReview> held_cons = Obj.vConsignmentForReview;
            held_cons = held_cons.Where(x => x.ActionedBy.Equals(loggeduser));
            // Retrieve data for CSV
            var data = held_cons.ToList();


            // Generate CSV content
            var csvContent = "InvoiceWeek,Connote,Customer,Held Reason,ErrCode,Exception,Carrier,ConnoteDate,Exception Detail\n";
            foreach (var item in data)
            {
                csvContent += $"{item.InvoiceWeek},{item.Connote},{item.Customer},{item.Comment},{item.ErrCode},{item.Reason},{item.Carrier},{item.cons_date},{item.Sub_Reason}\n";
            }

            // Return CSV file
            return File(new System.Text.UTF8Encoding().GetBytes(csvContent), "text/csv", "IVT_Held_Connote_Download.csv");
        }


        public ActionResult DownloadCSV_IVTResults()
        {


            string loggeduser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggeduser))
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            }



            IVTEntities Obj = new IVTEntities();
            IQueryable<vIVT_Result> cons = Obj.vIVT_Result;
            cons = cons.Where(x => x.HeldBy.Equals(loggeduser));
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






        public ActionResult DownloadCSV_IVTResultsHistory(  string search
          , string customerFilter, string carrierFilter, string workcodeFilter, string minMargin, string maxMargin, DateTime? invoiceWeek)
        {


            string loggeduser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggeduser))
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            }



            IVTEntities Obj = new IVTEntities();
            IQueryable<IVT_ResultHistory> cons = Obj.IVT_ResultHistory;

            if (invoiceWeek != null)
            {


                cons = cons
                    .Where(x => x.LoginName.Equals(loggeduser) && x.CurrentWeek == invoiceWeek.Value);
            }

            if (!string.IsNullOrEmpty(search))
            {
                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.con_note.Contains(search) || x.Reason.Contains(search) || x.customer.Contains(search) || x.ErrCode.Contains(search));
            }

            // New filters
            if (!string.IsNullOrEmpty(customerFilter))
            {
                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.customer.Equals(customerFilter));
            }

            if (!string.IsNullOrEmpty(carrierFilter))
            {
                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.carrier.Equals(carrierFilter));
            }

            if (!string.IsNullOrEmpty(workcodeFilter))
            {
                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.work_code.Equals(workcodeFilter));
            }

            if (!string.IsNullOrEmpty(minMargin))
            {
                decimal minMarginValue = string.IsNullOrEmpty(minMargin) ? 0 : Convert.ToDecimal(minMargin);

                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.GrossMargin >= minMarginValue);
            }

            if (!string.IsNullOrEmpty(maxMargin))
            {
                decimal maxMarginValue = string.IsNullOrEmpty(maxMargin) ? decimal.MaxValue : Convert.ToDecimal(maxMargin);
                cons = cons.Where(x => x.LoginName.Equals(loggeduser) && x.GrossMargin <= maxMarginValue);
            }

          

            if (string.IsNullOrEmpty(search) && string.IsNullOrEmpty(customerFilter) && string.IsNullOrEmpty(carrierFilter) && string.IsNullOrEmpty(workcodeFilter)
                && string.IsNullOrEmpty(minMargin) && string.IsNullOrEmpty(maxMargin))
            {
                cons = cons.Where(x => x.HeldBy.Equals(loggeduser));
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

        // Controller Action


        public ActionResult Dashboard()
        {
            string loggeduser = GetLoggedUser();
            if (string.IsNullOrEmpty(loggeduser))
            {
                loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
            }


            if (Session["LoggedAs"] != null)
            {

                ViewBag.DisplayForSuperAdmin = "visible";
            }
            else
            {
                ViewBag.DisplayForSuperAdmin = "hidden";
            }

            ViewBag.LoggedUser = loggeduser;
            ViewBag.LoggedUserStatus = "visible";

            try
            {
                IVTEntities Obj = new IVTEntities();
                //var commonMeasureData = Obj.Dash_CommonMeasure.OrderByDescending(x => x.TotCount).Where(x => x.loginname.Equals(loggeduser)).Take(10).ToList();
                var top10CarrierData = Obj.Dash_Top10Carrier.Where(x => x.LoginName.Equals(loggeduser))
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


                var top10CustomerData = Obj.Dash_Top10Customer.Where(x => x.LoginName.Equals(loggeduser))
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

                var top10ExceptionData = Obj.Dash_Top10Exception.Where(x => x.LoginName.Equals(loggeduser))
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



                var filteredData = Obj.vDash_TotalMeasures
     .Where(x => x.LoginName.Equals(loggeduser))
     .ToList(); // Materialize the query

                var totalMeasuresData = new List<vDash_TotalMeasures>
{
    new vDash_TotalMeasures
    {
        TotalExceptions = filteredData?.Max(x => x.TotalExceptions) ?? 0,
        TotalCustomer = filteredData?.Sum(x => x.TotalCustomer) ?? 0,
        TotalCarrier = filteredData?.Sum(x => x.TotalCarrier) ?? 0,
        SellTotalNett = filteredData?.Sum(x => x.SellTotalNett) ?? 0,
        BuyTotalNett = filteredData?.Sum(x => x.BuyTotalNett) ?? 0,
        TotalWeight = filteredData?.Sum(x => x.TotalWeight) ?? 0,
        TotalCubic = filteredData?.Sum(x => x.TotalCubic) ?? 0,
        TotalQuantity = filteredData?.Sum(x => x.TotalQuantity) ?? 0,
        HeldCount = filteredData?.Sum(x => x.HeldCount) ?? 0,
          ExceptionRate = filteredData?.Sum(x => x.ExceptionRate),
        AncillariesRate = filteredData?.Sum(x => x.AncillariesRate),
        TotalAllConnotes = filteredData?.Sum(x => x.TotalAllConnotes) ?? 0,
         TotalManualEntry =filteredData.Sum(x => (int?)x.TotalManualEntry) ?? 0,
         TotalNotInMoveit = filteredData.Sum(x => (int?)x.TotalNotInMoveit) ?? 0,
          CurrentWeek_ManualEntryRate =  (decimal?) (filteredData.Any() && filteredData.Sum(x => x.TotalManualEntry) > 0 ? filteredData.Sum(x => (int?)x.TotalManualEntry) * 100.0 / filteredData.Sum(x => x.TotalAllConnotes) : 0),
          CurrentWeek_NotInMoveitRate =(decimal?) ( filteredData.Any() && filteredData.Sum(x => x.TotalNotInMoveit) > 0 ? filteredData.Sum(x => (int?)x.TotalNotInMoveit) * 100.0 / filteredData.Sum(x => x.TotalAllConnotes) : 0),
        TotalAncillaries = filteredData?.Sum(x => x.TotalAncillaries) ?? 0
    }
};


                var LastIVTRunData = Obj.Dash_LastIVTRun.Where(x => x.LoginName.Equals(loggeduser))
           .GroupBy(x => x.CurrentWeek)
           .Select(group => new
           {
               CurrentWeek = group.Key,
               TotCount = group.Sum(x => x.TotCount)
           })
           .OrderByDescending(x=>x.CurrentWeek)
           .Take(5)
           .AsEnumerable() // Switch to LINQ to Objects
           .Select(item => new Dash_LastIVTRun
           {
               CurrentWeek = item.CurrentWeek,
               TotCount = item.TotCount
           })
           .ToList();

                //----Customer  Counts-----------------
                var CustomerCount = from data in Obj.vDash_CustomerCounts.Where(x => x.LoginName.Equals(loggeduser))
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


                // To get the final result as a list
                var CustomerCountList = CustomerCount.ToList();




                var currentWeek = Obj.FileLoadControl.FirstOrDefault()?.InvoiceWeek.ToString("dd/MM/yyyy");


                // Assuming DashExceptionSummary and vDash_Exception_Summary have similar properties
                var exception_summary_list = Obj.vDash_Exception_Summary
                    .Where(x => x.LoginName.Equals(loggeduser))
                    .Select(x => new DashExceptionSummary
                    {
                        ErrDesc = x.ErrDesc,
                        RuleCode = x.ErrCode,
                        RuleCategory=x.ErrPriority,
                        CurrentWeekCount = x.CurrentWeekCount,
                        LastWeekCount = x.LastWeekCount,
                        Last13WeeksAvgCount = x.Last13WeeksAvgCount
       
    })
             .OrderBy(x => x.RuleCode)
           .ToList();


                var viewModel = new DashboardViewModel
                {
                   // CommonMeasureData = commonMeasureData,
                    Top10CarrierData = top10CarrierData,
                    Top10CustomerData = top10CustomerData,
                    Top10ExceptionData = top10ExceptionData,
                    TotalMeasuresData = totalMeasuresData,
                    LastIVTRunData = LastIVTRunData,
                    CurrentWeek = currentWeek,
                    Exception_summary_list= exception_summary_list,
                    Customer_Count_list=CustomerCountList
                };

                return View(viewModel);

            }
            catch (Exception ex)
            {

                return RedirectToAction("TimeOut", "Home");
            }

            
        }


        [HttpPost]
        public async Task<ActionResult> ReleaseConsignment(ConsignmentForReview connotetoRelease,FormCollection formCollection)
        {
            try
            {
                string loggeduser = GetLoggedUser();
                if (string.IsNullOrEmpty(loggeduser))
                {
                    loggeduser = WindowsIdentity.GetCurrent().Name.Split('\\').Last();
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



    }
}