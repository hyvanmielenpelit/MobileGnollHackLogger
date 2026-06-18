---
name: MobileGnollHackLogger Pages and Frontend
description: Reference for all Razor Pages, Identity pages, shared layout, styling, static assets, email templates, and external integrations in the MobileGnollHackLogger project. Covers the page models, query parameters, view logic, Bootstrap 5 dark theme, SCSS compilation, DataTables, and how the frontend connects to NetHack Scoreboard, JunetHack, and the GnollHack mobile app.
---

# Razor Pages, Frontend & Integrations

## Solution Structure (Frontend-relevant)

```
MobileGnollHackLogger/
├── Pages/                         # Razor Pages
│   ├── Index.cshtml(.cs)         # Home page
│   ├── TopScores.cshtml(.cs)     # Leaderboard
│   ├── RecentGames.cshtml(.cs)   # Recent games list
│   ├── Statistics.cshtml(.cs)    # Win rate matrix
│   ├── BonesTransactions.cshtml(.cs)  # Bones sharing stats
│   ├── Contact.cshtml(.cs)       # Contact page
│   ├── Privacy.cshtml(.cs)       # Privacy policy
│   ├── Error.cshtml(.cs)         # Error page
│   ├── DeathModel.cs             # Base model for death filtering
│   ├── ModeModel.cs              # Base model for mode filtering
│   ├── Test.cshtml(.cs)          # Test: xlogfile POST
│   ├── TestBones.cshtml(.cs)     # Test: bones POST
│   ├── TestSFTCreate.cshtml(.cs) # Test: SFT create
│   ├── TestSFTUse.cshtml(.cs)    # Test: SFT use
│   ├── _ViewImports.cshtml       # @using, @addTagHelper
│   ├── _ViewStart.cshtml         # Layout = "_Layout"
│   └── Shared/
│       ├── _Layout.cshtml        # Main layout
│       ├── _Layout.cshtml.css    # Scoped layout styles
│       ├── _LoginPartial.cshtml  # Login/logout partial
│       └── _ValidationScriptsPartial.cshtml
├── Areas/Identity/Pages/         # Scaffolded ASP.NET Identity
│   ├── Account/
│   │   ├── Login.cshtml(.cs)
│   │   ├── Register.cshtml(.cs)
│   │   ├── ConfirmEmail.cshtml(.cs)
│   │   ├── ForgotPassword.cshtml(.cs)
│   │   ├── RegisterConfirmation.cshtml(.cs)
│   │   ├── ResendEmailConfirmation.cshtml(.cs)
│   │   └── Manage/
│   │       ├── Index.cshtml(.cs)       # Edit profile (phone)
│   │       ├── JunetHack.cshtml(.cs)   # Edit JunetHack username
│   │       ├── ManageNavPages.cs       # Sidebar navigation
│   │       └── _ManageNav.cshtml       # Sidebar partial
├── Content/                       # Email templates
│   ├── ConfirmAccountEmail.html
│   └── ForgotPasswordEmail.html
└── wwwroot/
    ├── css/
    │   ├── site.css               # Base styles (font sizes, focus)
    │   ├── site2.scss             # SCSS source (dark theme)
    │   ├── site2.css              # Compiled CSS
    │   └── site2.min.css          # Minified CSS
    ├── js/site.js                 # Placeholder (empty)
    ├── img/
    │   ├── gnoll-illustration-darkened-bottom-w1920-q60.jpg  # Background
    │   └── gnoll-illustration-w1920-q30.jpg                  # Alternative
    └── lib/bootstrap/             # Bootstrap 5
```

---

## Shared Layout

File: `Pages/Shared/_Layout.cshtml` (161 lines)

- **Theme:** Dark Bootstrap 5 (`data-bs-theme="dark"`)
- **Google Tag Manager:** Conditional on `GoogleTagManagerID` config key
- **Navigation bar:**
  - Results → Top Scores, Recent Games
  - Statistics → Win Rates, Bones Sharing
  - Links → GnollHack.com, Wiki, GitHub, NetHack Scoreboard, Junethack
  - Contact
- **Footer:** Environment name, copyright (Tommi Gustafsson), privacy link
- **JS libraries:** jQuery, Bootstrap bundle, site.js, DataTables (`jquery.dataTables.min.js`)
- **CSS:** Bootstrap, site.css, site2.min.css
- **Responsive:** Supports `ViewData["ContainerClass"]` and `ViewData["MainClass"]` for per-page width control
- **Login partial:** `_LoginPartial.cshtml` — shows dropdown for authenticated (Profile, Logout) or Register/Login for anonymous

---

## Public Pages

### Index (`/`)
**Model:** `IndexModel : PageModel` — empty `OnGet()`.

- **Authenticated:** "Thank you for registering!" + instructions to configure the GnollHack app (Settings → Server Posting → enable features)
- **Unauthenticated:** "Getting Started" + register link

### TopScores (`/TopScores`)
**Model:** `TopScoresModel : DeathModel`

- **Query params:** `mode` (normal, modern, or omit for all), `death` ("ascended" or omit for all)
- **Data:** Queries `GameLog` where `Scoring == "yes"`, optional `DeathText == "ascended"` and `Mode` filter, ordered by `Points DESC`, top 1000
- **View:** Mode selector (All/Classic/Modern) as URL links, death type selector (Games/Ascensions)
- **DataTable columns:** Rank, Version, Account Name + Char Name, Class/Race/Gender/Alignment, Difficulty/Mode, Points, Turns/Duration, DL/HP, End Time UTC, Deaths/Crashes, Death Reason
- **Row click:** Navigates to `/dumplog/{id}`

### RecentGames (`/RecentGames`)
**Model:** `RecentGamesModel : DeathModel`

- Same filters as TopScores but ordered by `EndTimeUTC DESC`
- Shows subtitle: "Last 1000 of {total} Games"
- Same DataTable layout with adjusted column order (End Time UTC is more prominent)

### Statistics (`/Statistics`)
**Model:** `StatisticsModel : ModeModel`

- **Query param:** `mode` (normal, modern, or omit for all)
- **Filters:** `Scoring == "yes"`, `Mode != "debug"` and `!= "explore"`, `Turns >= 1000`
- **View:** Win rate matrix — Roles (13) × Difficulties (7)
- Each cell shows: games count, ascension count, win rate %
- Totals row and column

### BonesTransactions (`/BonesTransactions`)
**Model:** `BonesTransactionsModel : PageModel` — exposes `DbContext` directly.

- **View:** Tabbed by difficulty level (All + each individual level)
- Each tab: DataTable with Account Name, Bones Uploaded, Bones Downloaded, Bones Shared
- LINQ join between `Users` and `BonesTransactions` done in the view

### Contact (`/Contact`) & Privacy (`/Privacy`)
Static content pages.

---

## Base Page Models

### ModeModel (`Pages/ModeModel.cs`) : PageModel
- `string? Title` — page title
- `string? Mode` — current mode filter
- `string[] DisplayModes` = `["normal", "modern"]` — modes shown in the UI selector

### DeathModel (`Pages/DeathModel.cs`) : ModeModel
- `string? PageName` — page name for URL generation
- `string? Death` — current death filter
- `GetUrl(string? mode, string? death)` — builds URL like `/TopScores?mode=normal&death=ascended`

---

## Identity Pages (Scaffolded)

All under `Areas/Identity/Pages/Account/`.

### Login
- **Input:** UserName (alphanumeric + underscore, max 31), Password, RememberMe checkbox
- **Auth:** `PasswordSignInAsync` by **UserName** (not email)

### Register
- **Input:** UserName (regex `^[A-Za-z0-9][A-Za-z0-9_]{0,30}$`), Email, Password (min 6, max 100), ConfirmPassword
- **Logic:** Checks email uniqueness, creates user, sends confirmation email using `ConfirmAccountEmailHtml` template
- **Requires:** `RequireConfirmedAccount = true`

### ForgotPassword
- **Input:** Email
- **Logic:** Generates password reset token, sends email with `ForgotPasswordEmailHtml` template (includes UserName)

### Manage/JunetHack
- **Input:** JunetHackUserName (max 255, alphanumeric + underscore)
- **Logic:** Updates `ApplicationUser.JunetHackUserName`

### ManageNavPages
Navigation helper. Sidebar items: Profile, Email, Password, External logins (conditional), Personal data, Junethack. Two-factor auth is commented out.

---

## Test Pages

Debug/development tools for manually exercising API endpoints. **Do not remove.**

| Page | Model | Binds To | Tests |
|------|-------|----------|-------|
| `Test.cshtml` | `TestModel` | `LogModel Input` | `POST /xlogfile` |
| `TestBones.cshtml` | `TestBonesModel` | `BonesModel Input` | `POST /bones` |
| `TestSFTCreate.cshtml` | `TestSFTCreateModel` | `SaveFileTrackingCreateModel Input` | `POST /api/SaveFileTracking/create` |
| `TestSFTUse.cshtml` | `TestSFTUseModel` | `SaveFileTrackingUseModel Input` | `POST /api/SaveFileTracking/use` |

All have `[BindProperty]` on their Input model and simple `OnGet()` → `Page()`.

---

## Styling

### SCSS Compilation
- Source: `wwwroot/css/site2.scss` → compiled to `site2.css` via WebCompiler (`compilerconfig.json`)
- Minified: `site2.min.css`

### Theme Variables (from `site2.scss`)
- `$font-color: white`
- `$link-color: #ded18b`
- `$primary-color: gold + 10%`
- `$nav-color` — navbar color
- Background image: `gnoll-illustration-darkened-bottom-w1920-q60.jpg`

### Key CSS Features
- Dark theme throughout
- DataTable rows have `cursor: pointer` (clickable to dump log)
- Responsive max-widths at media breakpoints: 1000px, 1500px, 1800px, 2200px
- Custom width utility classes

### Base Styles (`site.css`)
- Font size: 14px mobile, 16px desktop
- Focus outline styles
- Relative positioning defaults

---

## Email Templates

Files: `Content/ConfirmAccountEmail.html`, `Content/ForgotPasswordEmail.html`

**ConfirmAccountEmail.html:**
```html
<h1>Confirm GnollHack Account Email</h1>
<p>Please confirm your GnollHack account by <a href='{CallbackUrl}'>clicking here</a>.</p>
```

**ForgotPasswordEmail.html:**
```html
<h1>GnollHack Account Password and User Name Recovery</h1>
<p>Your GnollHack Account user name is: <b>{UserName}</b></p>
<p>Please reset your GnollHack Account password by <a href='{CallbackUrl}'>clicking here</a>.</p>
```

Placeholders are replaced by the Identity scaffolding code before sending via `EmailSender`.

