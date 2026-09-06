# Echo Lifestyle ERP

ERP, POS and e-commerce platform for Echo Lifestyle — a Bangladeshi cosmetics,
haircare and skincare retailer.

One database is the source of truth for the back office, the point of sale and
the storefront: products, prices, stock, customers, orders and accounting all
live in the same place, so nothing has to be entered twice or kept in sync.

**Status:** Phase 1 (foundation) complete. See [`CLAUDE.md`](CLAUDE.md) for the
architecture, the rules the codebase is held to, and the roadmap.

---

## Requirements

| | |
|---|---|
| .NET SDK | 10.0.301 or later (pinned in `global.json`) |
| Database | SQL Server 2019+ (Developer or Express edition is fine) |
| OS | Windows, macOS or Linux for development; IIS on Windows Server for production |

Everything else restores from NuGet. `dotnet-ef` is pinned as a local tool, so
no global install is needed.

---

## Getting started

```bash
git clone https://github.com/Evan93/echo-lifestyle-ERP.git
cd echo-lifestyle-ERP

# 1. Confirm your toolchain (reports SDK, SQL instances, git — changes nothing)
cmd //c tools/env-check.bat

# 2. Create the database
./tools/migrate.bat update

# 3. Set the owner passwords (kept outside the repository)
dotnet user-secrets init --project src/EchoLifestyle.Web
dotnet user-secrets set "Seed:Owners:0:Password" "<password>" --project src/EchoLifestyle.Web
dotnet user-secrets set "Seed:Owners:1:Password" "<password>" --project src/EchoLifestyle.Web

# 4. Run
dotnet run --project src/EchoLifestyle.Web
```

Then open `https://localhost:<port>/backoffice/account/login` and sign in as
`evan`.

If you skip step 3, the seeder generates a password per owner in Development and
writes it to the startup log — change it immediately, since it then exists in
`logs/`.

The default connection string expects a local default SQL Server instance
(`Server=localhost`). Override it in `appsettings.Development.json` or with
user-secrets if yours differs.

---

## Build and test

```bash
cmd //c build.bat            # restore, build, test — transcript in tools/build-output.txt
cmd //c build.bat Release
cmd //c build.bat Debug notest
```

Integration tests need a reachable SQL Server. They create and drop
`EchoLifestyle_Tests`; point them elsewhere with `ECHO_TEST_CONNECTION`.

---

## Layout

```
src/
  EchoLifestyle.Domain           entities and business rules — references nothing
  EchoLifestyle.Application      use cases, interfaces, permissions, business calendar
  EchoLifestyle.Infrastructure   EF Core, Identity, seeding, adapters
  EchoLifestyle.Web              MVC, Razor, authorization, composition root
  EchoLifestyle.Worker           background jobs (shell until Phase 4)
tests/
  EchoLifestyle.UnitTests        domain and application logic, no database
  EchoLifestyle.IntegrationTests real SQL Server, plus web-layer security
tools/                           build, migration and environment scripts
```

The dependency direction is enforced by project references: `Domain` cannot
reference EF Core or SQL Server, so business rules stay free of persistence
concerns and testable without a database.

---

## Database

```bash
./tools/migrate.bat add <MigrationName>
./tools/migrate.bat update
./tools/migrate.bat list
./tools/migrate.bat script     # idempotent SQL for a controlled deployment
```

Tables are grouped into schemas by module — `admin`, `security`, `audit` today,
more as modules land. Keys are `bigint` identities, money is `decimal(19,4)`,
timestamps are `datetime2` in UTC, and every business table carries a
`rowversion` for optimistic concurrency.

Migrations are applied automatically **only in Development**. In any other
environment the application logs that migrations are pending and continues —
schema changes belong in the deployment process, not in an app-pool restart.

---

## Conventions worth knowing before you contribute

- **UTC in, Dhaka out.** Timestamps are stored in UTC; business dates come from
  `BusinessCalendar`. An order at 03:00 Dhaka is 21:00 UTC the previous day, so
  grouping by the UTC date would move revenue between days.
- **`UserType` separates staff from customers structurally.** Every back-office
  permission check requires `Staff` in addition to the permission, so a
  mis-assigned role cannot expose cost prices to a shopper.
- **Permissions, not roles.** Authorization is always against
  `Module.Entity.Action` constants in `Permissions`; roles are just bundles of
  them and can be reshaped freely.
- **Stock and money move only through ledgers.** No screen writes a balance
  directly, and posted journals are never edited — they are reversed.

`CLAUDE.md` has the full set, including the rules that must not be broken.

---

## Licence

Proprietary. © Echo Lifestyle.
