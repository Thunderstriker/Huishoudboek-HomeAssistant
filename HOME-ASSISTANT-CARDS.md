# Home Assistant cards — guide and development handoff

This document records the goal, implemented functionality, design decisions, local
preview, installation, security boundaries, and possible next cards. Read it alongside
`AGENTS.md` before continuing this work in another session. The source code is the
authority if this guide and the implementation diverge.

## 1. Goal and current scope

Expose selected Huishoudboek components as independently placeable Home Assistant
dashboard cards. Users should be able to create a dedicated **Financiën** dashboard,
or put one or two financial cards alongside their existing household cards, without
embedding the entire application with its navigation and toolbar.

Implemented cards (all independently placeable):

| Widget value | Card | Contents |
| --- | --- | --- |
| `month-overview` | Monthly overview | Month result after savings, income, expenses including housing, and net savings contributions |
| `spending` | Where does my money go? | Spending ring, the four largest positive spending categories, other categories grouped together, amounts and percentages |
| `freedom-map` | Compact financial freedom map | Essential months covered, buffer target, spending room, income commitments, and lifestyle coverage |
| `budget-watch` | Budget watch | Four categories with highest budget overspend/utilization, progress and remaining amounts |
| `upcoming` | Upcoming payments / next income | Known next-seven-day payments, next income dates and explicit planning uncertainty |
| `data-status` | Data / attention | Stale or missing bank data, uncategorized counts and existing prioritized dashboard checks |
| `category-trend` | Selected category trend | 6/12/24 completed months, net-spending bars, moving average, coverage warnings and exact values |
| `import-status` | Current account imports | Last active import per current account, weekly cadence colours and separate bank-data freshness |

These are real application calculations and visuals, not sample graphs created by
the card frontend. Cards also show their load time, bank-data freshness, owner context,
and relevant warnings. Native disclosure elements preserve explanations.

Also implemented:

- A custom `huishoudboek-card` frontend resource and authenticated ingress handling.
- Shared ingress metadata/session work across cards on the same HA connection/user/app.
- Session validation/replacement, load deadlines, connection/error handling, and refresh.
- Content-driven height, including changes when explanations are expanded.
- Light/dark themes, Dutch/English, fixed/current month and optional owner filters.
- A realistic **localhost-only gallery** with desktop/tablet/phone layouts and example YAML.
- A download link and installation help under **Beheer & import**.

**Release record:** these changes are listed under `0.2.1` in
`huishoudboek/CHANGELOG.md`. The user initially chose **release notes only** for this
work, leaving the application version at `0.2.0`. A subsequent explicit request bumps
both application version fields to **`0.2.2`**, which includes these cards and the new
receipt/warranty feature. Do not bump them again without a new request.
The five additional cards also belong to `0.2.1`, explicitly selected by the user.
The new resource cache key is `0.2.1-cards2`; this is not an application version bump.

## 2. Try the realistic local gallery

Requirements: .NET 10 SDK and the existing local preview launcher.

From the repository in PowerShell:

```powershell
.\preview.ps1
```

Open:

**http://127.0.0.1:5099/kaarten/preview**

For a one-time build without file watching:

```powershell
.\preview.ps1 -NoWatch
```

For another port:

```powershell
.\preview.ps1 -Port 5100
```

Then use `http://127.0.0.1:5100/kaarten/preview`. The launcher prints both the main
app URL and the card gallery URL. The gallery is also linked from the local app's
**Beheer & import → Home Assistant-dashboardkaarten** section.

### What to try

1. Switch between light/dark and Dutch/English.
2. Select desktop (maximum 1280 px), tablet (820 px), or phone (390 px).
   A smaller actual browser window limits these widths; it does not force horizontal scrolling.
3. Switch between following the current month and a fixed month.
4. Choose an owner; shared accounts keep the app's existing whole-account semantics.
5. Expand an explanation and watch the card height adjust.
6. Refresh one card, or use the gallery's refresh button for all cards.
7. Expand the YAML examples. Replace the sample installed app ID before using them in HA.
8. Choose a trend category and 6/12/24 months; check coverage warnings and expand exact values.
9. Inspect import cadence separately from bank-data freshness; expand the colour legend.

Appearance controls update immediately. Period/owner changes load new snapshots;
changing language reloads the gallery. The URL preserves gallery settings, so a
local bookmark can reproduce a chosen layout. A deleted/invalid selected owner must
fail, not silently reveal the whole household. Owner UUIDs in this checkout's preview
are not necessarily those in the Home Assistant administration.

### What the gallery does and does not simulate

The gallery displays all eight cards. It subclasses the production card element and reuses configuration,
frame rendering, resizing, theme handling, refresh controls, and lifecycle cleanup.
The subclass replaces only loading/session maintenance with direct local widget
requests. A small `ha-card` surface and responsive dashboard grid simulate the
surrounding Home Assistant UI.

It does **not** run Home Assistant, verify HA sign-in, establish real ingress sessions,
or guarantee identical layout in every HA theme, Sections/Masonry view, browser, or
companion app. Those require a real HA installation. There is no `preview: true`
production card option or production authentication fallback.

### Local data and safety

- The first launch creates fictional data. Later launches retain the same checkout's
  preview administration, which may contain real financial data imported by the user.
- The launcher stores it outside Git under
  `%LOCALAPPDATA%\Huishoudboek\Preview\<checkout-id>`; separate checkouts/worktrees
  have separate preview directories.
- Gallery controls and card GET requests do not change financial data. Editing/importing
  through the full local app still changes that local administration as before.
- Nothing is synchronized with Home Assistant or a bank.
- The server checks both loopback peer and local Host. Do not publish, reverse-proxy,
  or tunnel this password-free preview.
- Only widget endpoints allow **same-origin** framing in preview mode. The gallery,
  full app and other preview pages retain `frame-ancestors 'none'`.

### Direct widget pages

These are also useful for styling and inspecting the rendered HTML:

- `/kaarten/month-overview`
- `/kaarten/spending`
- `/kaarten/freedom-map`
- `/kaarten/budget-watch`
- `/kaarten/upcoming`
- `/kaarten/data-status`
- `/kaarten/import-status`
- `/kaarten/category-trend?category=<UUID>&history_months=12`

For example:

`http://127.0.0.1:5099/kaarten/spending?theme=dark&language=en&period=2026-10`

Direct widget pages accept `theme=light|dark`, `language=nl|en`,
`period=current|YYYY-MM`, and an optional `owner=<UUID>`.
Category trend additionally requires `category=<UUID>` (all-zero UUID means
uncategorized) and accepts `history_months=6|12|24` (default 12).

## 3. Install on actual Home Assistant

Use Home Assistant OS with Supervisor, **Core 2026.11 or newer**, and a trusted
administrator account. These cards are included in Huishoudboek app release **0.2.2**.
Updating the app does not replace the separately copied dashboard JavaScript resource;
download that file again when upgrading from the initial three-card implementation.

1. In Huishoudboek, open **Beheer & import → Home Assistant-dashboardkaarten**.
2. Download `huishoudboek-card.js` and save it as
   `/config/www/huishoudboek-card.js` in Home Assistant.
3. Add a dashboard resource with type **JavaScript module**:
   `/local/huishoudboek-card.js?v=0.2.1-cards2`.
4. Find the full installed app slug, including the repository ID, for example
   `abcdef12_huishoudboek`. A local add-on installation uses `local_huishoudboek`.
5. Add a Manual card:

```yaml
type: custom:huishoudboek-card
addon: abcdef12_huishoudboek # replace with your actual installed app ID
widget: month-overview
```

Use any widget value from the table above. Category trend requires a category UUID
from **Beheer & import → Home Assistant-dashboardkaarten → Categorie-id's**; for example:

```yaml
type: custom:huishoudboek-card
addon: abcdef12_huishoudboek
widget: category-trend
category: "00000000-0000-0000-0000-000000000000" # replace; all-zero = uncategorized
history_months: 12
```

The Dutch installation guide and a complete three-section dashboard example are in
[`huishoudboek/DOCS.md`](huishoudboek/DOCS.md#losse-home-assistant-dashboardkaarten-proefversie).

### Supported card options

| Option | Values | Default |
| --- | --- | --- |
| `addon` | Full installed app slug | required |
| `widget` | One of the eight values above | required |
| `period` | `current` or quoted `"YYYY-MM"`, years 1900–2200 | `current` |
| `owner` | Owner UUID shown in Administration | whole household when omitted |
| `theme` | `auto`, `light`, `dark` | `auto`, following HA |
| `language` | `auto`, `nl`, `en` | `auto`; non-Dutch HA languages use English |
| `refresh_interval` | Integer seconds, 60–3600 | `300` |
| `category` | Normal category UUID; all-zero UUID = uncategorized | required for `category-trend` |
| `history_months` | Numeric `6`, `12`, `24` | `12`; category trend only |

The current month follows the app's time zone. The freedom map always uses today's
balances and its historical analysis period, independent of `period`. Fixed months
and owners are display filters, not permissions.

Upcoming payments and import status also use today regardless of `period`.
Category trend ends in the last completed month when following current (or selecting
the current month explicitly); an earlier fixed month becomes the final month.
Future trend months and windows extending before 1900 are rejected. An invalid/deleted
category fails, never silently substitutes another category. The gallery preserves
category/history in its URL and provides YAML for all eight cards.
Data status combines today's freshness/planning and uncategorized counts through
today with budget/month-result checks for the selected month; it is not an all-clear.

### Current-account import cadence

`import-status` considers only current accounts already open today within the owner
scope. It ignores undone batches and shows the latest remaining import timestamp
per account. The oldest or missing/invalid import determines the headline; a new
upload for one account cannot hide a stale account. No current accounts is explanatory,
not green. Text labels accompany every colour:

| Calendar days since import | Colour |
| --- | --- |
| 0–7 | Green `#23734c` |
| 8–14 | Yellow-green `#b6cc65` |
| 15–21 | Amber `#efbd54` |
| 22–30 | Red-orange `#bc4e32` |
| More than 30 | Dark red `#730e25` |
| Missing/future import date | Neutral grey `#56636d`, unknown |

The target is one import per week per current account. A recent upload of old data
can be green for **upload cadence** while a separate visible warning says its
**bank data is stale/unknown**. Neither timestamp proves complete statement coverage.
Refresh only reloads the card; it does not fetch transactions from a bank.

Cards refresh periodically and on return from the background; their refresh button
loads a new snapshot immediately. Background pages do not poll. Links open the full
app in a new tab; that app keeps its own month/owner filters.

### Distribution and upgrade limitations

- No HACS package, Python integration, visual card editor, or auto-discovery is included.
- The app download and the copied `/config/www` file are separate deployments.
  Recopy the JavaScript when it changes, update the resource's `?v=` and reload HA's frontend.
- Do not register the resource itself through an ingress URL: it would require an
  ingress session before the code responsible for creating that session could load.
- The gallery is not exposed as a real HA widget.

## 4. Architecture and file map

Production request flow:

```text
Lovelace custom:huishoudboek-card
  -> HA authenticated WebSocket: app info / ingress session / validation
  -> same-origin iframe under /api/hassio_ingress/<token>/kaarten/<widget>
  -> authorized RazorComponentResult<CardDocument>
  -> existing Core calculations + pure shared Razor components
  -> static HTML/CSS (no Blazor circuit per card)
```

The wrapper uses the `supervisor/ingress/info`, `supervisor/ingress/session`, and
`supervisor/ingress/validate_session` commands. It validates the returned ingress
origin/path, uses HA's ingress cookie convention, and never requires a Huishoudboek
editor/reader token in YAML. Its session cache is scoped by HA connection, user and app.
Late responses from removed/previous-user cards must not overwrite the ingress cookie.

Relevant files, relative to the repository root:

| File | Responsibility |
| --- | --- |
| `huishoudboek/source/src/Huishoudboek.Service/DashboardCards.cs` | Authorized widget routes, validation, preview-only gallery route and frame metadata |
| `huishoudboek/source/src/Huishoudboek.Service/HomeAssistantHosting.cs` | Existing trusted ingress proxy and user identity handling |
| `huishoudboek/source/src/Huishoudboek.Service/ServiceApplication.cs` | Middleware, CSP, endpoint registration, existing API block |
| `huishoudboek/source/src/Huishoudboek.Service/LocalPreviewAuthentication.cs` | Explicit local preview authentication and peer/Host checks |
| `huishoudboek/source/src/Huishoudboek.Service/WebsiteLanguage.cs` | Widget-specific explicit language without changing the app's language cookie |
| `huishoudboek/source/src/Huishoudboek.Web/DashboardCardModel.cs` | Transient read-only render input |
| `huishoudboek/source/src/Huishoudboek.Web/Components/Widgets/CardDocument.razor` | Minimal widget document and context/freshness warnings |
| `huishoudboek/source/src/Huishoudboek.Web/Components/Widgets/CardGallery.razor` | Local gallery document, controls and preview owner list |
| `huishoudboek/source/src/Huishoudboek.Web/wwwroot/huishoudboek-card.js` | Production custom element, ingress, lifecycle, resizing and themes |
| `huishoudboek/source/src/Huishoudboek.Web/wwwroot/card-gallery.js` | Loopback-only preview subclass/adapter and gallery controls |
| `huishoudboek/source/src/Huishoudboek.Web/wwwroot/dashboard-cards.css` | Widget presentation and light/dark overrides |
| `huishoudboek/source/src/Huishoudboek.Web/wwwroot/card-gallery.css` | Preview dashboard shell and responsive layouts |
| `huishoudboek/source/src/Huishoudboek.Web/Components/Shared/DashboardMonth.razor` | Monthly snapshot reused by the full dashboard and widget |
| `huishoudboek/source/src/Huishoudboek.Web/Components/Shared/DashboardSpending.razor` | Pure spending ring/category component |
| `huishoudboek/source/src/Huishoudboek.Web/Components/Shared/FreedomMap.razor` | Full/compact freedom visualization |
| `huishoudboek/source/src/Huishoudboek.Web/HomeOverview.cs` | Existing dashboard reports, account freshness and scope |
| `huishoudboek/source/src/Huishoudboek.Web/HomeAttention.cs` | Shared prioritized dashboard/status checks |
| `huishoudboek/source/src/Huishoudboek.Web/CurrentAccountImports.cs` | Current-account upload age bands and oldest/unknown-first ordering |
| `huishoudboek/source/src/Huishoudboek.Web/Components/Shared/DashboardBudgetWatch.razor` | Pure budget view reused by dashboard and widget |
| `huishoudboek/source/src/Huishoudboek.Web/Components/Shared/DashboardUpcoming.razor` | Pure payment/income view reused by dashboard and widget |
| `huishoudboek/source/src/Huishoudboek.Web/Components/Shared/DashboardDataStatus.razor` | Read-only attention/status view |
| `huishoudboek/source/src/Huishoudboek.Web/Components/Shared/DashboardImportStatus.razor` | Weekly import cadence, per-account dates and separate freshness warnings |
| `huishoudboek/source/src/Huishoudboek.Web/Components/Shared/DashboardCategoryTrend.razor` | Static SVG trend and accessible exact-value table, using `CategoryHistory` |
| `huishoudboek/source/src/Huishoudboek.Core/FinancialInsights.cs` | Existing financial freedom/fingerprint calculations and history rules |
| `huishoudboek/source/src/Huishoudboek.Web/UiResources*.resx` | Dutch/English application and gallery strings |

`DashboardMonth` and its scoped CSS were extracted from `Pages/Home.razor` so the full
dashboard and card do not drift. `DashboardSpending` now uses its report's category
names rather than depending on editable/circuit-scoped `WebState`.
Budget and upcoming-payment sections were also extracted from `Home.razor`; their
scoped CSS travels with the shared components. Category trend deliberately uses
static SVG instead of Chart.js, retaining the script-free iframe sandbox and no
Blazor circuit. It reuses existing net amounts, coverage and moving-average results;
negative values stay below zero, and unconfirmed coverage is visible and hatched.

The gallery adapter overrides the production element's `_load` and `_heartbeat`.
If those internal methods or `_renderFrame` change, review the adapter for parity
in cancellation, error clearing, timing and cleanup. Do not move preview transport
into normal card configuration or relax ingress checks to make local viewing work.

## 5. Security, privacy and financial meaning

- Production stays ingress-only: no new exposed port, MQTT, cloud, or machine API.
  The existing `/api` endpoints remain blocked in Home Assistant mode.
- The app has `panel_admin: true`; the card also requires an HA administrator.
  Retain this default unless a separately designed authorization change is requested.
- Display-only cards are **not a read-only role**. Trusted users retain full app
  access. Neither hiding a dashboard nor an owner filter is a security boundary.
- Do not use this as a guest/unsecured wall-tablet solution without separate access design.
- Responses use `no-store`. There is no financial data in localStorage, no raw
  transaction database serialized to the card, and no new HA Recorder history/entities.
- Failed refreshes, known connection loss and denied access clear financial frames.
  Previously rendered data cannot be remotely retracted before the browser discovers
  a session/access change. The card is not a continuous authorization monitor.
- The iframe permits same-origin access for sizing and new-tab drill-downs, but not
  scripts, forms, or top-level navigation. Its contents are static HTML/CSS.
- Never turn unknown/insufficient history into zero, perfect scores, or an all-clear.
  Bank data date and snapshot load time are different concepts.
- Month result after savings is not account balance or safely spendable money.
  Savings means net contributions, not investment return or the balance of savings accounts.
- The spending ring includes positive category totals only. Net refunds/recoveries
  can still affect the monthly result; preserve the existing explanation.
- Freedom/insight estimates need confirmed complete historical coverage. At least
  three covered months support estimates; behavioral discoveries have stricter
  rules, including six consecutive covered months. Investments are not liquid reserves.
- A freedom ring reaching its target is not a claim of financial independence.
  Each ring has a separate meaning, not a shared euro breakdown or overall score.

## 6. Remaining possible cards — not implemented or automatically authorized

The four formerly high-priority candidates (budget watch, upcoming payments, data
status and category trend), plus current-account import status, are now implemented
as described above. The remaining candidates are not approved merely by listing them.

| Candidate | Priority/use | Existing starting point | Work/constraints |
| --- | --- | --- | --- |
| Selected account balance | Optional: one account or chosen set | `DashboardAccounts.razor` | Use per-card selection, not full-app browser preferences; show import/valuation dates; sensitive balances |
| Money fingerprint | Medium: dedicated finance page | `MoneyFingerprint.razor` | Existing legend uses Blazor click handlers; provide local interaction or an appropriate static compact variant |
| Annual income vs expenses | Medium: longer-term overview | First chart in `YearCharts.razor` | Split grouped component and chart initialization; preserve actual-only/incomplete-year semantics |
| Savings rhythm | Medium: savings habit and contributions | Savings chart in `YearCharts.razor` | Monthly/cumulative contributions; negative values below zero; never label as account value or return |
| Category year comparison | Later: seasonal comparison | Comparison chart in `CategoryHistoryCharts.razor` | Needs sufficient history; preserve nonpositive-base comparison rules |
| Category heatmap | Later: broad seasonal patterns | Heatmap in `CategoryHistoryCharts.razor` | Accessible exact values; missing coverage and no entries must remain distinct |
| Spendable until next income | Useful but needs strong context | `SafeSpending.razor`, `CashFlow.cs` | Extract read-only amount + uncertainty; do not embed the buffer editor or claim guaranteed affordability |
| Month-end forecast | Optional planning card | `MonthlyCharts.razor`, monthly reporting | Clearly distinguish forecast from actuals and explain uncertainty; no implied savings forecast beyond existing logic |
| Savings goal progress | Optional: progress toward a named goal | `SavingsOverview.razor`, account savings-goal data | Distinguish category contribution targets from an account's actual balance/goal; make the chosen meaning explicit |
| Recorded net worth | Optional/default-off: private finance page | Monthly overview totals and account reporting | Distinguish cash, savings, investments and home/mortgage treatment; disclose valuation dates and avoid a real-time market-value claim |
| Mortgage summary | Later: remaining principal or repayment progress | `Pages/MortgagePage.razor`, mortgage calculations | Reuse existing assumptions and recorded principal payments; avoid placing a full amortization editor in a card |
| Recent transactions | Low/default-off: quick review | Recent section in `Pages/Home.razor` | Exposes descriptions and account names; less suitable for shared displays |
| Insight discoveries | Low: exploratory finance page | `Pages/Insights.razor` | Keep evidence/context in full app; avoid source-transaction disclosure by default |

Smaller insight cards are another possibility: essential months covered, usable
reserve versus target, spending room, fixed commitments as a share of income, or
one of the fingerprint's six dimensions. Reuse Core results and explanatory text;
do not invent new calculations, infer intentions such as impulsivity, or present
a larger metric as automatically better. The full freedom/fingerprint visuals
remain useful companions on a dedicated finance page.

### Suggested dedicated dashboard

For the implemented expanded set:

```text
Monthly overview            Upcoming payments / next income
Where does my money go?     Budget watch
Financial freedom          Selected category trend
Current account imports    Data / attention
```

For a general household landing page, use only monthly overview and upcoming payments.
Fingerprint and longer-term graphs fit better on the finance page. None of the
remaining candidate widgets exist merely because they appear in this document.

### Visual cards versus HA entities

The current choice is to reuse visual widgets over ingress, avoiding duplicate
calculation/presentation logic and additional permissions. Native sensor entities
would be a separate feature for automations, alerts, or standard entity/history cards.

Possible future scalar entities include current-month income/outflow, budget remaining,
estimated spending room, or lowest projected balance. That would need a deliberately
authenticated data connection or integration: do not simply enable the desktop/editor
API. Plan privacy, owner scope, unknown/unavailable states, update cadence, Recorder
retention, and permission boundaries before choosing integration/API/MQTT transport.

## 7. Validation checklist

Build:

```powershell
dotnet build huishoudboek/source/src/Huishoudboek.Service/Huishoudboek.Service.csproj
node --check huishoudboek/source/src/Huishoudboek.Web/wwwroot/huishoudboek-card.js
node --check huishoudboek/source/src/Huishoudboek.Web/wwwroot/card-gallery.js
```

Also publish with `dotnet publish` and verify the RCL CSS/JavaScript are included.
Do not introduce a new test dependency/framework without considering the repository's
intentional removal of the earlier automated test infrastructure.

Local checks:

- Eight cards load in the gallery without HA or app tokens; no ingress requests/cookies or child scripts.
- Desktop/tablet/phone, actual narrow viewport, both themes/languages, no horizontal overflow.
- Expand/collapse explanations; heights adjust and no previous iframe sizes leak after filters change.
- Owner/month filtering, fixed/current period, and freedom's independent historical period.
- Reload the gallery URL and retain settings; unknown/deleted owners fail closed.
- Invalid settings clear prior data. Empty accounts/insufficient confirmed history remain explanatory.
- Gallery changes and widget GETs do not modify the administration revision.
- Gallery/full app reject framing; widget pages allow only same-origin framing.
- Nonlocal Host/peer remains rejected. Gallery route is absent outside explicit preview mode.
- Full dashboard still shows the extracted monthly component correctly.
- Full dashboard also retains extracted budget and upcoming-payment views and shared attention ordering.
- Import ages: 0/7/8/14/15/21/22/30/31 days, absent/future timestamps, undone imports,
  old statements uploaded today, multiple current accounts and no current accounts.
- Trend: 6/12/24 completed months, earlier fixed month, future/before-1900 rejection,
  invalid/deleted category, owner scope, negative recoveries, missing coverage and exact-value table.

Actual HA checks:

- Cold dashboard load without first opening the app; multiple cards share session work.
- Session expiration, app restart, stopped app, access denial, disconnected/reconnected HA.
- User changes, including in-place role changes, immediately clear old card content when observed.
- Late asynchronous responses from removed/previous-user cards do not overwrite ingress cookies.
- Resource reload after copying an updated card file; clear errors for unsupported commands.
- Real Sections/Masonry layouts, custom themes, companion app sleep/resume and external HA URL.
- No desktop/editor API exposure, extra ports, or new financial Recorder history.

### Expanded-card validation record (2026-11-13)

- Service build and Release publish succeeded; both card JavaScript files passed `node --check`.
- Temporary, dependency-free .NET assertions and HTTP checks used a separate fictional
  administration outside Git, including all import-age boundaries above, undo, missing/future
  dates, old bank data uploaded today, savings-only/empty owner scopes, and negative refunds.
- Chrome checks covered all eight gallery cards, both languages/themes, desktop/tablet/phone
  down to 320 px, disclosure resizing, category/history/month/owner filters, YAML, refresh,
  fail-closed invalid/deleted selections and the full dashboard's extracted panels.
- Widget/gallery GETs and browser filter/refresh operations preserved the administration revision.
- The published app, running with a Production environment, served the stable asset aliases
  (including the fingerprinted RCL CSS) and all eight gallery cards. HTTP smoke checks in
  HA hosting mode retained ingress identity/path requirements, hid the gallery and blocked `/api`.
- These are local/simulated-host checks, **not** a real Home Assistant session or companion-app
  validation. The actual HA checklist above remains necessary. No new test framework or
  dependency was added to the repository; the checkout's existing preview data was not used.

## 8. Continue in a new session

Open a **new session/chat with `E:\Huishoudboek` as the working directory**. If using
the CLI, `opencode "E:\Huishoudboek"` opens the project; choose a fresh session in
your interface rather than assuming it inherits this conversation.

Paste this prompt:

```text
Read AGENTS.md and HOME-ASSISTANT-CARDS.md in full, then inspect the current Git
status and relevant card code. Treat the guide's future-card table as possibilities,
not approval to implement all of them.

The current scope includes eight ingress-only cards and a realistic loopback-only
gallery at /kaarten/preview. Preserve the existing financial calculations, access
restrictions, and preview isolation. The 0.2.1 record was release-notes-only; do not
bump application versions automatically. The added budget, upcoming, data-status,
category-trend and import-status cards also belong to 0.2.1. The frontend resource
cache key is 0.2.1-cards2. Preserve the script-free SVG trend and separate import-cadence
versus bank-freshness meanings.

My next goal is: [describe the card, improvement, or investigation you want].

Summarize what exists, identify the affected files and propose a focused scope.
Ask which release-note version applies before implementing a feature or bug fix,
unless I specify it in this session. Do not push. Do not commit unless I ask you to.
```

For viewing only, replace the next-goal line with:

> Start the existing local preview without resetting its data, tell me the card
> gallery URL, and help me inspect desktop/mobile and light/dark behavior. Do not edit files.

The new session does not automatically know this chat, local temporary test scripts,
or which preview server is running. Explicitly asking it to read this committed
document provides the handoff; merely placing a Markdown file in the repo does not
guarantee it will be loaded automatically.

## 9. External references

- [HA ingress](https://contributors.home-assistant.io/apps/guides/ingress/)
- [HA custom cards](https://contributors.home-assistant.io/frontend/custom-ui/card/)
- [HA Webpage card](https://www.home-assistant.io/dashboards/iframe/)
- [HA 2026.11.1 ingress WebSocket implementation](https://github.com/home-assistant/core/blob/2026.11.1/homeassistant/components/hassio/websocket_api.py)
- [HA frontend ingress helpers](https://github.com/home-assistant/frontend/blob/dev/src/data/hassio/ingress.ts)
- [OpenCode V2 CLI](https://opencode.ai/v2/docs/cli)

Upstream APIs can change. Check the installed/supported HA version rather than
blindly copying current development-branch code.
