# Fantasy Keeper App

Friendly keeper-submission UI for the Worm Burners Dynasty League, backed
by the league's live Google Sheet. See
`docs/superpowers/specs/2026-08-21-fantasy-keeper-app-design.md` in the
repo root for the full design.

## Running locally (no Google account needed)

By default the backend runs against in-memory fake Sheets/Drive clients
seeded with data shaped like the real `2026 Keepers` tab — no Google
Cloud setup required to develop or demo the app.

```bash
dotnet run --project backend/FantasyKeeper.Api
```

The app listens on `http://localhost:5080`. Try:

```bash
curl "http://localhost:5080/api/keepers?pin=1111"
```

Seeded PINs: team PIN `1111` (B Squared), admin PIN `0000`.

## Going live against your real Google Sheet

1. In Google Cloud Console, create a project and enable the **Google
   Sheets API** and **Google Drive API**.
2. Create a service account, then create and download a JSON key for it.
   Save it as `fantasy-keeper-app/secrets/service-account.json` (this
   path is gitignored).
3. Open your league's Google Sheet, click Share, and add the service
   account's email address (from the key file) as an **Editor**.
4. Create `backend/FantasyKeeper.Api/appsettings.Development.json`
   (gitignored) with:

```json
{
  "Google": {
    "UseDevClients": false,
    "ServiceAccountKeyPath": "../../secrets/service-account.json",
    "CommissionerEmail": "you@example.com"
  },
  "AdminPin": "<pick a real admin PIN>"
}
```

5. Update `config/seasons.json` with the real Sheet's file ID (from its
   URL) and `config/team-mappings/season-1.json` with each team's actual
   cell ranges in the sheet.
6. Update `config/teams.json` with real team PINs.
