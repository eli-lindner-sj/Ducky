# 🦆 Ducky — DuckDB for Grasshopper

**Ducky** brings the power of [DuckDB](https://duckdb.org/) to [Grasshopper](https://www.grasshopper3d.com/). Open in-memory or file-backed databases, load CSV/JSON/Parquet/Excel files and Grasshopper data trees, query everything in SQL, export results, and round-trip Rhino geometry through the DuckDB spatial extension.

[![Rhino 8](https://img.shields.io/badge/Rhino-8-blue)](https://www.rhino3d.com/)
[![Grasshopper](https://img.shields.io/badge/Grasshopper-8.0-green)](https://www.grasshopper3d.com/)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![DuckDB](https://img.shields.io/badge/DuckDB-1.5.3-yellow)](https://duckdb.org/)

---

## Features

- **In-memory & file-backed databases** — spin up a throwaway analytics session or persist data to a `.duckdb` file.
- **Import CSV, JSON, Parquet, Excel** — leverage DuckDB's blazing-fast native readers (`read_csv_auto`, `read_json_auto`, `read_parquet`) and the `spatial`/`excel` extensions for `.xlsx` files.
- **Import Grasshopper data trees** — push branch data directly into tables via the high-performance Appender API with automatic type inference.
- **SQL queries** — run any SQL against your data; results come back as Grasshopper data trees with typed columns.
- **Export** — write query results or tables to CSV, JSON, Parquet, or Excel with a single component.
- **Spatial / Geometry round-trip** — import Rhino points, curves, meshes, and breps as WKB-encoded GEOMETRY columns via the DuckDB `spatial` extension, then query them back with `ST_*` functions.

---

## Components

### 1 · Connect

| Component | Nickname | Description |
|---|---|---|
| **Ducky Connect** | `DuckyOn` | Opens or creates a database session. Leave the source empty for in-memory; supply a file path for a persistent database. |
| **Ducky Disconnect** | `DuckyOff` | Closes a session and releases file locks. In-memory databases are discarded. |
| **Ducky Enable Spatial** | `DuckySpatial` | Installs and loads the DuckDB `spatial` extension on the connection. |
| **Ducky Enable Excel** | `DuckyExcel` | Installs and loads the DuckDB `spatial` and `excel` extensions for `.xlsx` support in manual SQL queries. Import/Export Excel components auto-enable these. |

### 2 · Import

| Component | Nickname | Description                                                                                   |
|---|---|-----------------------------------------------------------------------------------------------|
| **Ducky Import Data Tree** | `DuckyTree` | Imports a Grasshopper data tree into a table. Each branch is one column; items are row values. |
| **Ducky Import CSV** | `DuckyCSV` | Imports a CSV file into a table using `read_csv_auto`.                                        |
| **Ducky Import JSON** | `DuckyJSON` | Imports a JSON file (array or newline-delimited) into a table using `read_json_auto`.         |
| **Ducky Import Parquet** | `DuckyPQ` | Imports one or more Parquet files (supports globs) into a table.                              |
| **Ducky Import Excel** | `DuckyXLSX` | Imports an Excel (.xlsx) file into a table via the `spatial`/`excel` extensions. Supports sheet selection and headers. |
| **Ducky Import Geometry** | `DuckyGeo` | Imports Rhino geometry to a table as a WKB-encoded GEOMETRY column.                           |

### 3 · Query

| Component | Nickname | Description |
|---|---|---|
| **Ducky Inspect** | `DuckyPeek` | Lists tables, columns, types, and row counts for a database connection. |
| **Ducky Query** | `DuckyQ` | Executes a SQL query and returns the result as a data tree (one branch per column). |
| **Ducky Query Geometry** | `DuckyGeoQ` | Executes a SQL query that includes a GEOMETRY column and reconstructs Rhino geometry from WKB. |
| **Ducky Query Builder** | `DuckyQB` | Builds a SQL SELECT query from simple inputs — no SQL knowledge required. |
| **Ducky Filter** | `DuckyFlt` | Creates a filter condition for the Query Builder's Filters input. |
| **Ducky Join Tables** | `DuckyJoin` | Combines rows from two tables based on a shared column (JOIN). |

### 4 · Export

| Component | Nickname | Description |
|---|---|---|
| **Ducky Export** | `DuckyEx` | Exports a table or query result to CSV, JSON, or Parquet via DuckDB's `COPY TO`. |
| **Ducky Export Excel** | `DuckyExXL` | Exports a table or query result to an Excel (.xlsx) file via the `spatial`/`excel` extensions. |

---

## Getting Started

### Prerequisites

- [Rhino 8](https://www.rhino3d.com/) (Windows)
- .NET 8.0 SDK (for building from source)

### Install via Yak (Package Manager)

> search for **Ducky** in Rhino's package manager.

### Build from Source

```bash
git clone https://github.com/mitchell-tesch/Ducky.git
cd GhDucky
dotnet build
```

The build produces `Ducky.gha` in `GhDucky/bin/Debug/net8.0/`. Copy the entire output folder to your Grasshopper libraries directory:

```
%APPDATA%\Grasshopper\Libraries\Ducky\
```

> **Tip:** The build also auto-generates a Yak package if `Yak.exe` is found in your Rhino 8 installation.

### First Use

1. Drop a **Ducky Connect** component onto the canvas.
2. Set *Connect?* to `True` — this opens an in-memory database.
3. Wire the **Database** output into an import component (e.g. **Ducky Import CSV**).
4. Point the import at a file, set *Import?* to `True`.
5. Wire the **Database** output into a **Ducky Query** component, write your SQL, and set *Run?* to `True`.

---

## Geometry Support

GhDucky can write Rhino geometry to DuckDB's `GEOMETRY` type via WKB encoding and read it back. The spatial extension is loaded using the **Ducky Enable Spatial** component.

### Supported Geometry (Write → WKB)

| Rhino Type | WKB Type |
|---|---|
| `Point3d` / `Point` | POINT Z |
| `LineCurve` / `PolylineCurve` / `Polyline` | LINESTRING Z |
| Other `Curve` | LINESTRING Z (tessellated) |
| `Mesh` | MULTIPOLYGON Z (one polygon per face) |
| `Brep` | MULTIPOLYGON Z (auto-meshed) |

### Supported Geometry (WKB → Read)

| WKB Type | Rhino Type |
|---|---|
| POINT | `Point` |
| LINESTRING | `PolylineCurve` |
| POLYGON | `Mesh` (outer ring; holes ignored with warning) |
| MULTIPOLYGON | `Mesh` (combined) |
| MULTIPOINT | `PointCloud` (all points preserved) |
| MULTILINESTRING | `PolylineCurve` (segments combined with warning) |

---

## Project Structure

```
GhDucky/
├── Components/
│   ├── DuckyComponentBase.cs      # Shared base class
│   ├── Connect/                   # Connect, Disconnect, Spatial, Excel
│   ├── Import/                    # CSV, JSON, Parquet, Excel, DataTree, Geometry
│   ├── Query/                     # Inspect, Query, QueryGeometry, QueryBuilder, Filter, Join
│   └── Export/                    # Export, ExportExcel
├── Services/
│   ├── DuckDBConnectionManager.cs # Process-wide session registry
│   ├── DuckDBSession.cs           # Thread-safe connection wrapper
│   ├── NativeLibraryResolver.cs   # Resolves native DuckDB library for Grasshopper
│   ├── SpatialExtension.cs        # Spatial extension facade
│   ├── ExcelExtension.cs          # Excel extension facade (depends on spatial)
│   └── DuckDbExtensionTracker.cs  # Generic extension install/load/clear tracker
├── Utils/
│   ├── DuckyColumnType.cs         # SQL column type enum
│   ├── ExceptionFormatter.cs      # User-friendly exception messages
│   ├── GH_DuckDBConnection.cs     # Grasshopper Goo wrapper for sessions
│   ├── IconFactory.cs             # Emoji-based component icon generator
│   ├── SqlIdentifier.cs           # SQL identifier quoting / injection prevention
│   ├── TypeMapping.cs             # CLR ↔ DuckDB ↔ Grasshopper type conversion
│   └── WkbCodec.cs                # ISO WKB encoder/decoder for Rhino geometry
├── Parameters/
│   └── ParamDuckyDBConnection.cs  # Custom Grasshopper parameter type
├── GhDuckyPriority.cs             # Assembly priority / cleanup on unload
└── GhDuckyInfo.cs                 # Assembly metadata
```

---

## Dependencies

| Package | Version        | Purpose |
|---|----------------|---|
| [Grasshopper](https://www.nuget.org/packages/Grasshopper/) | 8.0.23304.9001 | Rhino/Grasshopper SDK |
| [DuckDB.NET.Data.Full](https://www.nuget.org/packages/DuckDB.NET.Data.Full/) | 1.5.3          | DuckDB ADO.NET provider + native binaries |
| [System.Drawing.Common](https://www.nuget.org/packages/System.Drawing.Common/) | 8.0.27         | Icon rendering (GDI+) |

---

## Tests

Pure-logic unit tests live in `GhDucky.Tests/` (xUnit, net8.0). They cover
`SqlIdentifier`, the non-Grasshopper parts of `TypeMapping`, and the WKB
encoder in `WkbCodec`. Run from the repo root:

```bash
dotnet test Ducky.slnx
```

See `GhDucky.Tests/README.md` for what's intentionally out of scope (decode
paths and component-level integration require a running Rhino host).

---

## Contributing

Contributions are welcome! Please open an issue or pull request on [GitHub](https://github.com/mitchell-tesch/Ducky).

---

## License

Copyright © GhDucky Contributors. See [LICENSE](LICENSE) for details.

## Attributions

Ducky Icon: <a href="https://www.flaticon.com/free-icons/rubber-duck" title="rubber duck icons">Rubber duck icons created by Talha Dogar - Flaticon</a>
