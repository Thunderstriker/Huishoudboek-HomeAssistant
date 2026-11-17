# Follow-up: stock and ETF portfolio

Status: original investigation and proposed design, retained for reference.
The user subsequently approved implementation for **0.3.0**, selecting Alpha Vantage,
personal-only access, daily updates by default, configurable free/paid limits, historical
multi-currency trades and FIFO sales. The user explicitly requested **performance cards,
not charts**, with weighted remaining purchase costs across all lots.

Current implementation and setup: [Beleggingen en Alpha Vantage](huishoudboek/DOCS.md#beleggingen-en-alpha-vantage).
The EODHD selection and original pending decisions below are historical, not current defaults.
Alpha Vantage coverage must still be checked locally with the user's own key; no personal
key was requested or used during implementation.

Research date: 12 November 2026. Recheck provider coverage, prices and terms before implementation.

## 1. User requirements and agreed direction

- Add stock and ETF tracking to the local Huishoudboek application.
- No real-time tracker: the original suggestion was approximately every three hours;
  the user subsequently confirmed that **daily closing prices are sufficient**.
- Prefer a free solution, without a paid market-data subscription.
- The broker is **Bolero**.
- Track quantities, actual purchase history, estimated holding values and profit/loss.
- Actual purchase prices are more relevant than historical market prices.
- Historical market-price charts and bulk historical-price downloads are not needed.
- Add a local setting for the user's own market-data API key.
- Keep purchase/sale records and calculations locally. Do not send quantities,
  purchase prices, bank transactions or account balances to the price provider.

## 2. Instruments investigated

These are researched listings, not a confirmed inventory of the user's holdings.
Confirm the actual instrument, share class, exchange and currency against Bolero
before saving a price-provider mapping. In particular, VWCE has multiple listings.

| Instrument | Identity / ISIN where established | EODHD candidate symbol | Trading currency | Evidence |
| --- | --- | --- | --- | --- |
| VWCE | Vanguard FTSE All-World UCITS ETF, accumulating; `IE00BK5BQT80` | `VWCE.XETRA` | EUR | Exact provider listing found |
| STK | State Street SPDR MSCI Europe Technology UCITS ETF EUR (Acc); `IE00BKWQ0K51` | `STK.PA` | EUR | Exact provider listing found; STK is its Paris ticker |
| TDT | Matched to VanEck AEX UCITS ETF; `NL0009272749` | `TDT.AS` | EUR | Exact provider listing found; confirm identity from Bolero |
| Amazon | Amazon.com Inc, Nasdaq listing | `AMZN.US` | USD | Provider listing found; daily-price API successfully tested with official demo token |
| AMD | Advanced Micro Devices Inc, Nasdaq listing | `AMD.US` | USD | Exact provider listing found |
| SpaceX | Provider identifies Space Exploration Technologies Corp. Class A, Nasdaq listing | `SPCX.US` | USD | Exact provider listing found with recent daily-price information |
| OpenAI | No direct ordinary listed company share confirmed in this investigation | Not established | Not established | Leave unpriced or manually valued until an exact instrument is verified |

Important identification corrections:

- The user explicitly clarified STK's full ETF name. The initial search result for
  plain US ticker `STK` was an unrelated Columbia Seligman closed-end fund. **Do not use it.**
- The researched Paris ETF maps to `STK.PA`, not plain `STK`.
- Do not substitute OpenAI tokens, derivatives or funds with OpenAI exposure for
  direct company shares.
- Identify instruments by ISIN/share class plus exchange and currency where possible;
  ticker text alone is insufficient.

## 3. Leading data-provider candidate: EODHD Free Package

The provider's public catalogue contains all six priced instruments above. Its
published end-of-day documentation states that the free plan supports the endpoint
for any supported ticker, with:

- A personal API key obtained through a free account; no credit card required to start.
- A price of $0/month for the Free Package, rather than the $19.99 paid historical plan.
- 20 API calls per day.
- One end-of-day request per instrument costing one call.
- Up to one year of downloaded market-price history.
- European exchange updates normally 2–3 hours after market close; major US exchange
  updates normally within 15 minutes after market close.

A once-daily update of the six priced instruments would use six calls, excluding
lookup requests, retries, currency data and corporate-action requests. All requests,
including manual refreshes, must share one persistent daily budget.

Only a short recent price response is needed to obtain the latest completed
trading date and close together. Do not download a year's history unnecessarily,
and do not discard the price date by retrieving only a scalar price.

The one-year market-price limit does **not** prevent retaining older actual
purchases imported from Bolero or calculating their current gains from recorded costs.

### Verification boundary

- Instrument coverage was confirmed from EODHD's own public instrument pages.
- Amazon's daily-price API was tested successfully using the official `demo` token.
- Responses for the other instruments have **not** been tested with a personal free key.
- The demo token only permits sample symbols. A demo-token 403 is an entitlement
  restriction, not proof that the instrument is absent or requires a paid plan.
- Before implementation, validate the configured instruments with the user's own
  free key, locally. Do not request that the user paste the key into chat or commit it.
- EODHD describes its prices as indicative rather than guaranteed official exchange
  prices. Compare samples with Bolero before relying on the estimates.

### Licensing: unresolved fit for a household application

EODHD's official help pages explicitly allow personal automated downloads, local
databases/backups, private portfolio software and private display on one's own devices.
They also explicitly restrict personal plans to **one person**:

- Do not share the API key or account, including with family members.
- Do not show the provider's data or data-bearing outputs to other people.
- A local installation, free/open-source code or authenticated access does not
  automatically remove these restrictions.
- Publishing application code is different from publishing cached data or secrets.

**This is a design constraint, not resolved merely by adding an API-key field.**
Huishoudboek currently grants all admitted Home Assistant ingress users the same
financial access. Clarify whether only the subscribing individual will access the
investment data. If household sharing is required, obtain appropriate licensing or
choose a provider that explicitly permits it; do not silently assume compliance.

### Other providers investigated

- **Yahoo Finance:** tested feeds returned the ETFs and stocks, but automated access
  permission was not established. General terms restrict automated collection without
  prior permission; Finance documentation prohibits redistribution. Not the default.
- **Alpha Vantage:** free personal key normally permits 25 requests/day and daily
  quotes, but exact coverage of these three European ETFs remains unverified.
  Their demo key refused the arbitrary symbol searches used in this investigation.
- **Twelve Data:** free equity coverage is US-focused; the researched European listings
  are not covered by its free plan. Free-plan display rights are also restrictive.
- **Marketstack:** has a free end-of-day offering with 100 requests/month, but exact
  instrument coverage and suitability were not established. Not selected.

## 4. Proposed feature scope — not yet approved for implementation

### API-key settings

Proposed location: **Beleggingen → Koersgegevens**.

- Optional provider integration, disabled until explicitly configured.
- Masked API-key entry, replace/remove controls and a test-connection action.
- Store the secret server-side in private local configuration, outside the financial
  export data. Never include it in source control, browser local storage, logs,
  exception messages or request URLs shown to the user.
- Decide explicitly how Home Assistant backups handle secrets; local storage does
  not itself mean encryption or exclusion from platform backups.
- Show connection status, last attempted check, last successful check and price date.
- Persist quota/scheduling state across restarts. Bound retries and manual refreshes.
- Keep the last successful price during outages, marked appropriately; never replace
  a failed quote with zero. The rest of the app must remain usable offline.

### Local portfolio records

- Instruments and explicit provider mappings.
- Buy/sell date, executed quantity, unit price, currency and actual settlement amount.
- Brokerage fees, Belgian transaction tax (TOB), and currency-conversion costs where available.
- Link positions to an investment account; respect existing ownership filters.
- Derive held quantities from the transaction ledger rather than maintaining an
  unrelated editable quantity that can diverge from purchase/sale history.
- Support manual entry regardless of whether a Bolero importer is available.
- Flag incomplete history, transfers and corporate actions. Stock splits still
  matter for quantities/cost basis even without historical market-price charts.

### Values and profit/loss

- Current quantity, remaining purchase cost, latest completed daily price and its date.
- Estimated holding value in EUR.
- Unrealized gain/loss in EUR and %, including recorded purchase costs.
- Realized gain/loss from sales separately.
- Use actual historical EUR settlement costs when known; otherwise mark any FX-based
  reconstruction as estimated. Current USD-to-EUR conversion must not rewrite old costs.
- FIFO was suggested for partial-sale reporting, but **not explicitly chosen by the user**.
  Confirm and document the method. These are portfolio calculations, not tax returns.
- If dividends are tracked, show them separately and distinguish price gains from
  total investment results. Do not invent cash dividends for accumulating ETFs such
  as STK and VWCE: internal reinvestment is already reflected in the fund value.

### Preserve existing bookkeeping

- Investment trade imports must not automatically create duplicate bank transactions.
- Existing manual securities valuations must not be added on top of calculated
  holdings values. Define an explicit manual-versus-calculated valuation mode or keep
  the portfolio estimate separate initially.
- Do not rewrite past valuations, imports, bank balances, income, budgets or savings
  contributions merely because a price changes.
- Record import provenance and detect duplicates; preview before committing.
- User-owned trade records must be included in financial backup/restore. Design
  quote-cache retention and secret handling separately.

## 5. Bolero purchase-history export: what is actually confirmed

The user asked whether purchase history can be exported. Public Bolero documentation confirms:

1. **Portefeuille → Historiek** provides historical transactions, filtered by transaction,
   market and period.
2. **Orders → Historiek** contains previous buy/sell orders, including cancelled orders.
3. **Portefeuille → Posities** can be exported to Excel, but is a current-holdings snapshot.
4. **Rekeningen → Transactiehistoriek** can export cash movements through an Excel icon.
5. Individual historical purchase prices and transaction confirmations (*borderellen*)
   are available from the position's transaction details.

**Not yet confirmed:** whether one export of executed-trade history contains all
quantities, purchase prices, ISINs, currencies, fees, TOB and EUR settlement amounts.
Do not promise a complete Bolero importer based solely on the position-export feature.

Next user-side check, preferably in Bolero's web platform:

1. Open **Portefeuille → Historiek**, select the full relevant period and include
   executed purchases **and sales**.
2. Check whether that history view offers an Excel/export icon.
3. Provide the column headers and a few anonymized rows, with fictional amounts if preferred.
4. Remove names, account numbers, credentials and other personal identifiers.
5. If the file is incomplete, inspect whether trade confirmations or a separate
   cash-history export supply missing details. Do not assume cash movements alone
   contain enough information to reconstruct quantities and purchase lots.

Do not count cancelled, rejected or still-open orders as executed trades. Account
for partial executions and repeated exports when designing duplicate detection.

## 6. Existing implementation touchpoints

The investigation found .NET 10, Blazor Interactive Server and SQLite, with existing
investment accounts and manual account-level securities valuations, but no individual
security/holding ledger or scheduled quote service.

Relevant files for a future implementation review:

- `huishoudboek/source/src/Huishoudboek.Core/Models.cs`
  — `AccountKind.Investment`, `InvestmentValuation`, `HouseholdData`.
- `huishoudboek/source/src/Huishoudboek.Core/Reporting.cs`
  — account cash and latest manual securities valuation are combined here.
- `huishoudboek/source/src/Huishoudboek.Infrastructure/SqliteHouseholdStore.cs`
  — versioned aggregate persistence and migrations.
- `huishoudboek/source/src/Huishoudboek.Service/AdministrationCoordinator.cs`
  — single-writer coordination and optimistic revisions; background quote updates
  must not overwrite edits or unnecessarily invalidate unrelated editor sessions.
- `huishoudboek/source/src/Huishoudboek.Service/ServiceApplication.cs`
  — hosting, dependency registration and application security.
- `huishoudboek/source/src/Huishoudboek.Web/Components/Pages/Accounts.razor`
  — existing account and manual-valuation UI.

Reinspect the current checkout before changing anything: it already contained unrelated
modified and deleted files during this investigation. Do not discard or restore them.

## 7. Follow-up checklist before implementation

- [ ] Obtain explicit permission to implement; this document does not provide it.
- [ ] Ask which version the required Dutch entry in `huishoudboek/CHANGELOG.md` belongs to.
      Do not choose a version or bump application versions automatically.
- [ ] Resolve the single-person provider licence versus household access requirement.
- [ ] Validate personal-free-key responses and sample quality for the actual listings.
- [ ] Confirm instrument mappings from Bolero, including VWCE exchange and OpenAI handling.
- [ ] Inspect a redacted Bolero export and decide the supported import format/fields.
- [ ] Confirm sale cost-basis method, dividend scope and existing-valuation integration.
- [ ] Design quota enforcement, secret storage, migrations, backup/restore and failure states.
- [ ] Plan tests for fees/TOB, FX, partial sales, fractional quantities, splits, incomplete
      history, duplicate imports, stale prices, quota exhaustion and valuation double-counting.

## 8. Sources

### EODHD

- [Pricing](https://eodhd.com/pricing)
- [End-of-day API, free-plan scope, response fields and update timing](https://eodhd.com/financial-apis/api-for-historical-data-and-volumes)
- [API limits and call costs](https://eodhd.com/financial-apis/api-limits)
- [Personal use and private display](https://eodhd.com/help/personal-use)
- [Private software, automation and publishing source code](https://eodhd.com/help/ai-tools)
- [Storage, backups and retention](https://eodhd.com/help/storage)
- [Plans and trials](https://eodhd.com/help/plans)
- [Terms and conditions](https://eodhd.com/financial-apis/terms-conditions)
- Instrument pages: [VWCE](https://eodhd.com/financial-summary/VWCE.XETRA),
  [STK](https://eodhd.com/financial-summary/STK.PA),
  [TDT](https://eodhd.com/financial-summary/TDT.AS),
  [Amazon](https://eodhd.com/financial-summary/AMZN.US),
  [AMD](https://eodhd.com/financial-summary/AMD.US),
  [SpaceX](https://eodhd.com/financial-summary/SPCX.US).

### Instrument identification and Bolero

- [STK identity, ISIN and exchange listings](https://www.justetf.com/en/etf-profile.html?isin=IE00BKWQ0K51)
- [VanEck TDT identity](https://www.vaneck.com/nl/en/investments/aex-etf/overview/)
- [Bolero ETF list, including VWCE ISIN](https://www.bolero.be/nl/lp/de-wereld-van-etfs/beleggen-dus-maar-waarin/etf-playlist/etf-playlist-volledige-lijst)
- [Bolero: find historical transactions](https://www.bolero.be/nl/support/veelgestelde-vragen/hoe-kan-ik-vroegere-transacties-opzoeken)
- [Bolero: order history](https://www.bolero.be/nl/support/veelgestelde-vragen/waar-vind-ik-een-historiek-terug-van-mijn-orders)
- [Bolero: historical purchase price and confirmations](https://www.bolero.be/nl/support/veelgestelde-vragen/waar-vind-ik-de-historische-aankoopkoers-van-een-bepaald-effect)
- [Bolero: export current positions to Excel](https://www.bolero.be/nl/support/veelgestelde-vragen/hoe-kan-ik-mijn-portefeuille-exporteren-naar-excel)
- [Bolero: cash-transaction export](https://www.bolero.be/nl/support/veelgestelde-vragen/rekeninguittreksels)

### Other providers and currency conversion

- [Alpha Vantage free key and limits](https://www.alphavantage.co/support/#api-key)
- [Alpha Vantage documentation](https://www.alphavantage.co/documentation/)
- [Twelve Data pricing](https://twelvedata.com/pricing.md)
- [Marketstack pricing](https://marketstack.com/product)
- [Yahoo terms, automated-access restriction in section 2(d)(9)](https://legal.yahoo.com/us/en/yahoo/terms/otos/index.html)
- [Yahoo Finance data-provider restrictions](https://help.yahoo.com/kb/finance-for-web/SLN2310.html)
- [ECB daily EUR reference rates](https://www.ecb.europa.eu/stats/policy_and_exchange_rates/euro_reference_exchange_rates/html/index.en.html)
