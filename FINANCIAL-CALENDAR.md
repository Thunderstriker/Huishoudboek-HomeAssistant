# Financial calendar and learned spending — 0.3.3

## Goal

Add a financial month calendar on its own **Overzichten → Financiële kalender**
screen, linked from **Financiële vooruitblik**, combining existing scheduled
income/bills with locally learned everyday spending. The calendar is
account-specific and respects the owner filter. It is not a bank connection and
never creates transactions or changes recorded bank balances.

## Purchase dates and evidence

- Preserve the original bank booking date, amount and description.
- Prefer an explicitly corrected purchase date, then a reviewed linked receipt,
  then an unambiguous `DD-MM-YYYY HH:MM` (also `/` or `.`) timestamp in the original
  description. Reject invalid, future, ambiguous and implausibly old timestamps.
- Description timestamps are candidates, not independently verified facts. Show
  their source and the booking date for review; allow correction, exclusion and reset.
- Do not move Monday bookings to Saturday without evidence. Value dates are not
  automatically purchase dates.
- The supplied private spreadsheet was inspected read-only: 98 payments, 97 with
  timestamp candidates, including 15 Saturday and 12 Sunday purchases booked on
  Monday. It is not a fixture, is not imported automatically, and must not be
  committed or copied into documentation/tests.

## Learning and controls

- Discover suggestions per current account/category from up to 12 recent complete
  weeks. Require at least 6 explicitly covered weeks and repeated weekday evidence.
  Allow a 7-day booking window so an unposted weekend is not learned as zero.
- Coverage comes from the existing complete-statement confirmations, not the first
  and last transaction. Gaps do not count as zero spending; covered quiet days do.
- Use ordinary bank-funded spending allocations, excluding fixed-payment links,
  transfers, mortgages, savings and exceptional categories. Voucher-funded receipt
  costs never increase bank spending estimates. Respect mixed-category purchases.
- Automatic learning and the residual daily estimate use only selected category
  IDs. Default new/legacy selections to Voeding and Medische kosten once; renaming
  categories must preserve their selection. Excluded categories contribute no
  automatic deduction or optional reserve. Known future transactions, scheduled
  costs and income always retain their cash effect regardless of category.
- Combine purchases on the same day. Show frequency, average on active days and
  expected weekday amounts (including covered zero days). Weight recent weeks more
  heavily when distributing spending across weekdays.
- Unknown purchase dates in selected categories remain in the ordinary daily estimate. They must neither
  teach false Monday habits nor disappear from the forecast.
- Suggestions require acceptance once; accepted patterns recalculate after imports.
  Users can pause a pattern, choose weekdays, override its weekly amount, skip or
  adjust individual occurrences and return to automatic estimates.
- Learned spending replaces its own portion of the daily run-rate rather than
  adding to it. Preserve the selected categories' overall historical spending level
  unless the user explicitly overrides it. Estimates do not affect actual expenses or budgets.
- Recent unconfirmed occurrences are short-lived planning reserves, not overdue
  bills. Account for already recorded spending without linking unrelated payments;
  permit dismissing a skipped trip. No accumulating historical shopping debt.

## Calendar

- Monday-first monthly grid, Today/previous/next controls; today through 90 days of
  forecasts, with actual activity for earlier days in the current calendar view.
- Payment/income chips, learned-spending estimates and daily closing balance.
- Everyday spending already deducted by the forecast is also visible in the grid:
  a trend-line icon marks accepted learned patterns; an approximation icon marks
  the remaining historical daily estimate, not a learned weekday/category claim.
  Both use a lavender fill and purple dashed outlines in the grid, legend and day
  details, with dark high-contrast text, a Dutch/English legend and accessible
  descriptions. Payment chips keep their neutral fill; day backgrounds keep their
  risk colours, so forecast styling is not a new risk level.
  Show only positive applied estimates, after recorded-spending offsets, with no
  extra deduction. Group multiple learned categories into a compact total in the
  grid (individual amounts in hover help and day details). Forecast summaries stay
  visible beside the two payment chips and on mobile; they do not disappear into
  payment overflow. The daily remainder does not count as another dated outgoing
  event for risk colours or reasons.
- Once a date is reached, the grid shows recorded activity rather than old guesses.
  Keep the existing short-lived pending-reserve handling separate from dated chips.
  Accepted patterns continue recalculating from imported actual history; this does
  not introduce stored prediction-versus-actual comparisons or change learning.
- Green: above protected buffer. Yellow: outflow above twice the ordinary daily
  baseline (minimum €25). Orange: three or more dated outgoing events, or balance
  below protected buffer. Red: projected intraday balance below zero. Strongest wins.
- Use the conservative intraday low, not just the closing balance. Explain every
  colour, show a legend and textual status, and mark stale/incomplete data visibly.
- Unknown-date fixed reserves and pending shopping reserves are explained separately,
  never presented as payments definitely due tomorrow.
- Selecting a day shows opening/lowest/closing balance, scheduled items, learned
  costs, residual everyday spending and original booking dates where relevant.
- Keep detailed tables and affordability checks on **Financiële vooruitblik**, using
  the same forecast as the separate calendar screen and its learned-spending controls.
  No implicit transfers between accounts, colour-only warnings, external AI or cloud processing.

## Delivery and verification

- SQLite schema 17 stores pattern preferences, purchase-date decisions and occurrence
  adjustments. Take the existing automatic safety backup before migration. Include
  settings in database backup/restore and clean references on deletion/import undo.
- Add Dutch and English UI strings, responsive keyboard-accessible components,
  documentation and a Dutch changelog entry under 0.3.3. Release metadata is not
  bumped merely to assign the changelog entry.
- Test timestamp ambiguity/year boundaries, receipt/manual precedence, missing
  coverage, zero days, weekday aggregation, splits/vouchers, opt-in and pause,
  explicit future transactions, pending/skip handling, risk precedence, owner scope,
  migration/restore and both UI languages. Use synthetic data only.

## Forecast visibility follow-up — 0.3.3.1

- Explain future balance changes with visible learned and residual spending chips,
  matching icons in day details, and compact mobile summaries as described above.
- Record this display enhancement in the Dutch changelog under 0.3.3.1 without
  changing application/container version metadata or the forecast calculations.

## Selected-category and recurring-day follow-up — 0.3.3.3

- The category selection applies to all accounts and both calendar/outlook; pattern
  evidence, acceptance and amounts remain account-local and respect the owner scope.
  Explicitly empty selections disable automatic spending but not known payments.
- Infer regular purchase weekdays from local SQLite-backed history, not the supplied
  spreadsheet, chat examples or synthetic tests. Retain up to 12 complete covered
  weeks, the seven-day booking window and the purchase-date evidence precedence.
- Require purchases on each automatic weekday in at least four and at least half
  the covered weeks. A Wednesday/Saturday habit should allocate different expected
  amounts to those days; a single purchase on Monday must not create Monday shopping.
- Only the historical recurring portion moves to those weekdays. Irregular selected
  spending and unknown dates stay in an explicitly labelled unscheduled daily reserve.
  An irregular medical bill must not invent recurring appointments. Preserve total
  selected spending, explicit-payment offsets, manual overrides and occurrence skips.
- Keep one-time acceptance. Show detected days and amounts, and explain separately
  missing coverage, missing purchase dates, insufficient repetition and paused or
  unaccepted patterns. Make unapplied patterns discoverable from the calendar.
- Persist category IDs inside the existing insights metadata, included in normal
  SQLite backup/restore. Missing legacy preferences get defaults; an explicit empty
  list remains empty. No schema migration or application/container version bump.
- Cover defaults, renames/deletion, excluded history and future payments, mixed
  allocations, year-long Wednesday/Saturday history, rare off-day purchases, medical
  irregularity, no double deduction, empty/legacy settings and both UI languages
  with synthetic regression tests. Record Dutch release notes under 0.3.3.3.
