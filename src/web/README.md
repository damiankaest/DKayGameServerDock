# DKayGameServerDock web client

Angular client for the DKayGameServerDock REST and SignalR API.

```powershell
npm ci
npm start
```

The development server runs at `http://localhost:4200` and proxies `/api`, `/hubs` and `/health` to the ASP.NET Core API on port 5080.

```powershell
npm test -- --watch=false
npm run build
```

The production output is written to `dist/web/browser`. The repository-level Windows publish script copies it into the API before `dotnet publish`.
