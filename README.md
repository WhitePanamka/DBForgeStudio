# DBForge Studio — Integrated MVP

Windows desktop application built with C# / .NET 8 / WPF and MySQLConnector. The solution combines the main workflows of the original eight-stage roadmap in one integrated project rather than requiring a separate ZIP per stage.

## Open and run
1. Install Visual Studio 2022 with **.NET desktop development** and .NET 8 SDK.
2. Extract this archive.
3. Open `DBForgeStudio.sln`.
4. Wait for NuGet restore, then set `DBForge.App` as Startup Project if needed.
5. Press F5. The app expects MySQL at `localhost:3306` by default.

The MySQL service must be running and the entered account must have the appropriate privileges. The password is not written to the local settings JSON. Connection parameters are saved under `%LOCALAPPDATA%\DBForgeStudio\connection.json`.

## Implemented in this MVP
- **Project basics:** new/open/save project as JSON; schema definitions persist with the project.
- **MySQL connection:** connection test, server version, database list, table list, column metadata.
- **Schema designer:** add/remove entities; edit field names, MySQL types, required/PK/auto-increment/unique flags; configure foreign-key references.
- **Model checks:** identifier/type checks, duplicate fields, reference checks and common constraint validation.
- **SQL generation:** MySQL `CREATE TABLE`, PK, unique constraints, foreign keys; SQL preview and export to `.sql`.
- **SQL apply:** explicit confirmation before executing the generated script against the selected database.
- **Data browser:** inspect up to 500 rows, text search across columns, parameterized insert prompts.
- **Business-rule foundation:** required fields, uniqueness and numeric/foreign-key constraints are represented in the schema model and validated where applicable.
- **Safety baseline:** parameterized values for search/insert; identifiers validated for generated schema; connection password is not saved; explicit confirmation before applying SQL.
- **Sample:** `sample/warehouse_demo.sql` provides a small inventory schema and sample data.

## Scope and known limitations
This is an integrated **MVP**, not a production-ready replacement for MySQL Workbench or a general-purpose low-code platform. Review SQL before applying it. Use a disposable test database first. Current SQL application is intended for generated DDL scripts, not arbitrary scripts with stored procedures, triggers, or semicolons inside strings. The first data browser version supports reading, filtering, and inserting records; row editing/deletion, full schema diff/migration planning, generated standalone CRUD applications, credential vault integration, user/role management, backup/restore automation, and automated CI tests remain follow-up hardening work. Do not point the initial version at production data without independent review and backups.

## Project layout
- `src/DBForge.App` — WPF interface and workflows
- `src/DBForge.Domain` — project/schema model
- `src/DBForge.Application` — application validation result types
- `src/DBForge.Infrastructure` — MySQL integration
- `sample` — demo schema and seed data

## Git
Initialize or reuse the repository after extracting the folder:
```powershell
git init
git add .
git commit -m "Integrate DBForge Studio MVP"
```
