using System;
using System.Configuration;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Net.Mail;
using System.Security.Principal;
using System.Web;

namespace AFSDataGateway.Services
{
    // Keep the existing IVT LoginName (and portfolio) when a mailbox or Windows login changes.
    public static class UserIdentity
    {
        private static string Setting(string key)
        {
            return Environment.GetEnvironmentVariable(key) ?? ConfigurationManager.AppSettings[key] ?? "";
        }

        private static string[][] Entries(string key)
        {
            return Setting(key).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split(new[] { '=' }, 2)).Select(x =>
                {
                    if (x.Length != 2 || x.Any(string.IsNullOrWhiteSpace))
                        throw new ConfigurationErrorsException("Invalid entry in " + key);
                    return x.Select(v => v.Trim()).ToArray();
                }).ToArray();
        }

        public static string NormalizeLogin(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity)) return null;
            var name = identity.Trim();
            if (name.Contains("@") && !IsCompanyEmail(name)) return null;
            // Email mappings also link the migrated mailbox to its original IVT account.
            var mapped = Entries("IVT_USER_EMAILS").Where(x => x[1].Equals(name, StringComparison.OrdinalIgnoreCase))
                .Select(x => x[0]).Concat(Entries("IVT_USER_ALIASES")
                    .Where(x => x[0].Equals(name, StringComparison.OrdinalIgnoreCase)).Select(x => x[1]))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (mapped.Length > 1) throw new ConfigurationErrorsException("Ambiguous IVT identity mapping.");
            if (mapped.Length == 1) return mapped[0].ToUpperInvariant();
            name = name.Split('\\').Last();
            if (name.Contains("@"))
            {
                if (!IsCompanyEmail(name)) return null;
                name = name.Split('@')[0];
            }
            return name.ToUpperInvariant();
        }

        public static string CurrentLogin()
        {
            var principal = HttpContext.Current == null ? null : HttpContext.Current.User;
            var authenticated = principal != null && principal.Identity != null && principal.Identity.IsAuthenticated;
            // On Azure the process identity is the app pool, never a person: no fallback when Microsoft sign-in is required.
            if (!authenticated && AFSDataGateway.Services.Entra.EntraAuth.Required) return null;
            var identity = authenticated ? principal.Identity.Name : WindowsIdentity.GetCurrent().Name;
            return NormalizeLogin(identity);
        }

        // altfs.com.au is the AFS tenant's primary domain; afslogistics.com.au is its alias.
        public static string[] CompanyDomains
        {
            get
            {
                var value = Setting("IVT_COMPANY_DOMAINS");
                if (string.IsNullOrWhiteSpace(value)) value = "afslogistics.com.au;altfs.com.au;efmlogistics.com.au";
                return value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            }
        }

        public static bool IsCompanyEmail(string value)
        {
            try
            {
                var mail = new MailAddress(value);
                return mail.Address.Equals(value, StringComparison.OrdinalIgnoreCase) &&
                    CompanyDomains.Contains(mail.Host, StringComparer.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (FormatException) { return false; }
        }

        public static string EmailFor(string identity)
        {
            var login = NormalizeLogin(identity);
            if (string.IsNullOrEmpty(login)) throw new InvalidOperationException("No IVT recipient identity.");
            var configured = Entries("IVT_USER_EMAILS").Where(x => x[0].Equals(login, StringComparison.OrdinalIgnoreCase))
                .Select(x => x[1]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (configured.Length > 1) throw new ConfigurationErrorsException("Ambiguous IVT mailbox mapping.");
            if (configured.Length == 1)
            {
                if (!IsCompanyEmail(configured[0])) throw new ConfigurationErrorsException("IVT mailbox must use an approved company domain.");
                return configured[0];
            }
            if (IsCompanyEmail(identity)) return identity.Trim();
            // AD's mail attribute handles mailboxes whose names differ from their Windows login.
            try
            {
                using (var context = new PrincipalContext(ContextType.Domain))
                using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, login))
                    if (user != null && IsCompanyEmail(user.EmailAddress)) return user.EmailAddress;
            }
            catch (PrincipalException) { /* Preserve the existing AFS convention if AD is unavailable. */ }
            return login.ToLowerInvariant() + "@afslogistics.com.au";
        }

        public static string[] LdapPaths
        {
            get
            {
                var paths = Setting("IVT_AUTH_LDAP_PATHS");
                return string.IsNullOrWhiteSpace(paths) ? new[] { "LDAP://DC=afs,DC=local" } :
                    paths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            }
        }
    }
}
