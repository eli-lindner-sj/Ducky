using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DuckDB.NET.Data;
using GhDucky.Parameters;
using GhDucky.Services;
using GhDucky.Utils;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace GhDucky.Components.Query
{
    public class InspectComponent : DuckyComponentBase
    {
        public InspectComponent()
            : base(
                "Ducky Inspect",
                "DuckyPeek",
                "Inspects a Database connection and reports tables, columns and row counts. " +
                "Pass Schema=\"*\" (or leave it empty) to scan every user schema.",
                "Ducky",
                "3 | Query")
        {
        }
        
        private int _inDatabase;
        private int _inSchema;
        
        private int _outSummary;
        private int _outTables;
        private int _outColumns;
        private int _outRows;

        public override Guid ComponentGuid => new Guid("b04d2c66-9c1a-4ad3-95e2-5b14a7d9d0a8");

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => IconFactory.Build("🔍", IconFactory.Query);

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            _inDatabase = pManager.AddParameter(new ParamDuckyDbConnection(), "Database", "DB",
                "Database connection.", 
                GH_ParamAccess.item);
            _inSchema = pManager.AddTextParameter("Schema", "S",
                "Schema filter. Defaults to 'main'. Use '*' (or an empty string) to scan all user schemas; " +
                "table names are then emitted as 'schema.table'.",
                GH_ParamAccess.item, "main");
            
            pManager[_inSchema].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            _outSummary = pManager.AddTextParameter("Summary", "S",
                "Summary of the database.", 
                GH_ParamAccess.item);
            _outTables = pManager.AddTextParameter("Tables", "T",
                "Table names. Schema-qualified ('schema.table') when scanning all schemas.",
                GH_ParamAccess.list);
            _outColumns = pManager.AddTextParameter("Columns", "C",
                "Column metadata per table as a tree: each branch contains \"name:type\" entries for that table.",
                GH_ParamAccess.tree);
            _outRows = pManager.AddIntegerParameter("Rows", "R",
                "Row count per table (parallel to Tables).", 
                GH_ParamAccess.list);
        }

        // System schemas we never enumerate in "all schemas" mode.
        private static readonly HashSet<string> SystemSchemas = new(StringComparer.OrdinalIgnoreCase)
        {
            "information_schema",
            "pg_catalog",
        };

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (!TryGetSession(da, _inDatabase, out var session))
                return;

            var schemaInput = "main";
            da.GetData(_inSchema, ref schemaInput);
            var allSchemas = string.IsNullOrWhiteSpace(schemaInput) ||
                             schemaInput.Trim() == "*";

            try
            {
                session.Execute(conn =>
                {
                    // Resolve the set of (schema, table) pairs to inspect.
                    var entries = allSchemas
                        ? ListAllTables(conn)
                        : ListTables(conn, schemaInput);

                    var displayNames = new List<string>(entries.Count);
                    var counts = new List<int>(entries.Count);
                    var columnTree = new GH_Structure<GH_String>();

                    for (var i = 0; i < entries.Count; i++)
                    {
                        var (schema, table) = entries[i];
                        var qualifiedIdent = SqlIdentifier.Quote(schema) + "." + SqlIdentifier.Quote(table);
                        displayNames.Add(allSchemas ? schema + "." + table : table);

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT COUNT(*) FROM " + qualifiedIdent;
                            var result = cmd.ExecuteScalar();
                            counts.Add(result is null or DBNull
                                ? 0
                                : Convert.ToInt32(result, CultureInfo.InvariantCulture));
                        }

                        var path = new GH_Path(i);
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText =
                                "SELECT column_name, data_type FROM information_schema.columns " +
                                $"WHERE table_schema = {SqlIdentifier.QuoteLiteral(schema)} " +
                                $"AND table_name = {SqlIdentifier.QuoteLiteral(table)} " +
                                "ORDER BY ordinal_position";

                            using var reader = cmd.ExecuteReader();
                            while (reader.Read())
                            {
                                var col = reader.GetString(0);
                                var type = reader.GetString(1);
                                columnTree.Append(new GH_String($"{col}:{type}"), path);
                            }
                        }
                    }

                    da.SetData(_outSummary,
                        BuildSummary(session, allSchemas ? "*" : schemaInput, entries, counts, allSchemas));
                    da.SetDataList(_outTables, displayNames);
                    da.SetDataList(_outRows, counts);
                    da.SetDataTree(_outColumns, columnTree);
                });
            }
            catch (Exception ex)
            {
                ReportError("Inspection failed", ex);
            }
        }

        private static List<(string Schema, string Table)> ListTables(DuckDBConnection conn, string schema)
        {
            var list = new List<(string, string)>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT table_name FROM information_schema.tables " +
                $"WHERE table_schema = {SqlIdentifier.QuoteLiteral(schema)} ORDER BY table_name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add((schema, reader.GetString(0)));
            return list;
        }

        private static List<(string Schema, string Table)> ListAllTables(DuckDBConnection conn)
        {
            var list = new List<(string, string)>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT table_schema, table_name FROM information_schema.tables " +
                "ORDER BY table_schema, table_name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var schema = reader.GetString(0);
                if (SystemSchemas.Contains(schema)) continue;
                list.Add((schema, reader.GetString(1)));
            }
            return list;
        }

        
        private static string BuildSummary(
            DuckDBSession session,
            string schemaLabel,
            List<(string Schema, string Table)> entries,
            List<int> counts,
            bool allSchemas)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"DuckDB: {session.DisplayName}");
            sb.AppendLine(session.IsInMemory ? "Kind: in-memory" : "Kind: file: " + session.Source);
            sb.AppendLine($"Schema: {schemaLabel}");
            sb.AppendLine($"Tables: {entries.Count}");
            for (var i = 0; i < entries.Count; i++)
            {
                var name = allSchemas
                    ? entries[i].Schema + "." + entries[i].Table
                    : entries[i].Table;
                sb.AppendLine($"  - {name}  ({counts[i]:N0} rows)");
            }
            return sb.ToString();
        }
    }
}
