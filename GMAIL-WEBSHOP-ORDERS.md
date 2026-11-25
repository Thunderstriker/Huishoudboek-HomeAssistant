# Gmail webshop orders — feature specification and session handoff

## Status and instructions for the next session

- **Planning/specification only. No Gmail integration has been implemented.**
- This document records the user's requirements and the proposed design discussed
  in the originating session. Distinguish confirmed requirements from defaults
  and implementation choices that still need validation.
- The application must remain the existing **Home Assistant app/add-on**. Do not
  convert it to a HACS integration, desktop application or separate hosted service.
- **The user has not selected a changelog version. Before implementing, ask which
  version the entries belong to in `huishoudboek/CHANGELOG.md`.** Follow `AGENTS.md`:
  concise Dutch release notes, preserve existing notes, and never assume a version.
- Do not bump application versions unless explicitly requested. If requested,
  synchronize `huishoudboek/config.yaml` and `io.hass.version` in
  `huishoudboek/Dockerfile`.
- Re-read applicable project instructions and inspect the current code before
  implementation; the source observations below are not a substitute for that.
- No real credentials, private emails, invoices or voucher codes belong in this
  document, source code, test fixtures or Git.

## 1. Goal and user context

Automatically detect order-confirmation emails from explicitly selected webshops,
extract products and prices locally, and help link those orders to existing bank
payments and product categories without double-counting expenses.

The user:

- Uses a personal `@gmail.com` account.
- Opens Home Assistant locally or through a Cloudflare tunnel.
- Has already saved Google OAuth credentials locally, outside the repository.
  The user subsequently shared a redacted JSON structure with a top-level `web`
  object, identifying a **Web application OAuth client**, not a Desktop client.
  Real client ID/secret values and the file's precise location were not shared.
- Imports bank payments approximately weekly and may later import monthly.
- Wants a daily email-check interval for their installation.
- Buys multiple products in an order and needs different categories per product.
- Sometimes buys discounted webshop vouchers on a different platform and uses
  them to pay for orders, so order value and actual bank cost can differ.
- Has Visa purchases ultimately paid through an ordinary aggregate bank
  transaction. **Do not introduce a separate creditcard payment type, account or
  workflow merely for this case.** Allocate portions of the ordinary transaction.

This is an order-confirmation workflow, not merely a Gmail viewer or PDF invoice
collector. A confirmation containing product details is useful without an invoice.

## 2. Confirmed requirements

1. Gmail access is strictly read-only. Absolutely no sending, drafting, deleting,
   archiving, labeling or marking messages read/unread.
2. Credentials are configured manually and must never be stored in Git.
3. Add a new standalone menu item/page named **Instellingen**. Initially place
   Gmail settings here; moving existing settings is a future task, not this one.
4. Integration is optional and **off by default**. Ordinary users can ignore it.
5. When enabled, the user must select an available email-check interval. Include
   **1 day**. Run checks in the add-on, not in an open browser or a PC process.
6. Only explicitly configured/enabled webshops are scanned. The app must not
   retrieve all mailbox content and then filter locally.
7. Detection/extraction depends on webshop-specific email formats. Provide
   configurable sender/subject rules and processing profiles.
8. Notify the user inside the app when new relevant orders are detected and
   provide a screen to review/link those orders to payments.
9. Orders must remain available while waiting for bank imports, including waits
   lasting weeks/months. Reconsider matches when new bank data is imported.
10. Support vouchers, differing order/payment amounts, multiple item categories,
    and partial allocations of ordinary bank payments.
11. One ordinary bank payment may fund several orders. One order may use several
    ordinary bank payments and/or vouchers. Do not require exact equality between
    the whole bank transaction and an individual order.
12. Preserve original bank amounts, booking dates and source descriptions. Never
    create duplicate spending from an email or hide an unallocated bank remainder.

Current connection direction: use the existing **Web application** client and an
in-app **Gmail verbinden** browser authorization flow. The earlier Desktop-client
helper recommendation is superseded; do not require a separate PC helper or ask
the user to recreate their credentials as a Desktop client by default. A working
Home Assistant ingress callback must still be validated before implementation.

The user accepted automatic linking of clear, unambiguous direct-payment matches,
with uncertain matches requiring review. Accounting/category application remains
an explicit confirmation; an evidence link alone does not change financial totals.

## 3. Instellingen page and connection

Proposed Gmail section:

| Control | Behavior |
|---|---|
| Gmail-integratie inschakelen | Installation-level toggle, off by default |
| Google-client instellen | Upload Google's Web-client JSON; alternative manual client ID/secret fields |
| Gmail verbinden | Open Google consent in a separate browser tab and return through the callback |
| Verbinding | Account/status display, test, replace/reconnect and disconnect |
| Controle-interval | Proposed presets: 1 hour, 6 hours, 12 hours, 1 day, 1 week |
| Eerste controle vanaf | Explicit start date; proposed suggestion: last 30 days |
| Te controleren webshops | Add/edit/enable/disable webshop rules |
| Nu controleren | Manual check without modifying the recurring schedule |
| Status | Last attempt, last success, next check and actionable error |

- The chosen interval must be explicit before scheduled scanning begins. Do not
  silently force 15-minute polling or infer the user's choice for every installation.
- Settings are shared installation settings, not browser-local preferences.
- Run at most one scan concurrently. After downtime, perform one bounded catch-up
  scan rather than a burst of missed scheduled runs.
- Disabling cancels/stops mailbox scanning and prevents subsequent fetches as
  promptly as practical. Keep collected orders, links and their review screen.
- Disconnect is separate from disable. Decide and document whether disconnect
  revokes Google authorization in addition to removing local credentials.
- No enabled webshop rules means no mailbox-content scanning.
- Handle invalid credentials, revoked access, expired refresh tokens, rate limits,
  network failures and restarts without taking normal bookkeeping offline.
- Uploading credentials or completing consent does not automatically enable mail
  scanning. Explicit connection setup/test may run while scanning is disabled;
  background scanning still requires enablement, an interval and webshop rules.

### Manual credential configuration and connection UI

Provide a setup section/wizard under **Instellingen → Gmail**:

1. **Upload Google-client JSON** (recommended) or enter **client ID** and **client
   secret** manually. No source/config-file editing or API key is required.
2. Show the exact callback URI and explain where to register it in the existing
   Web client's **Authorized redirect URIs** in Google Cloud Console.
3. Click **Gmail verbinden**. Open Google's authorization/consent page in a separate
   browser tab, not inside the Home Assistant ingress iframe or an embedded WebView.
4. The user signs in and explicitly approves `gmail.readonly`. Their browser returns
   to Huishoudboek's validated callback through the configured HTTPS address.
5. The add-on exchanges the authorization code server-side, validates the granted
   scope and privately stores the access/refresh tokens. No manual token paste or
   separate authorized-connection-file import is required in the normal Web flow.
6. Show the connected account and a **Test verbinding** action. The user separately
   chooses webshops/interval and enables scanning.

Distinguish **Client ingesteld / Nog niet verbonden**, **Verbonden** and
**Opnieuw verbinden nodig**. Client credentials alone are not mailbox authorization:

| Value | Purpose |
|---|---|
| Client ID and client secret | Identify the application to Google's OAuth service |
| Access token | Authorize short-lived read access after user consent |
| Refresh token | Obtain replacement access tokens without daily interactive login |

The user's Google JSON contains client configuration, not an access/refresh token.
The built-in connect flow supplies those tokens. Saved secret values must not be
sent back to the browser; display only that a secret is configured, with an explicit
replace action. Do not keep uploaded JSON in the repository or a browser-accessible
download location. Validate its shape/client type with bounded input. Use trusted
Google endpoints rather than accepting arbitrary `auth_uri`/`token_uri` values from
an uploaded file as destinations for secrets.

### OAuth decision and console progress

Use Google's **server-side Web application authorization-code flow** with the
user's existing Web OAuth client. A separate helper is not intrinsically needed
for OAuth; many applications implement this authorization step internally.

Google configuration to document/check:

- Dedicated Google Cloud project recommended, separate from other integrations.
- Gmail API enabled.
- Branding configured for that project; branding from a different project does
  not carry over.
- Audience **External** for personal Gmail; Gmail account added as a test user.
- Only `https://www.googleapis.com/auth/gmail.readonly` selected/requested.
- Keep the existing **Web application** client and save its credentials privately.
- Add the exact implemented callback to **Authorized redirect URIs**. Do not guess
  a path, use the Home Assistant homepage or copy an arbitrary current ingress URL.
- Authorized JavaScript origins are not needed for a purely server-side OAuth
  flow; do not introduce client-side secret handling.
- No API key or service account required.

The client type is identified by the user's redacted `web` JSON structure. API
enablement, consent settings and a usable registered callback have not been
independently verified. Confirm those without requesting secrets in chat. Do not
claim that saved credentials alone mean the mailbox is already connected.

### Home Assistant ingress callback: unresolved implementation prerequisite

The intended first-time experience is:

```text
Instellingen: upload client JSON → Gmail verbinden
    → Google consent in a browser tab
    → HTTPS callback through the user's Cloudflare/Home Assistant setup
    → Server-side token exchange and private storage
    → Return to Instellingen with connected status
```

- Validate a stable, precisely registered callback that works with ingress paths,
  Home Assistant authentication and any Cloudflare access controls. The existing
  app has no Gmail callback; adding a button does not solve routing by itself.
- Google redirects the **user's browser** to the callback. This is not a Gmail
  webhook and does not require Google's servers to reach an unauthenticated app.
- Do not expose a new add-on port, bypass Home Assistant/Cloudflare protection or
  depend on a guessed/session-specific ingress URL remaining valid forever.
- Bind a short-lived, one-time OAuth state to the authorized initiating session and
  installation; prevent CSRF, replay, cross-installation connection and arbitrary
  return URLs. Use PKCE where supported and appropriate for the selected library.
- Configure/validate the external callback base explicitly. Do not trust arbitrary
  Host/forwarded headers to construct destinations or leak authorization codes.
  Avoid logging callback codes or tokens; remove them from the browser URL after
  handling. Keep token exchange and the client secret entirely server-side.
- A user starting locally may need to complete authorization through their
  configured HTTPS address; local everyday use should continue after connection.
- If a safe callback cannot be supported, explain the blocker and ask the user to
  choose a fallback. Do not silently revert to a Desktop client/helper requirement.
- Do not use deprecated out-of-band copy/paste authorization-code flows or pretend
  a browser's localhost points at the Home Assistant host.

An External project in **Testing** issues Gmail refresh tokens expiring in
**7 days**. Before long-term unattended operation, guide the user through the
appropriate production status/personal-use verification exemption using current
Google documentation. Do not promise a permanent token or automatic exemption.

Cloudflare is not needed for ongoing polling: requests are outbound from the
add-on. Once connected, the add-on refreshes tokens and checks mail without an open
browser, running PC or available Cloudflare tunnel. Expired/revoked authorization
should show **Opnieuw verbinden nodig** and pause integration, not normal bookkeeping.

## 4. Privacy, credential storage and read-only enforcement

- Request only `https://www.googleapis.com/auth/gmail.readonly`; validate granted
  permissions and reject unexpected broader Gmail access rather than silently
  reusing a broadly authorized token.
- Never request `gmail.modify`, `gmail.compose`, `gmail.send`, `gmail.labels`,
  `https://mail.google.com/` or unnecessary identity/other API scopes.
- The readonly scope grants read access to the **whole mailbox**, not only the
  inbox or selected senders. Explain this plainly in the connection UI. Google
  cannot enforce our webshop allowlist through this scope.
- The application must enforce restricted fetching: build a Gmail search from
  enabled sender/subject/folder/date rules, list matching IDs, verify candidate
  eligibility and retrieve only relevant content. Do not bulk-fetch unrelated mail.
- Default to the inbox. Searching archived webshop messages requires an explicit
  setting/decision; do not silently expand the original inbox requirement.
- Sender names/domain matches are filtering hints, not proof a message is genuine.
  Reject lookalike-domain matches and never let email content change configuration.
- No external AI processing, telemetry containing mail, remote tracking-image
  loading, HTML script execution or automatic following of arbitrary email links.
- Apply bounded parsing, attachment size/type limits, safe filenames, sanitized
  previews and appropriate resource limits for local PDF/text processing.
- Gmail data methods used for this feature must only read. OAuth token exchange,
  refresh or explicit disconnect/revocation is separate from mutating mailbox data.
- Store user-supplied client credentials and tokens in private runtime storage
  **outside the repository**, not just in a file protected by `.gitignore`.
- Protect persisted tokens using an appropriate installation-local mechanism and
  restricted filesystem permissions. Never put credentials in app images,
  committed options/config, logs, URLs, diagnostics or database exports.
- `.gitignore`/container exclusions are defense in depth, not the primary boundary.
- Full Home Assistant backups containing private runtime storage remain sensitive;
  explain that local protection does not protect against a fully compromised host.
- Orders and financial links belong in normal administration backup/restore.
  Connection credentials do not. Avoid moving authorization silently between
  administrations or restoring it from a financial database.
- Keep only necessary order/source evidence by default. Decide explicit retention
  controls for original message content and invoice attachments; do not retain the
  whole mailbox or real private examples as test fixtures.

## 5. Webshop configuration and extraction

Each webshop rule/profile should contain:

- Display name and enabled flag.
- Explicit sender addresses or carefully bounded sender-domain matches.
- Order-confirmation subject criteria, with a preview/test explaining what matches.
- Processing profile (webshop-specific or generic).
- Optional bank-description/account hint and default category.

Extraction targets:

- Webshop, order reference, optional invoice reference, order date and currency.
- Product descriptions, quantities, unit prices and final line amounts.
- Discounts, shipping and final order total.
- Payment details (direct amount, voucher amount/reference) only where evidenced.
- Original source identifiers and extraction profile/version for traceability.

Use deterministic webshop parsers first and reuse bounded local receipt/PDF
extraction where appropriate. A generic parser may offer partial results; never
invent missing prices, totals, quantities or categories.

Validate product/discount/shipping reconciliation. Unknown/changed templates,
missing prices, uncertain totals and ambiguous currencies go to **Controle nodig**.
Allow manual correction with a useful source preview.

Repeated confirmations, shipment notices and later invoices for one order should
attach to the same order, not create new spending. Do not overwrite reviewed user
corrections or category choices when a later message arrives. Conflicting changes
need review. Login-only invoice links are not automatically retrievable through
Gmail access; show a manual action instead of scraping webshop credentials.

Initial supported shops/locales and the voucher platform are still unconfirmed.
Amazon is the user's example, not evidence that all Amazon locales/templates are
identical. Request anonymized samples as needed; use synthetic fixtures in Git.

## 6. Order queue, notifications and review

Proposed screen: **Transacties → Webshopbestellingen**, integrated with rather
than duplicating **Aankopen & garanties**.

Proposed order states:

| Status | Meaning |
|---|---|
| Nieuw | Detected, not yet reviewed |
| Controle nodig | Extraction/reconciliation needs correction |
| Wacht op betaling | Reviewed order, bank funding not imported/linked yet |
| Betalingsvoorstel | Candidate funding now available |
| Deels gekoppeld | Only some funding sources allocated |
| Afgerond | Reviewed and fully reconciled |
| Genegeerd | Irrelevant; suppress repeated resurfacing |

Extraction review, funding status and accounting application may need separate
internal fields; do not force independent concerns into one fragile status enum.

Provide a navigation badge, dashboard summary and post-bank-import summary. Keep
notifications local initially; Home Assistant push notifications are not required.
Persist acknowledgment/ignore decisions so every scan does not re-notify the same
unchanged order. Material new evidence may require a separate review notification.

The detail screen contains:

1. Order header and relevant source evidence.
2. Editable product/category lines and reconciliation totals.
3. Payment sources, explained match suggestions and remaining funding differences.

Keep pending orders visible across month changes, even before they have an account
or bank transaction. Fit owner/account scoping explicitly; do not inherit existing
filters that accidentally hide unassigned orders. Only trusted authorized app
users may access private order information; respect existing ingress security.

## 7. Late imports, matching and ordinary bank-payment portions

Persist orders independently of whether a bank payment exists. Recompute proposals
after bank imports and relevant payment/order edits, including monthly imports.

Match using amount/currency, merchant/bank reference, known order reference, dates,
account hints and existing allocations. Explain evidence to the user. Never match
on amount alone or on the date a transaction was imported into Huishoudboek.

Use the payment's **remaining allocatable amount**, not only its full original
amount. For aggregate payments, incomplete evidence, foreign exchange or vouchers,
suggest and require confirmation. A unique, strong, exact direct-payment match
may be automatically evidence-linked; never automatically apply accounting splits.

Example of an ordinary aggregate payment (such as the user's Visa payoff):

| Allocation of a EUR 600 bank payment | Amount |
|---|---:|
| Amazon clothing order line | EUR 60 |
| Amazon kitchen order line | EUR 40 |
| Remaining bank spending not assigned to these orders | EUR 500 |
| Total original bank payment | EUR 600 |

The Amazon order uses EUR 100 of the existing EUR 600 transaction. Other orders may
use other portions. **No separate Creditcard / verzamelde afrekening payment type,
creditcard account, card statement import or card ledger is required.**

- Support many orders to one bank payment, and several bank payments to one order.
- Prevent total claimed portions exceeding the original payment magnitude.
- Preserve the visible unallocated remainder and its existing category treatment
  unless the user explicitly changes it. Unallocated money must not disappear from
  reports because part of a transaction was linked.
- Account for pre-existing receipt links, allocations, refunds/reimbursements and
  internal-transfer restrictions. Never silently replace prior user work.
- Keep evidence linking separate from allocation changes that affect reporting.
- Orders waiting weeks/months are not automatically overdue bills or cashforecast
  reserves. A confirmation alone does not prove an unpaid future obligation.
- Handle removed/import-undone payments by safely detaching/reopening affected
  funding links, preserving order evidence and recomputing accounting consistently.

## 8. Vouchers and actual-cost accounting

Use a small webshop-voucher/stored-value register **separate from maaltijdcheques**.
The existing meal-voucher accounting includes earned noncash income and must not
be reused blindly for purchased discounted vouchers.

A voucher/funding lot needs:

- Applicable webshop/issuer and optional user label (not a redemption code).
- Face value/currency, actual purchase cost and funding-platform information.
- One or more funding bank-payment portions, which may be linked later.
- Remaining nominal value and redemptions linked to orders.
- Funding/review completeness and cost basis, including an explicit unknown state.

Example:

- Buy a EUR 100 Amazon voucher for EUR 95 on another platform.
- Spend it on an Amazon order with EUR 60 clothing and EUR 40 kitchen products.
- Order value remains EUR 100; voucher redemption covers EUR 100; no EUR 100
  direct Amazon bank payment needs to exist.
- Actual cost is the existing EUR 95 funding bank outflow, counted once.
- On explicit confirmed proportional categorization, allocate EUR 57 to Kleding
  and EUR 38 to Huishouden. Preserve EUR 100 as original order/product value.

Also support partial voucher redemption, vouchers spanning orders, several
vouchers in an order, and voucher-plus-direct-bank funding. A voucher may itself
be funded by a **portion of an ordinary aggregate bank transaction**. For example,
EUR 95 of a Visa payoff can fund the EUR 100 voucher used in the order above.

Two distinct reconciliations are necessary:

1. Nominal order coverage: voucher face value redeemed plus direct payments covers
   the order total. An unexplained difference requires review, not forced matching.
2. Actual cost allocation: bank-funded cost is allocated once across the redeemed
   value/products, not counted again at redemption.

Allocate a known voucher lot's actual cost proportionally to nominal redemption,
with deterministic cent rounding and a residual correction on final redemption.
Do not imply all vouchers cost face value or use unlimited untracked voucher value
to make mismatches disappear. Gifts/unknown cost bases need an explicit decision.

Unspent/unassigned purchased voucher cost remains visible and traceable to its
original bank outflow. Do not hide it from cash totals, manufacture bank assets or
increase freely available bank cash. Define an explicit consistent treatment for
unassigned category cost before implementing accounting application.

Actual spending follows real bank cost once; order/product views retain nominal
prices. Do not introduce synthetic duplicate expenses or a second purchase on
voucher redemption. Unknown funding may be saved pending, but does not justify
fabricated cost-based category allocations.

## 9. Product categories, totals and dates

- Each product supports its own ordinary expense category/subcategory.
- A webshop default is a suggestion, never a rule that all Amazon purchases share
  one category. Learn suggestions only from confirmed user choices.
- Preserve discounts and shipping with explicit allocation/category treatment.
  Reconcile final line totals, not VAT twice or voucher value as a product discount.
- Before applying categories, show order totals, funding coverage, actual cost,
  category totals, allocation remainders and existing splits that would change.
- Applying financial allocations is explicit and must respect existing validation.
  An automatically detected or merely linked order does not become extra spending.
- Preserve the original bank booking date, amount and description. Store the order
  date separately; optional evidenced charge dates do not rewrite bank dates.
- Initial actual-cost/category reporting follows existing bank-date behavior.
  Alternate purchase-date reporting is a separate decision, not an implicit change.
- Foreign-currency charges, FX differences, fees, refunds, cancellations and
  incomplete/partial payments require evidence and explicit reconciliation. Do not
  infer exchange rates or force an order nominal total to equal a bank amount.

## 10. Persistence, concurrency and reliability

- Store reviewed orders/items/evidence relationships and financial allocations in
  the administration database with migration, backup/restore and validation support.
- Keep credentials and installation-specific connection state outside financial
  exports; clearly separate portable order data from authorization and sync cursors.
- Deduplicate by account/source message and attachment identifiers plus scoped
  webshop/order identity. Do not assume order references are globally unique.
- Persist ignored/processed decisions and safely reprocess after restart/restore
  without duplicating orders, notifications or financial applications.
- Use bounded paginated queries with retry/backoff. Checkpoints must not skip mail
  after partial failures. Use suitable overlap/deduplication for delayed arrivals and
  date-boundary/timezone behavior while staying within the configured scope.
- Coordinate scan/import/user edits using existing revision/concurrency patterns.
  A background scan must not overwrite an open draft or confirmed financial change.
- Make review/application operations atomic and idempotent. Prevent concurrent
  sessions from allocating the same available payment or voucher value twice.
- Preserve pending orders when a payment is deleted or an import is undone. Ensure
  restored data cannot silently exceed funding limits or retain stale authorization.

## 11. Existing implementation touchpoints

Source observations from the originating session:

- `huishoudboek/source/src/Huishoudboek.Web/Components/Shared/MainNavigation.razor`
  defines navigation sections. Add Instellingen without moving existing settings;
  the existing `/instellen` setup/checklist page is a different concept.
- `huishoudboek/source/src/Huishoudboek.Web/Components/Pages/Purchases.razor`
  implements Aankopen & garanties, product/category editing and local receipt review.
  Current bank suggestions use exact amount within a seven-day date window.
- `huishoudboek/source/src/Huishoudboek.Web/ReceiptBankSearch.cs` provides existing
  bank-search behavior; extend/reuse suitable parts rather than duplicate it.
- `huishoudboek/source/src/Huishoudboek.Core/PurchaseReceipts.cs` currently has one
  `TransactionId` and optional `MealVoucherAmount`. Application requires one exact
  remaining bank payment and reconciled reviewed items. It cannot directly model
  many-to-many order funding, discounted vouchers or portions of aggregate payments.
  Design a real extension; do not simply weaken validation to allow mismatches.
- `huishoudboek/source/src/Huishoudboek.Core/TransactionAccounting.cs` and
  `MealVouchers.cs` affect reports, splits, noncash lines and reference validation.
  Preserve current bank totals and keep purchased vouchers separate from meal credits.
- `huishoudboek/source/src/Huishoudboek.Service/ReceiptAdministration.cs` performs
  authenticated receipt handling with temporary upload state and revision checks.
  Background extraction should not bypass auth/concurrency assumptions by reusing
  circuit-bound services inappropriately.
- `huishoudboek/source/src/Huishoudboek.Service/InvestmentMarketStore.cs` demonstrates
  installation-local protected secret storage outside financial restore paths.
  Reuse appropriate patterns, but assess Linux key protection and backup sensitivity.
- Home Assistant bootstrap uses private `/data` storage. Do not put real credentials
  in `config.yaml`, the Dockerfile or checked-in JSON/options.
- Existing regression tests live under
  `huishoudboek/source/tests/Huishoudboek.RegressionTests`.

No schema number, package versions, release version or final class names have been
chosen. Verify the current structure before selecting any of these.

## 12. Suggested delivery sequence

1. Confirm changelog version, initial webshop/locales/platform and unresolved
   accounting choices. Inspect current code, validate the Web OAuth ingress callback
   approach and settle the funding data model before promising an in-app connection.
2. Add Instellingen, disabled-by-default persisted settings, private credential
   storage, JSON upload/manual client fields, built-in **Gmail verbinden** consent,
   validated callback/token exchange and reconnect/test/disconnect guidance.
3. Add allowlist-constrained background scanning, initial supported extraction
   profiles, persistent deduplicated orders, review UI and local notifications.
4. Add explained match proposals after imports and ordinary bank-payment portion
   links, without duplicate reporting or automatic replacement of existing splits.
5. Add purchased-voucher provenance/redemption and explicit actual-cost category
   application, including mixed categories and aggregate-payment funding chains.
6. Complete migrations, regression coverage, responsive/accessibility behavior,
   Dutch/English UI strings, user documentation and required Dutch changelog entries.

Intermediate delivery must be honestly labeled. Do not advertise full voucher or
partial-payment support while relying on the existing single-payment receipt model.

## 13. Acceptance criteria and essential tests

- Existing installations make no Google calls by default. Explicit authorization
  and connection tests may run before enabling scanning; automatic mailbox scanning
  requires a connected, enabled integration, selected interval and webshop rules.
- Web-client JSON upload/manual client fields configure the app but do not grant
  access or enable scanning. No Desktop client, PC helper or manual refresh-token
  file is needed for the normal built-in connection flow.
- The callback works through configured HTTPS ingress/auth protection with an exact
  registered redirect URI and a separate Google browser tab. Denied consent,
  mismatched redirect, expired/replayed state, interrupted setup and reconnect
  produce safe actionable errors without exposing secrets/codes or saving a wrong
  account. Uploads cannot redirect credential/token requests to arbitrary hosts.
- Daily/preset/manual scheduling works with closed UI, restarts, downtime, disable,
  cancellation, no enabled rules and connection failures.
- Only configured inbox/webshop candidates are fetched; out-of-scope messages,
  lookalike domains and unsafe HTML/links/oversized attachments are not processed.
- Only readonly OAuth permissions are requested/accepted. Test read-only behavior
  without attempting a real destructive action against the user's mailbox.
- Client secrets/tokens never appear in logs, Git, images, diagnostics or financial
  database exports. Connection UI does not re-display saved secret values.
- Products/prices/categories reconcile for supported templates; unsupported layouts
  preserve partial evidence and ask for review rather than fabricate data.
- Repeated mail, later invoices, pagination retries and restarts create one order
  and do not replace corrected fields or duplicate notifications/applications.
- Orders detected before weekly/monthly bank imports remain visible and gain
  proposals once suitable transactions arrive; import date is not the match date.
- A EUR 100 order links to EUR 100 of a EUR 600 ordinary bank payment while EUR 500
  remains visible. Multiple orders share that payment without over-allocation.
- Several payments can fund one order. Existing splits/links are not overwritten
  without confirmation; concurrent reviews cannot spend the same remainder twice.
- A EUR 100 voucher bought for EUR 95 funds an order without a EUR 100 webshop bank
  payment, counts EUR 95 once and supports a confirmed EUR 57/EUR 38 category split.
- The EUR 95 voucher funding can itself be a portion of an aggregate bank payment.
- Partial voucher use, multiple orders/vouchers, mixed voucher/bank funding and
  cent rounding preserve both nominal balances and actual-cost funding limits.
- Unassigned payment/voucher cost stays visible; no artificial income/free bank
  cash or expense duplication appears in budgets, reports or forecasts.
- Undo/deletion/restore reopens or safely detaches funding without losing orders or
  retaining impossible allocations. Owner/account scopes do not hide pending orders.
- Foreign currencies, fees, refunds/cancellations and unknown voucher cost require
  explicit supported handling/review. Original bank data remains unchanged.
- Responsive Dutch/English screens support keyboard navigation, labeled controls,
  clear validation and existing unsaved/stale-edit protection.
- Use synthetic or properly anonymized fixtures only, never the user's real mail,
  credentials or voucher redemption codes.

## 14. Open decisions to resolve with the user

1. **Changelog version — mandatory before implementation; not specified.**
2. Initial webshop profiles/locales and voucher platform. Amazon was discussed;
   exact senders and representative formats are not yet supplied.
3. Validate the safe stable **Web OAuth callback** through Home Assistant ingress
   and the user's HTTPS/Cloudflare setup. The existing client is a Web client; do
   not replace it with a Desktop client by default. API/consent/redirect settings
   and long-term token availability still need checking without exposing secrets.
4. Confirm proposed interval presets, initial lookback and optional archived-mail
   support. Daily checking and selected-webshop-only fetching are required.
5. Confirm exact automatic evidence-link thresholds; uncertain aggregate/voucher
   matches and all accounting application require user confirmation.
6. Define treatment of unassigned purchased voucher cost, gifted/unknown-cost
   vouchers, cross-currency funding and returns/refunds before applying accounting.
7. Define minimal source retention/attachment options and admin/account ownership
   for the connected mailbox and newly detected unassigned orders.

## Reference documentation (verify current behavior when implementing)

- Gmail scopes: <https://developers.google.com/workspace/gmail/api/auth/scopes>
- OAuth overview and refresh-token expiration:
  <https://developers.google.com/identity/protocols/oauth2>
- Server-side Web application OAuth:
  <https://developers.google.com/identity/protocols/oauth2/web-server>
- Verification exemptions:
  <https://support.google.com/cloud/answer/13464323>
