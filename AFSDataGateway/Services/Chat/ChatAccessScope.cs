using AFSDataGateway.Models;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Security.Principal;

namespace AFSDataGateway.Services.Chat
{
    public sealed class ChatAccessScope
    {
        public string Actor { get; private set; }
        public string ActorIdentity { get; private set; }
        public string LoginName { get; private set; }
        public bool IsAdmin { get; private set; }
        public string Fingerprint { get; private set; }
        public string Label { get { return IsAdmin ? "All Account Managers" : "Account Manager: " + LoginName; } }

        private ChatAccessScope() { }

        // Identity comes exclusively from the authenticated principal. Session role/name values
        // and the application's process-wide impersonation flag are deliberately not authorities.
        public static ChatAccessScope Resolve(IPrincipal principal, string impersonatedLogin, IQueryable<vIVTUser> users)
        {
            if (principal == null || principal.Identity == null || !principal.Identity.IsAuthenticated)
                throw new ChatAccessException();

            var identity = principal.Identity as ClaimsIdentity;
            var name = identity == null ? principal.Identity.Name :
                identity.FindFirst("preferred_username")?.Value ?? identity.FindFirst(ClaimTypes.Email)?.Value ?? identity.Name;
            var login = NormalizeIdentity(name);
            if (string.IsNullOrEmpty(login)) throw new ChatAccessException();
            var actor = Single(users, login);
            ValidateUser(actor);
            var target = actor;
            bool narrowed = false;
            if (!string.IsNullOrWhiteSpace(impersonatedLogin))
            {
                var selected = NormalizeIdentity(impersonatedLogin);
                if (selected != login)
                {
                    if (!IsAdministrator(actor.UserType)) throw new ChatAccessException();
                    target = Single(users, selected);
                    ValidateUser(target);
                    // Impersonation always narrows to the selected user's own portfolio (never admin scope),
                    // even when that user is itself an administrator.
                    narrowed = true;
                }
            }
            return new ChatAccessScope
            {
                Actor = login,
                ActorIdentity = name,
                LoginName = NormalizeIdentity(target.LoginName),
                IsAdmin = IsAdministrator(actor.UserType) && !narrowed,
                Fingerprint = actor.Id + ":" + actor.UserType + ":" + login + ":" + target.Id + ":" + target.UserType + ":" + NormalizeIdentity(target.LoginName)
            };
        }

        public ChatAccessScope ForAccountManager(string requestedLogin)
        {
            if (string.IsNullOrWhiteSpace(requestedLogin)) return this;
            var requested = requestedLogin.Trim().ToUpperInvariant();
            if (!IsAdmin && requested != LoginName) throw new ChatAccessException();
            return new ChatAccessScope { Actor = Actor, ActorIdentity = ActorIdentity, LoginName = requested, IsAdmin = false, Fingerprint = Fingerprint };
        }

        public IQueryable<T> Apply<T>(IQueryable<T> query, Expression<Func<T, string>> loginSelector)
        {
            if (IsAdmin) return query;
            // Plain equality, like the IVT screens: the IVT database collation is case-insensitive and ignores trailing
            // spaces, and wrapping the column in UPPER/TRIM would stop SQL Server using indexes on large views.
            var value = loginSelector.Body;
            var predicate = Expression.AndAlso(Expression.NotEqual(value, Expression.Constant(null, typeof(string))),
                Expression.Equal(value, Expression.Constant(LoginName)));
            return query.Where(Expression.Lambda<Func<T, bool>>(predicate, loginSelector.Parameters));
        }

        // SQL form of Apply for the dataset engine: same rule, with the login passed as a parameter.
        public string SqlFilter(string quotedColumn, Func<string, string> parameter)
        {
            if (IsAdmin) return "(1 = 1)";
            return "(" + quotedColumn + " IS NOT NULL AND " + quotedColumn + " = " + parameter(LoginName) + ")";
        }

        // One user per login. Duplicate rows are accepted only when they are equivalent (same role, active, not deleted);
        // otherwise the identity is ambiguous and access is refused.
        private static vIVTUser Single(IQueryable<vIVTUser> users, string login)
        {
            var matches = users.Where(x => x.LoginName != null && x.LoginName.Trim().ToUpper() == login).Take(10).ToList();
            if (matches.Count == 0) throw new ChatAccessException();
            if (matches.Select(m => new { m.UserType, m.IsActive, m.IsDeleted }).Distinct().Count() != 1) throw new ChatAccessException();
            return matches.OrderBy(m => m.Id).First();
        }

        private static void ValidateUser(vIVTUser user)
        {
            if (user.IsActive != 1 || user.IsDeleted != 0 || (user.UserType != 0 && !IsAdministrator(user.UserType)))
                throw new ChatAccessException();
        }

        private static bool IsAdministrator(int role) { return role == 1 || role == 2; }

        private static string NormalizeIdentity(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            // Matches the existing IVT login mapping for Windows and Entra identities.
            return UserIdentity.NormalizeLogin(name);
        }
    }

    public sealed class ChatAccessException : Exception
    {
        public ChatAccessException() : base("This request is outside your permitted account scope.") { }
    }
}
