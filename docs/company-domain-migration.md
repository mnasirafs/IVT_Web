# AFS and EFM user migration

IVT accepts authenticated Windows identities and company UPNs from both
`afslogistics.com.au` and `efmlogistics.com.au`. Users must retain an existing active
IVT account and its permissions. This change does not create accounts or establish
Windows domain trust.

## Mailbox and account mappings

Set `IVT_USER_EMAILS` in Web.config appSettings or the IIS process environment:

```text
jsmith=john.smith@efmlogistics.com.au;anotherlogin=person@efmlogistics.com.au
```

The repository includes John's mapping based on the previous `AFS\jsmith`
identity in the local login log. Confirm that account against production before
deploying. Each entry maps the original IVT LoginName to the current mailbox and
also links that mailbox identity back to the IVT account. Keep the original IVT
LoginName so customer portfolios and audit history remain associated with it.
Only migrate each person's entry when their new mailbox is ready.

If the Windows account name also changes, add its full authenticated identity to
`IVT_USER_ALIASES`, for example (replace with the actual domain and account):

```text
EFM\new.windows.login=originalivtlogin;old.alias@afslogistics.com.au=originalivtlogin
```

Do not guess the new Windows account from an email address. Bare or domain-qualified
accounts with the same account name continue to use the existing IVT login.

Notifications and assistant reports prefer the configured mailbox, then a full
company email identity, then the AD account's `mail` attribute. Without any of
these, existing users retain the original `login@afslogistics.com.au` convention.
Update mappings or the AD mail attribute when staff move. Messages go to the
current mailbox once; the system does not send duplicate copies to old aliases.
Configured admin and finance recipient lists continue to accept their existing
full email addresses, including EFM addresses.

Environment variables override appSettings. Existing server-side appSettings in
`App_Data/ivt-chat.secrets.config` may also override Web.config. Preserve all
mappings when updating these settings and recycle the app pool after changes.

## Authentication infrastructure

IIS Windows authentication is still responsible for verifying sign-in. The IIS
server must be able to authenticate EFM domain accounts (through the applicable
domain trust and IIS access policy). An email suffix in this application alone
cannot establish that access. Cloud-only EFM accounts require a separate identity
integration if they cannot authenticate to this Windows-authenticated server.

The existing password sign-in preserves explicitly supplied `DOMAIN\user` or
`user@company` credentials instead of forcing the AFS prefix. A UPN must be a valid
directory sign-in name; a new SMTP alias alone does not make it a directory UPN.
Bare usernames keep the existing AFS prefix.

`IVT_AUTH_LDAP_PATHS` holds semicolon-separated real LDAP roots for password sign-in.
It defaults to `LDAP://DC=afs,DC=local`. If EFM has a separate AD directory, add its
actual reachable root; no EFM directory address is assumed. Each configured root
is tried until one verifies the submitted credentials. Review the directories'
lockout policies when configuring multiple roots.

## Deployment verification

Run `tests/Chat.Verification/run.ps1` for local checks of both domains, explicit
alias mapping, existing permissions, mailbox routing, and admin impersonation.
These checks do not contact AD, the database, or SMTP.

On the deployed server, verify one current AFS user and John's EFM account:

1. Sign in through IIS and confirm the authenticated Windows/UPN identity.
2. Confirm the original IVT account, role, and customer portfolio are used.
3. Trigger an authorised notification and assistant report and verify arrival at
   the current mailbox, with existing admin and finance copies preserved.
4. Verify an inactive IVT user cannot access assistant data.

Actual EFM domain authentication and mailbox delivery require this server check;
the local build and verification suite cannot establish them.
