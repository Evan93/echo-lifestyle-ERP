# CLAUDE.md — working agreement for the Echo Lifestyle ERP codebase

This file is the standing brief for any AI session working on this repository.
Read it before writing code. It records decisions that have already been made,
so they are not silently re-litigated or accidentally undone.

---

## 1. What this is

An ERP, POS and e-commerce platform for **Echo Lifestyle**, a Bangladeshi
cosmetics retailer run by two partners (Evan and Jaman). Currently online-only,
selling through Facebook, Messenger, Instagram, phone and (from Phase 5) its own
website. Physical stores, wholesale and a broader lifestyle catalogue come later.

The goal is a system the business can run on for years — not a demo.

---

## 2. Approved stack — do not change without an ADR

- .NET 10, C#, ASP.NET Core MVC, Razor views (`.cshtml`)
- ASP.NET Core Identity, SQL Server
- EF Core for transactional writes and migrations; Dapper for reporting and
  bulk work (from Phase 2)
- Bootstrap 5, jQuery, jQuery AJAX, DataTables (server-side mode) for grids,
  Alpine.js for small per-screen interactions
- Serilog, Hangfire (in-process in `Web` until Phase 4 gives it real work)
- .NET MAUI for the Phase 7 customer and staff apps, consuming the same API

**Explicitly rejected:** Angular, React, Vue, Blazor, any SPA storefront,
microservices, event sourcing, Kubernetes, and generic repository abstractions
that only wrap EF Core.

---

## 3. Architecture

A **modular monolith**. One deployable web application, layered so the compiler
enforces the boundaries:

```
Domain          entities and business rules; references nothing
Application     use cases, DTOs, interfaces, pure logic (permissions, calendar)
Infrastructure  EF Core, Identity, adapters; implements Application interfaces
Web             MVC, Razor, authorization, composition root
Worker          Hangfire shell (empty until Phase 4)
```

`Domain` physically cannot reference EF Core or SQL Server. Keep it that way —
that constraint is what makes the accounting and inventory rules testable in
milliseconds without a database.

Organise every project by **business feature**, not by technical type.

---

## 4. Non-negotiable business rules

These come from the owner's brief and from decisions taken in Phase 0. Breaking
one is a defect, not a style preference.

1. **Stock never moves without a ledger entry.** No screen updates a stock
   balance directly. Balance tables are projections, rebuildable from the ledger.
2. **Debits equal credits.** Posted journals are immutable; corrections are
   reversals or adjusting entries, never edits.
3. **Closed accounting periods cannot be posted to** without an authorised,
   logged reopen.
4. **The server recalculates every total** — price, promotion, tax, delivery,
   order total. Never trust a figure from the browser.
5. **Money is `decimal`**, never floating point. `decimal(19,4)` in the database.
6. **UTC in the database, Asia/Dhaka at the boundary.** Business dates come from
   `BusinessCalendar`, never from `DateTime.Today` or the raw UTC date.
7. **Tax is configurable, never hard-coded.** VAT stays dormant until an NBR
   registration number is entered; then Mushak-compliant invoicing switches on.
8. **Negative stock is refused** unless the branch policy allows it *and* the
   user holds `Inventory.Stock.AllowNegative`.
9. **Never hard-delete posted financial or stock documents.** Cancel or reverse.
   Soft delete is for eligible master data only.
10. **Historical documents keep their own snapshots** of price, discount, tax and
    promotion terms, so editing a promotion never rewrites past orders.
11. **Every document posts stock through `StockMovementWriter`.** It is the one
    place that appends a ledger entry and moves the balance together, so rule 1
    holds by construction rather than by everybody remembering. Receiving is the
    only exception and only because it creates the batch in the same breath;
    anything new posts through the writer.
12. **A stock count posts the variance as a delta, never as an overwrite.** The
    sheet freezes what the system believed when it was generated; posting moves
    stock by *counted minus frozen*, applied on top of the current balance.
    Setting the balance to the counted figure would silently put back everything
    sold while the count was in progress. There is a test named for this.
13. **An uncounted line is not a zero.** Blank means nobody reached that shelf
    and is skipped entirely at posting; zero means somebody looked and found
    nothing, and writes the batch off. Never collapse the two.
14. **Cost is frozen onto a document line when it posts, not when it is written.**
    A pending adjustment carries no value, because the batch it names can be
    recosted or sold down before anybody approves it.
15. **A customer is identified by phone number, not by name.** Numbers are
    normalised through `BangladeshPhone` before they are stored and are unique
    among non-deleted customers. Orders arrive by Messenger, Instagram and
    phone; the name is written three ways and the number is not. Never store a
    raw number, never add a second write path that skips normalisation.
16. **Contact details are masked in the service, not in the view.** Anyone
    without `Crm.Customer.ViewPii` receives an already-masked object, so a new
    screen, endpoint or export cannot forget. Do not add a method that returns
    an unmasked number without the same check.
17. **Confirming an order reserves; dispatching issues.** A reservation raises
    `QuantityReserved` and writes no ledger entry, because nothing has moved.
    Only dispatch writes an `Issue` entry, first expired first out, at each
    batch's own cost. Never collapse the two, and never write a ledger entry for
    a reservation.
18. **Available is on hand minus reserved.** Every sales-facing screen shows
    availability, never on-hand — on-hand includes stock already in somebody
    else's box.
19. **Plan the whole document, then apply it.** Reserving and dispatching both
    decide every line before touching anything. `ExecuteInTransactionAsync`
    commits whatever the delegate leaves behind, so returning a failure
    part-way through a loop would commit the half that already ran.
20. **Delivery addresses are copied onto an order, never referenced.** A
    customer who moves house must not rewrite where last month's parcel went.
    The same applies to product name, SKU and price on an order line.
21. **Money is recorded through `CashTransactionWriter`, never by typing over a
    column.** `CashTransaction` is append-only, exactly like the stock ledger,
    and `SalesOrder.AmountCollected` is a projection of it. That is what makes
    "why does this order say it is paid?" answerable. Corrections are reversing
    entries.
22. **A courier's fee is money out, not a netting-off.** Deducting it silently
    from a receipt would leave the cost of delivery absent from every margin
    figure in the system.
23. **A payout that does not add up stops.** Gross minus deductions must equal
    what arrived, or somebody has to tick "post anyway" and the difference is
    recorded and audited. Never default that on.
24. **The kind of money decides its direction; nobody is asked.** An expense is
    money out, capital is money in — `CashTransaction.NaturalDirection` says so
    and a check constraint enforces it. Never add an in/out control to a form: a
    wrongly signed entry is invisible, because the totals still add up and are
    simply wrong by twice the amount.
25. **A correction is a reversal that keeps the original's kind and party.**
    Same kind, same category, same partner, same order, direction flipped, dated
    today. That is what lets every report net correctly without knowing
    reversals exist — a reversal recorded as a generic "adjustment" would leave
    its category permanently overstated. One reversal per entry, and a reversal
    is never itself reversed.
26. **A cart reserves nothing, and stores no prices.** It is an intention, not
    a claim on stock: two shoppers may hold the last jar, and the first whose
    order is confirmed gets it. Reserving at add-to-cart would let anyone empty
    the shelf by filling a basket and walking away, and cash on delivery gives
    them no reason not to. Every read re-prices from the price list and
    re-checks availability, so a basket cannot hold a stale figure.
27. **A website order lands as a draft.** It goes through the same Confirm gate
    a Messenger order does, so a joke order or a customer who never answers
    cannot tie up real stock. The storefront calls the same
    `SalesOrderService.CreateAsync` the back office does - there is one way an
    order comes into existence.
28. **The checkout browser decides nothing that costs money.** Prices, delivery
    charge, branch and warehouse are all resolved server-side; the form carries
    a name, a number, an address and a district id. Never accept a price, a
    total or a branch from a public form.
29. **The checkout never says whether it recognised the number.** No "welcome
    back", no "we don't know you". Either answer turns the form into a way of
    testing whose phone number it is and learning her name. Same reasoning as
    the single login error message (rule in §5).
30. **A blocked customer's website order is accepted and flagged, never refused
    at the checkout.** Telling somebody there that they are blocked only
    teaches them to reorder from a new number. Staff see the flag and cancel.
31. **There is no expense document.** An expense paid when it is incurred *is* a
    cash transaction, so it is one row in the log with a category on it. A
    second table holding the same facts is a second table to disagree with the
    first. Bills owed but unpaid are accounts payable and arrive with
    double-entry, not before.

---

## 5. Security rules

- **`UserType` (Staff | Customer) is fixed at account creation and is checked in
  addition to every back-office permission.** Roles are mutable data; this is
  the structural guard that stops a mis-assigned role turning a shopper into an
  administrator. Never authorise on `UserType` alone, and never skip it.
- Back office and storefront use **separate authentication schemes and cookies**.
- Authorization is **action-based** (`Module.Entity.Action`), never on role
  names. Add new permissions to `Permissions`; the seeder grants them to Owner
  automatically.
- **Hiding a menu item is never access control.** Every action authorises on the
  server. The sidebar filter is a usability nicety.
- Unknown policy names **fail closed** — a typo locks a page rather than opening it.
- Login failures return **one generic message** for every cause (wrong password,
  unknown user, inactive account, customer account). The real reason goes to the
  audit log. Do not "improve" this by being more specific.
- **Permission changes take effect within two minutes, without signing out.**
  Editing a role's permissions bumps the security stamp of everyone holding it,
  and `SecurityStampValidator` (`ValidationInterval` = 2 minutes) rebuilds the
  cookie's claims. Any future code that changes what a user may do must bump the
  stamp too, or the revocation will not stick.
- **Roles are data, not a fixed list.** Anything that validates a role name reads
  it from the database (excluding `Customer`), never from `Roles.StaffRoles` —
  otherwise custom roles can be created but never assigned.
- Grant `[AllowAnonymous]` **per action, never on a controller** that also has
  authorised actions — a class-level attribute silently overrides them.
- Anti-forgery tokens on every state-changing request, AJAX included.
- Secrets never in source. Seed passwords come from user-secrets or configuration;
  outside Development the seeder refuses to invent one.

**Lock-out guards.** Administration screens refuse any change that would leave
the system unadministrable, and every one of them is covered by a test. Keep
adding to this list as new master data arrives:

- the last active branch cannot be deactivated;
- a warehouse that is primary for an active branch cannot be deactivated;
- the last active Owner cannot lose the role or be deactivated;
- nobody can deactivate their own account;
- staff cannot be created with no branch unless they are an Owner;
- system roles cannot be renamed or deleted, and Owner's permissions are fixed;
- a role somebody still holds cannot be deleted.

---

## 6. Scale discipline

The business currently has ~24 SKUs and two people. The architecture must
accommodate the full brief; **the build must not**.

- Push back when a requested feature is a 2028 problem. Say so, with reasoning.
- Every module gets a **quick-entry fast path** (one screen, e.g. Quick Purchase,
  Quick Sale) alongside the full formal workflow. Small teams must not be forced
  through requisition → RFQ → approval → partial GRN to record buying stock.
- Quick paths are an orchestration convenience only: they call the same domain
  rules and post the same ledger and journal entries in one transaction. There
  is exactly one way stock and money move in the database.

**Settled launch scope** (see `Delivery-Plan-And-Launch-Decisions.md`):

- **Cash on delivery only**, collected by Steadfast. Payment gateways — bKash,
  Nagad, card — are future development. Model payments so a gateway slots in
  later; do not build one now.
- **One warehouse, so no transfers.** Inter-location movement, in-transit stock
  and multi-location valuation wait until a second location exists.
- **The website is expected to be the main order channel.** Quick Sale stays
  functional but plain; the storefront gets the design investment.
- **Guest checkout**, with an optional account afterwards. Orders attach to a
  customer record by phone number either way, so loyalty works later without
  accounts existing at launch.

---

## 7. Code standards

- Thin controllers. Business logic lives in use cases, rules in the domain.
- One database transaction per business use case.
- `async`/`await` throughout with `CancellationToken`. Never `.Result`,
  `.Wait()`, or `async void`.
- Explicit DTOs and view models; no entities on the wire.
- No SQL in views or JavaScript. Parameterised SQL only, always.
- No swallowed exceptions. Catch only what you can handle, and log it.
- Feature-organised JavaScript in `wwwroot/js/<feature>/`; no large scripts
  inside Razor views.
- Output encoded; user input never concatenated into SQL or markup.
- Fix compiler warnings rather than suppressing them — `ASP0026` caught a real
  authorization hole in Phase 1. Prefer `TreatWarningsAsErrors` on `src/` once a
  phase settles.

---

## 8. Testing

- xUnit. `UnitTests` covers `Domain` and `Application` only — no database.
- `IntegrationTests` runs against **real SQL Server** (`EchoLifestyle_Tests`,
  override with `ECHO_TEST_CONNECTION`). Never an in-memory provider: filtered
  unique indexes, `rowversion` concurrency and restricted deletes do not exist
  there, so a green in-memory test proves nothing about production.
- Test the guarantees that live in the database, with raw SQL where that is what
  it takes to prove the constraint is real.
- **Test fixtures must mirror `Program.cs` registration for registration.** A
  fixture that registers less than the web host passes tests that fail in
  production — `AddDefaultTokenProviders()` and `AddDataProtection()` were both
  found this way. When `Program.cs` gains a registration, check the fixtures.
- **Identity tests get their own database** (`EchoLifestyle_IdentityTests`,
  `IdentityFixture`). They assert on questions answered by counting rows across
  the whole database ("is this the last active Owner?"), so sharing one would
  make results depend on class execution order.
- **Soft delete is `Remove()`, and it is safe with dependents loaded.** The
  interceptor demotes a Deleted entry to Modified at save time, and
  `CascadeDeleteTiming`/`DeleteOrphansTiming` are set to `OnSaveChanges` so EF
  does not cascade before that happens. Do not change those timings back: the
  failure only appears when the dependents are tracked, which is to say never in
  a small test and always in a real editor.
- **No test may count rows globally** in a shared database. Scope every count to
  the data the test created or seeded; a global count passes only until another
  class adds a row.
- **A test class that reasons about a whole database gets its own database.**
  `SeedingFixture` and `IdentityFixture` exist for exactly this. Weakening the
  assertions to fit a shared database would delete the thing being tested.
- **Fixtures seed their own baseline.** `DatabaseFixture` runs the seeder after
  migrating, so units of measure and the default price list are always there.
  Never let one test class rely on another having seeded them - that is an
  undeclared dependency on xUnit's class ordering, and it breaks the day a new
  class is added.
- A feature is not complete without: business rule, server-side authorization,
  validation, migration, audit behaviour, concurrency handling, tests, UI
  behaviour, error handling, documentation, and **evidence that build and tests
  passed**.

Never claim something works without running it.

---

## 9. Commands

```bash
cmd //c build.bat                       # restore, build, test -> tools/build-output.txt
./tools/migrate.bat add <Name>          # create a migration
./tools/migrate.bat update              # apply migrations
./tools/migrate.bat script              # idempotent SQL for deployment
cmd //c tools/env-check.bat             # report toolchain and SQL instances
dotnet run --project src/EchoLifestyle.Web
```

Migrations apply automatically **only in Development**. Elsewhere the app warns
that migrations are pending and carries on — applying schema changes is a
deliberate deployment step.

---

## 10. Roadmap

| Phase | Contents | Status |
|---|---|---|
| 0 | Discovery and architecture | Done |
| 1 | Foundation: identity, roles, permissions, branch scoping, audit, base UI, CI | Done |
| 1.1 | Administration screens: company, branches, warehouses, users, roles, audit viewer | Done |
| 2a | Catalog: brands, categories, products, variants, options, pricing, images | Done |
| 2b | Procurement: suppliers, stock ledger, batches, Quick Purchase, landed cost | Done |
| 3 | Inventory: stock on hand, ledger, near-expiry, balance rebuild, adjustments with approval, stock count | Done |
| 4a | CRM: customers, phone identity, delivery addresses, BD geography, blocking | Done |
| 4b | Sales: orders, reservations, COD lifecycle, courier dispatch, returns | Done |
| 4c | Money in: cash ledger, courier payout reconciliation, COD settlement | Done |
| 4d | Money out: expenses with categories, supplier payments, partner capital, refunds, cash position | Done |
| 5a | Storefront: catalog, categories, brands, product pages, search | Done |
| 5b | Storefront: cart, delivery charge, guest checkout, order intake | Done |
| 5c | Storefront: SEO, sitemap, order tracking, static pages | Next |
| 6 | Physical POS, advanced promotions | Deferred until a store opens |
| 7 | CRM, loyalty, targets, marketing, MAUI apps | |
| 8 | Full reporting, budgets, hardening, deployment, UAT | |

---

## 11. Open decisions

- **Accounting is built to standard best practice, marked "pending professional
  review".** No accountant is engaged. Chart of accounts, weighted-average
  valuation and tax treatment all need sign-off before they are trusted for
  filing. Never present them as authoritative.
- **Double-entry is deferred to December**, when NBR registration is expected.
  Until then every purchase, sale, payment and expense is still recorded in
  full — amount, date, party, category — because double-entry is a projection
  over those transactions and can be generated backwards. Do not thin that
  transaction detail to save time: it is the one part that cannot be
  reconstructed later.
- **Offline POS** — a browser till stops working during an internet outage.
  Decide before Phase 6 whether to accept that or build a local-queueing client.
- **A vector logo (SVG) is needed** before the storefront ships; the current
  asset is a JPEG with a white background.
- **Formal purchase orders were deliberately skipped**, not forgotten. Quick
  Purchase already posts stock correctly, and a two-partner business buying from
  local wholesalers gains nothing from an order-then-receive ceremony. Build them
  when import lead times start tying up money that needs tracking before it
  arrives — the receipt already supports a nullable `PurchaseOrderId` for that
  day, and the PO route must post through the same `WriteAsync`.
- **Delivery rates are seeded as placeholders** (৳60 inside Dhaka, ৳120
  outside) and are configuration on the company record, not code. They must be
  replaced with what Steadfast actually charges and what the business intends to
  charge before the site takes real orders. Free-delivery-over is off until
  somebody decides the threshold is worth the margin.
- **Cart cleanup is unbuilt.** Abandoned baskets accumulate; `LastTouchedAtUtc`
  is indexed for the sweep, and Hangfire is already in the stack for it. Not
  urgent at this volume, and not something to forget before launch.
- **The storefront ships in English only.** Bengali was considered and
  deliberately deferred to the planned skincare/haircare **blog site**, which
  comes after the ERP and the MAUI apps. The change is additive when it arrives
  — nullable `NameBn` / `DescriptionBn` columns beside the existing ones, plus a
  fallback helper, exactly as `District.NameBn` already works. What must not
  happen in the meantime is a duplicate row per language, which splits stock,
  price and order history and cannot be undone. Keep labels in views, never in
  data, and never branch on the text of a category name.
- **Transfers stay unbuilt while there is one warehouse.** The domain does not
  assume one location, but the screens are not worth writing until there are two.
- **Moving money between pots (cash → bKash → bank) is not built.** It is two
  entries, not one, and `CashKind.Transfer` exists for it. It waits for a bank
  reconciliation to check it against, in Phase 6. Until then a transfer would be
  recorded and never verified.
- **Nothing calculates from `Partner.OwnershipPercent`.** Profit distribution is
  a decision the partners make, not a formula. The capital screen answers "what
  am I in for?" — money in minus money out — and deliberately says nothing about
  what anyone is owed.
