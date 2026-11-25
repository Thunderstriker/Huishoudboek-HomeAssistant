# Flemish energy module — implementation handoff

**Status: planning only.** No implementation or release version has been approved.
This plan narrows the ideas in [IDEAS.MD](IDEAS.MD), sections 1 and 2. If a later
session is asked to implement, read [AGENTS.md](AGENTS.md), check the working tree
for existing changes, and ask which version the Dutch `huishoudboek/CHANGELOG.md`
entry belongs to before editing code. Do not bump app or container versions
unless requested.

## 1. Scope and decisions

- Give Flemish households a place to record electricity contracts and tariff
  terms, see prices where relevant, and calculate historical energy costs when
  both readings and applicable rates are available.
- **Fetch and display published dynamic prices only for a dynamic electricity
  consumption contract.** Fixed and variable consumption contracts do not activate
  that feature. A fixed contract with monthly indexed injection is still fixed
  for consumption; a monthly index value is not a dynamic price feed.
- **The user selects single-rate or separate day/night billing in the UI.** Meter
  register labels in a file do not choose the billing scheme. The user's own setup
  is separate day/night billing.
- **Include fixed supplier fees**, with amount, billing period, VAT treatment,
  effective date and charging rules. Keep them separate from kWh rates and advances.
- Section 2 remains deferred. The user supplied a quarter-hour CSV and asked if
  it would solve the missing-consumption problem. It does. **Proposed narrow
  exception:** allow manual upload of that electricity CSV through the app;
  confirm this exception before implementing it. Without readings, show prices
  and contract terms, not household cost totals.
- No automatic Home Assistant/P1 consumption ingestion, write endpoint, external
  port or device control. Recommend deferring gas, full bill reconstruction and
  automated supplier/V-test comparisons until the electricity workflow is proven.

## 2. Contract details and tariff-card review

- Use a dedicated energy model, optionally linked to **Abonnementen & contracten**
  for metadata/reminders. Do not feed energy tariffs or advance payments into its
  subscription cost calculation: it annualizes recurring amounts and validates
  them as non-negative two-decimal prices.
- Record supplier, product, supply start/end and renewal dates (where known),
  tariff card source and issue date, and effective dates for the actual terms.
  Preserve historical tariff versions rather than rewriting old costs when a
  contract, formula or billing setup changes. The card date is not the supply
  start date or proof that the card applies to the selected period.
- Keep consumption pricing (**fixed / variable / dynamic**), billing setup
  (**single rate / day-night**) and injection terms separate. Adapt the form to
  the selected scheme and keep units and VAT explicit. Support precise decimal
  rates and documented coefficients; retain precision until display/billing rounding.
- **Proposed PDF workflow:** extract those fields into an editable draft with the
  source page, flag anything unknown or ambiguous, then require review before
  saving. Start with the supplied Luminus layout rather than promise universal
  parsing. Manual entry remains possible. Process locally; retain the uploaded
  original only if the user opts in.

### Worked example: Luminus BasicFix Elektriciteit (July 2025)

The inspected card is for a two-year fixed consumption contract with variable
injection. Use the values below as a worked example, not defaults. Confirm that
the card and supplier terms apply to the actual supply dates before calculating
November 2026 costs.

| Card item | Value |
| --- | --- |
| Single-rate supplied energy (page 1) | €0.1691/kWh, including stated 6% VAT |
| Day/night supplied energy (page 1) | Day €0.2155/kWh; night €0.1322/kWh, including stated 6% VAT |
| Fixed supplier fee (pages 1–2) | €25/year per connection, including stated 6% VAT; same fee for single rate and day/night |
| Injection formula (page 2) | `0.0428 × Belpex − 0.5467` in **cents/kWh**, with Belpex in **€/MWh**; same formula for day and night; card states 0% VAT on injection |

- Injection is indexed monthly using the applicable Luminus Belpex index. The
  card's 2.25 cents/kWh is illustrative (June 2025 index of €65.33/MWh), not the
  November 2026 rate. Verify the supplier's index definition and historical values
  before using them. Without a verified index or rate, mark injection value unknown.
- The card states that if supply ends during the first year, the full annual fee
  is due. After the first year the fee is proportional to the further uninterrupted
  supply duration. It is charged on the settlement or final bill. Keep that rule
  distinct from a daily/monthly **allocated share** shown for cost tracking.
- Grid charges, taxes, green-power/CHP charges and applicable VAT can change even
  under a fixed consumption contract. Do not carry the card's July 2025 charges
  into a November 2026 calculation without verifying the period and applicability.

## 3. Consumption CSV (if the manual-upload exception is approved)

- The inspected sample is a semicolon-delimited digital-meter export with local
  `dd-MM-yyyy` dates, separate start/end times, decimal-comma kWh volumes, EAN,
  meter, register, validation status and a description. `Afname` and `Injectie`
  are separate quarter-hour quantities; `Dag` and `Nacht` identify the register.
  The EAN is formatted as spreadsheet text (e.g. `="…"`); parse it as data and
  never evaluate formulas.
- Preview meter, period, registers, counts, totals and validation issues before
  confirmation. Store interval bounds, meter/supply-point identity, direction,
  register, quantity, unit, source and validation status. Preserve an explicit
  zero; do not infer zero from an absent reading.
- Interpret local timestamps in `Europe/Brussels`, persist unambiguous UTC
  bounds and group by Belgian local day. Verify the export's convention for
  repeated fall-back intervals. If they cannot be resolved reliably, flag the
  affected rows rather than guessing. Allow 23- and 25-hour local days.
- Re-imports must not duplicate readings. Preview conflicting corrections and
  overlapping files; require explicit replacement of affected readings rather
  than silently summing or overwriting them. Commit a confirmed batch atomically.
- Do not commit the supplied documents or personal paths, EANs, meter IDs or
  readings. Use synthetic fixtures with the same format. Keep usage data local.

## 4. Published prices and historical costs

- For a dynamic consumption contract, show published Belgian market prices for
  today/tomorrow when available, and show supplier-formula prices separately.
  Leave unpublished periods unknown. Verify the contract's referenced market
  series, resolution, Belgian coverage, historical access and reuse/storage
  rights before choosing a source. [Home Assistant Nord Pool](https://www.home-assistant.io/integrations/nordpool/)
  is a candidate for prices only, not a confirmed source or consumption integration.
- Store available historical market prices with source, interval, unit and
  publication/fetch metadata. Apply the supplier formula in effect for the period.
  Match hourly or quarter-hourly prices according to the contract's settlement
  rules; do not assume interchangeability. Handle negative prices without
  assuming the complete contract price is also negative.
- For fixed day/night consumption, multiply each register's kWh by its applicable
  tariff. Under single-rate billing, use one rate across both consumption
  registers. For variable tariffs, use the applicable period's verified tariff or
  index formula; for dynamic tariffs, sum matched interval kWh × contract price.
  Calculate injection separately before subtracting earnings from consumption costs.
- Show daily/monthly kWh, weighted prices where meaningful, consumption cost,
  injection earnings, fixed fee allocation, other charges and data coverage.
  Label components as calculated, estimated or missing. If grid charges (including
  capacity tariff), taxes or other inputs are unavailable, show a **partial subtotal**,
  not a full bill. Keep estimates, actual invoices and monthly advances distinct.
- Never create or modify bank bookings, recurring payments, budgets or cash
  forecasts from tariff data, readings or cost estimates.

## 5. Suggested delivery order and verification

1. **Contract terms first:** dedicated data model, day/night selector, fixed fees
   and editable tariff versions. Confirm whether PDF prefill is in the first slice.
2. **Historical costs:** if the CSV exception is approved, add manual import,
   coverage and fixed day/night calculation. Add verified monthly index values
   for variable injection without activating dynamic prices.
3. **Dynamic contracts:** add the verified market feed, published price view and
   historical interval matching. Treat price-source verification as a dependency
   of this slice, not a reason to block the fixed-contract work.

Acceptance checks: single-rate and day/night calculations using the same readings;
separate injection and correct cents-to-euros conversion; no dynamic fetching or
price display for fixed/variable consumption (including BasicFix); missing indexes
or prices versus actual zeros; negative prices; tariff change dates; first-year
fixed fee versus later proration; duplicate/corrected imports; both daylight-saving
transitions; partial-day/month coverage; and unchanged bank totals. Test local
SQLite migration and backup/restore with the project's safety-backup convention.
Follow existing Dutch/English UI and documentation patterns.
