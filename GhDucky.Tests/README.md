# GhDucky.Tests

xUnit test project for GhDucky.

## Scope

Tests target the **pure-logic** parts of the plug-in so they run on plain
`dotnet test` without a Rhino host:

| Suite | Covers |
|---|---|
| `SqlIdentifierTests` | Identifier quoting, literal escaping, schema-qualified table names, default-schema detection. |
| `TypeMappingTests`   | `InferColumnType`, `CoerceForAppender`, `ResolveColumnNames`, `ResolveColumnTypes`. |
| `WkbCodecTests`      | Binary format of encoded `Point3d` / `Line` / `Polyline` (header bytes, endianness, type codes, coordinate payload). |

### Intentionally out of scope

- `TypeMapping.ToGoo` / `Unwrap` — require Grasshopper `IGH_Goo` runtime.
- `WkbCodec.Decode` — constructs Rhino types (`Point`, `Mesh`, `PolylineCurve`)
  whose constructors p/invoke into native Rhino and need a running Rhino host.
- Components, `DuckDBConnectionManager`, native library resolver — integration
  surface that is better exercised in Rhino directly.

If you need those, run a Rhino-hosted test runner (e.g. Rhino.Inside) — but the
suites above already cover most of the easy-to-regress logic.

## Running

```bash
dotnet test Ducky.slnx
```

or just this project:

```bash
dotnet test GhDucky.Tests/GhDucky.Tests.csproj
```

