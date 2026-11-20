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
- Combine purchases on the same day. Show frequency, average on active days and
  expected weekday amounts (including covered zero days). Weight recent weeks more
  heavily when distributing spending across weekdays.
- Unknown purchase dates remain in the ordinary daily estimate. They must neither
  teach false Monday habits nor disappear from the forecast.
- Suggestions require acceptance once; accepted patterns recalculate after imports.
  Users can pause a pattern, choose weekdays, override its weekly amount, skip or
  adjust individual occurrences and return to automatic estimates.
- Learned spending replaces its own portion of the daily run-rate rather than
  adding to it. Preserve the existing overall historical spending level unless the
  user explicitly overrides it. Estimates do not affect actual expenses or budgets.
- Recent unconfirmed occurrences are short-lived planning reserves, not overdue
  bills. Account for already recorded spending without linking unrelated payments;
  permit dismissing a skipped trip. No accumulating historical shopping debt.

## Calendar

- Monday-first monthly grid, Today/previous/next controls; today through 90 days of
  forecasts, with actual activity for earlier days in the current calendar view.
- Payment/income chips, learned-spending estimates and daily closing balance.
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
