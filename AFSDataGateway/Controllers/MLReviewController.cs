using AFSDataGateway.Models;
using AFSDataGateway.Services.Chat;
using AFSDataGateway.Services.ML;
using System;
using System.Data.Entity;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace AFSDataGateway.Controllers
{
    // "ML Findings": anomalies detected by the ML pipeline (shadow mode) with assessment, feeding ML.ReviewFeedback
    // so the models can be retrained on confirmed outcomes.
    [Authorize]
    public sealed class MLReviewController : Controller
    {
        [HttpGet]
        public ActionResult Index(DateTime? week, string status, string stratum, string notice, bool? error)
        {
            using (var db = new IVTEntities())
            {
                ChatAccessScope scope;
                try { scope = Resolve(db); }
                catch (ChatAccessException) { return new HttpStatusCodeResult(403, "ML Findings is not available for this account."); }
                var page = new MLReviewService(db, scope).Load(week, status, stratum);
                page.Message = notice;
                page.MessageIsError = error == true;
                ViewBag.LoggedUser = scope.LoginName;
                ViewBag.LoggedUserStatus = "visible";
                ViewBag.IsAdmin = scope.IsAdmin;
                ViewBag.DisplayForSuperAdmin = scope.IsAdmin ? "visible" : null;
                return View(page);
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Assess(string snapshotKey, string reviewOutcome, string outcomeType, decimal? correctedBuyAmount, decimal? correctedSellAmount,
                                   decimal? confirmedFinancialImpact, string comments, DateTime? week, string status, string stratum)
        {
            string problem;
            try
            {
                using (var db = new IVTEntities())
                {
                    var scope = Resolve(db);
                    problem = new MLReviewService(db, scope).SaveFeedback(snapshotKey, reviewOutcome, outcomeType, correctedBuyAmount, correctedSellAmount,
                                                                          confirmedFinancialImpact, comments);
                }
            }
            catch (ChatAccessException) { problem = "Your access changed. Please refresh the page."; }
            catch (Exception ex)
            {
                Trace.TraceError("ML review save failed: {0}", ex.GetType().Name);
                problem = "The assessment could not be saved. Please try again.";
            }
            return RedirectToAction("Index", new { week = week.HasValue ? week.Value.ToString("yyyy-MM-dd") : null, status, stratum,
                notice = problem ?? "Assessment saved. Thank you, it will be used when the models are retrained.", error = problem != null });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Hold(string snapshotKey, string holdReason, string holdComments, DateTime? week, string status, string stratum)
        {
            MLHoldResult result;
            string actor = null;
            try
            {
                using (var db = new IVTEntities())
                {
                    var scope = Resolve(db);
                    actor = scope.IsAdmin ? scope.Actor : scope.LoginName;
                    result = new MLReviewService(db, scope).Hold(snapshotKey, holdReason, holdComments);
                }
            }
            catch (ChatAccessException) { result = new MLHoldResult { Error = "Your access changed. Please refresh the page." }; }
            catch (Exception ex)
            {
                Trace.TraceError("ML finding hold failed: {0}", ex.GetType().Name);
                result = new MLHoldResult { Error = "The connote could not be held. Please try again." };
            }

            if (result.Error == null)
            {
                // The hold is saved; a notification failure must not report it as failed.
                try { await NotifyHold(result, actor.ToUpper()); }
                catch (Exception ex) { Trace.TraceError("ML finding hold notification failed: {0}", ex.GetType().Name); }
            }
            return RedirectToAction("Index", new { week = week.HasValue ? week.Value.ToString("yyyy-MM-dd") : null, status, stratum,
                notice = result.Error ?? "Connote " + result.Connote + " has been held.", error = result.Error != null });
        }

        // Same notifications as a hold from Trading Data.
        private static async Task NotifyHold(MLHoldResult held, string actionedBy)
        {
            var reason = HttpUtility.HtmlEncode(held.Comment.Length == 0 ? held.Reason : held.Reason + " - " + held.Comment);
            var details = $"Connote: {HttpUtility.HtmlEncode(held.Connote)},  Held By: {actionedBy},  Customer: {HttpUtility.HtmlEncode(held.Customer)},  Reason: {reason}<br>";
            var body = "Hi IVT Admins,<br><br>" + actionedBy + " has <b>Held</b> Connotes from ML Findings<br><br>"
                     + "The following connote has been <b>HELD</b>:<br><br>" + details
                     + "<br><br>Please make sure to reflect the changes on billing system and <b>ALWAYS</b> check  the complete list in IVT2.0 System.<br><br><br><br><br><br><br> "
                     + "This is an Automated System Notification.<br><br> Invoice Vetting Tool (IVT2.0) <br> ";
            await EmailNotification.SendNotificationEmailAsync("IVT Hold Notification - " + actionedBy, body, actionedBy);
            await EmailNotification.AM_Activity("Held Details (ML Findings) - " + details, "Held Connote", actionedBy);
            using (var db = new IVTEntities())
                await db.Database.ExecuteSqlCommandAsync("EXEC [WebIVT].[sp_IVTDashboardRefresh_TotalMeasure]");
        }

        private ChatAccessScope Resolve(IVTEntities db)
        {
            db.Database.CommandTimeout = 30;
            return ChatAccessScope.Resolve(User, Session["LoggedAs"] as string, db.vIVTUser.AsNoTracking());
        }
    }
}
