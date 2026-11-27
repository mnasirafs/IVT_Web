# IVT 2.0 – Invoice Vetting Tool

ASP.NET MVC 5 (.NET Framework) web app, with an optional Python ML module in `ivt-ml/`.

## Local setup

1. Open `IVT.sln` in Visual Studio and restore NuGet packages.
2. Configuration secrets are **not** in this repository. Before running locally:
   - In `AFSDataGateway/Web.config`, replace `YOUR_SQL_SERVER`, `YOUR_DB_USER` and `YOUR_DB_PASSWORD`
     in the `AFSXchange1Entities` and `IVTEntities` connection strings with credentials you have been given.
     Do not commit them.
   - Settings marked `SET_IN_APP_SERVICE` / `SET_ON_SERVER` are supplied by the hosting environment
     (Azure App Service settings) and can stay as they are for most local work.
3. Microsoft sign-in (`IVT_ENTRA_*` settings) is off unless `IVT_ENTRA_ENABLED=true` and a client secret is set.

## Rules for contributors

- Never commit credentials, connection strings, `.env` / `local.settings.json`, publish profiles (`*.pubxml.user`),
  logs or build output. `.gitignore` covers these; GitHub push protection will block detected secrets.
- Do not commit data extracts or ML outputs (`ivt-ml/output/`).
