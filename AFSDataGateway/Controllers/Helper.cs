using AFSDataGateway.Services;

using AFSDataGateway.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web;

namespace AFSDataGateway.Controllers
{
    public static class Helper
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

    

    public class EmailNotification
    {

        public static async Task SendNotificationEmailAsync(string subject, string body,string loggedUser)
        {
            string[] adminContactEmails = await GetAdminContactEmailsAsync();
            string[] financeContactEmails = await GetFinanceContactEmailsAsync();
            string amEmail = UserIdentity.EmailFor(string.IsNullOrWhiteSpace(loggedUser) ? UserIdentity.CurrentLogin() : loggedUser);

            // Admin contacts in "To"; finance contacts and the Account Manager in "CC".
            var cc = financeContactEmails.Select(x => x.Trim()).ToList();
            if (!string.IsNullOrEmpty(amEmail)) cc.Add(amEmail.Trim());
            await SendAsync(adminContactEmails.Select(x => x.Trim()), cc, subject, body, null);
        }

        private static string SenderAddress
        {
            get { return Services.Entra.GraphMail.ApprovedSenders[0]; }
        }

        // Single transport for every IVT email: Microsoft Graph sendMail from AFSReports with an app-only token.
        // Requires IVT_ENTRA_TENANT_ID, IVT_ENTRA_CLIENT_ID and IVT_ENTRA_CLIENT_SECRET on the server.
        private static async Task SendAsync(IEnumerable<string> to, IEnumerable<string> cc, string subject, string htmlBody, IEnumerable<Attachment> attachments)
        {
            if (!Services.Entra.GraphMail.AppSendConfigured)
                throw new InvalidOperationException("IVT email needs the Entra app registration (IVT_ENTRA_TENANT_ID, IVT_ENTRA_CLIENT_ID, IVT_ENTRA_CLIENT_SECRET).");
            var toList = (to ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var ccList = (cc ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var files = (attachments ?? Enumerable.Empty<Attachment>()).Select(a =>
            {
                using (a)
                using (var buffer = new System.IO.MemoryStream())
                {
                    if (a.ContentStream.CanSeek) a.ContentStream.Position = 0;
                    a.ContentStream.CopyTo(buffer);
                    return new Services.Entra.GraphMail.MailAttachment
                    {
                        Name = a.Name,
                        ContentType = a.ContentType != null ? a.ContentType.MediaType : null,
                        Content = buffer.ToArray()
                    };
                }
            }).ToList();
            await Services.Entra.GraphMail.SendAsAppAsync(SenderAddress, toList, ccList, subject, htmlBody, files).ConfigureAwait(false);
        }

        // Sends an email to one recipient (used by the IVT assistant for "email me" reports), with optional attachments.
        public static Task SendToUserAsync(string toAddress, string subject, string htmlBody, IEnumerable<Attachment> attachments = null)
        {
            return SendAsync(new[] { toAddress }, null, subject, htmlBody, attachments);
        }

        // HTML message with To and CC lists (used by IVT Administration).
        public static Task SendHtmlAsync(IEnumerable<string> to, IEnumerable<string> cc, string subject, string htmlBody)
        {
            return SendAsync(to, cc, subject, htmlBody, null);
        }

        public static async Task AM_Activity(string Comment, string Activity,string ActionedBy)
        {
            try
            {
               
                IVTEntities Obj = new IVTEntities();
                await Task.Run(() => Obj.sp_New_AM_Activity(Activity, ActionedBy, Comment));
            }
            catch (Exception ex)
            {
                // Handle the exception NA
            }
        }


        public static string GetConnectionString()
        {
            string server = "AFS-SQL01";
            string database = "IVT";
            string username = "report_subscriber";
            string password = "P@ssw0rd";
            return $"Server={server};Database={database};User Id={username};Password={password};";
        }

        public static async Task<SqlConnection> GetSqlConnectionAsync()
        {
            string connectionString = GetConnectionString();
            return new SqlConnection(connectionString);
        }

        public static async Task<string[]> GetAdminContactEmailsAsync()
        {
            string query = "SELECT [ADMIN_Contact] FROM [WebIVT].[IVT_Run_Control]";

            using (SqlConnection connection = await GetSqlConnectionAsync())
            {
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    await connection.OpenAsync();
                    string adminContactEmails = (string)await command.ExecuteScalarAsync();
                    return adminContactEmails.Split(';');
                }
            }
        }

        public static async Task<string[]> GetFinanceContactEmailsAsync()
        {
            string query = "SELECT [Finance_Contact] FROM [WebIVT].[IVT_Run_Control]";

            using (SqlConnection connection = await GetSqlConnectionAsync())
            {
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    await connection.OpenAsync();
                    string adminContactEmails = (string)await command.ExecuteScalarAsync();
                    return adminContactEmails.Split(';');
                }
            }
        }


    }




}
