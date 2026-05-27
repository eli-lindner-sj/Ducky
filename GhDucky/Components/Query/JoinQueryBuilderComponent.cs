using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GhDucky.Utils;
using Grasshopper.Kernel;

namespace GhDucky.Components.Query
{
    public class JoinQueryBuilderComponent : GH_Component
    {
        public JoinQueryBuilderComponent()
            : base(
                "Ducky Join Tables",
                "DuckyJoin",
                "Combines rows from two tables based on a shared column (join). " +
                "Connect the output to the Ducky Query component's Query input. No SQL knowledge required!\n\n" +
                "Join types:\n" +
                "  • Inner — only rows that match in both tables\n" +
                "  • Left — all rows from Table A, matched rows from Table B (or empty)\n" +
                "  • Right — all rows from Table B, matched rows from Table A (or empty)\n" +
                "  • Full — all rows from both tables, filling gaps with empty",
                "Ducky",
                "3 | Query")
        {
        }

        public override Guid ComponentGuid => new Guid("c4e7a9b1-6d3f-4a82-b5c0-8f1e2d7a4c93");

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => IconFactory.Build("🔗", IconFactory.Query);

        private int _inTableA;
        private int _inTableB;
        private int _inColumnA;
        private int _inColumnB;
        private int _inJoinType;
        private int _inSelectColumns;
        private int _inFilters;
        private int _inLimit;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            _inTableA = pManager.AddTextParameter("Table A", "A",
                "The first (left) table name.",
                GH_ParamAccess.item);

            _inTableB = pManager.AddTextParameter("Table B", "B",
                "The second (right) table name.",
                GH_ParamAccess.item);

            _inColumnA = pManager.AddTextParameter("Key A", "KA",
                "The column in Table A that links to Table B (e.g. \"id\", \"city_name\"). " +
                "This is the shared column that connects the two tables.",
                GH_ParamAccess.item);

            _inColumnB = pManager.AddTextParameter("Key B", "KB",
                "The column in Table B that matches Key A. " +
                "If the same column name exists in both tables, you can leave this empty.",
                GH_ParamAccess.item, string.Empty);

            _inJoinType = pManager.AddTextParameter("Join Type", "J",
                "How to combine the tables:\n" +
                "  • \"inner\" — only rows that match in BOTH tables (default)\n" +
                "  • \"left\" — ALL rows from Table A + matching from B\n" +
                "  • \"right\" — ALL rows from Table B + matching from A\n" +
                "  • \"full\" — ALL rows from both tables",
                GH_ParamAccess.item, "inner");

            _inSelectColumns = pManager.AddTextParameter("Columns", "C",
                "Columns to include in the result. Use \"a.column\" or \"b.column\" to specify which table. " +
                "Leave empty to select all columns from both tables.",
                GH_ParamAccess.list);

            _inFilters = pManager.AddTextParameter("Filters", "F",
                "Optional filter conditions applied after the join (same format as Query Builder Filters).",
                GH_ParamAccess.list);

            _inLimit = pManager.AddIntegerParameter("Limit", "L",
                "Maximum number of rows to return. 0 means no limit.",
                GH_ParamAccess.item, 0);

            pManager[_inColumnB].Optional = true;
            pManager[_inJoinType].Optional = true;
            pManager[_inSelectColumns].Optional = true;
            pManager[_inFilters].Optional = true;
            pManager[_inLimit].Optional = true;
        }

        private int _outQuery;
        private int _outExplanation;

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            _outQuery = pManager.AddTextParameter("Query", "Q",
                "The generated SQL JOIN query. Connect to the Ducky Query component.",
                GH_ParamAccess.item);

            _outExplanation = pManager.AddTextParameter("Explanation", "E",
                "A plain-English explanation of what the query does.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            // --- Table A (required) ---
            var tableA = string.Empty;
            if (!da.GetData(_inTableA, ref tableA) || string.IsNullOrWhiteSpace(tableA))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Table A is required.");
                return;
            }

            // --- Table B (required) ---
            var tableB = string.Empty;
            if (!da.GetData(_inTableB, ref tableB) || string.IsNullOrWhiteSpace(tableB))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Table B is required.");
                return;
            }

            // --- Key A (required) ---
            var keyA = string.Empty;
            if (!da.GetData(_inColumnA, ref keyA) || string.IsNullOrWhiteSpace(keyA))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Key A (the linking column) is required.");
                return;
            }

            // --- Key B (optional, defaults to Key A) ---
            var keyB = string.Empty;
            da.GetData(_inColumnB, ref keyB);
            if (string.IsNullOrWhiteSpace(keyB))
                keyB = keyA;

            // --- Join Type ---
            var joinType = "inner";
            da.GetData(_inJoinType, ref joinType);
            joinType = (joinType ?? "inner").Trim().ToLowerInvariant();

            string joinKeyword;
            string joinExplanation;
            switch (joinType)
            {
                case "inner":
                case "inner join":
                    joinKeyword = "INNER JOIN";
                    joinExplanation = "only where both tables match";
                    break;
                case "left":
                case "left join":
                case "left outer":
                    joinKeyword = "LEFT JOIN";
                    joinExplanation = "all rows from Table A, with matching rows from Table B";
                    break;
                case "right":
                case "right join":
                case "right outer":
                    joinKeyword = "RIGHT JOIN";
                    joinExplanation = "all rows from Table B, with matching rows from Table A";
                    break;
                case "full":
                case "full join":
                case "full outer":
                case "outer":
                    joinKeyword = "FULL OUTER JOIN";
                    joinExplanation = "all rows from both tables";
                    break;
                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Unknown join type: \"{joinType}\". Use: inner, left, right, or full.");
                    return;
            }

            // --- Columns ---
            var columns = new List<string>();
            da.GetDataList(_inSelectColumns, columns);
            columns = columns.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();

            // --- Filters ---
            var filters = new List<string>();
            da.GetDataList(_inFilters, filters);
            filters = filters.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();

            // --- Limit ---
            var limit = 0;
            da.GetData(_inLimit, ref limit);

            // ===== Build the SQL =====
            var sql = new StringBuilder();
            sql.Append("SELECT ");

            if (columns.Count > 0)
            {
                sql.Append(string.Join(", ", columns.Select(QuoteColumnRef)));
            }
            else
            {
                sql.Append("*");
            }

            sql.Append("\nFROM ");
            sql.Append(QuoteTableReference(tableA));
            sql.Append(" AS a");

            sql.Append("\n");
            sql.Append(joinKeyword);
            sql.Append(" ");
            sql.Append(QuoteTableReference(tableB));
            sql.Append(" AS b");

            sql.Append("\n  ON a.");
            sql.Append(SqlIdentifier.Quote(keyA.Trim()));
            sql.Append(" = b.");
            sql.Append(SqlIdentifier.Quote(keyB.Trim()));

            if (filters.Count > 0)
            {
                sql.Append("\nWHERE ");
                sql.Append(string.Join("\n  AND ", filters));
            }

            if (limit > 0)
            {
                sql.Append("\nLIMIT ");
                sql.Append(limit);
            }

            // ===== Build explanation =====
            var explanation = new StringBuilder();
            explanation.Append($"Combine \"{tableA}\" and \"{tableB}\" ");
            explanation.Append($"by matching {tableA}.{keyA} to {tableB}.{keyB} ");
            explanation.Append($"({joinExplanation})");

            if (columns.Count > 0)
                explanation.Append($", selecting {columns.Count} columns");

            if (filters.Count > 0)
                explanation.Append($", filtered by {filters.Count} condition(s)");

            if (limit > 0)
                explanation.Append($", limited to {limit} rows");

            explanation.Append(".");

            // ===== Output =====
            da.SetData(_outQuery, sql.ToString());
            da.SetData(_outExplanation, explanation.ToString());
        }

        /// <summary>
        /// Quotes a column reference that may be prefixed with "a." or "b." for table alias.
        /// </summary>
        private static string QuoteColumnRef(string col)
        {
            var trimmed = col.Trim();

            // If it contains SQL expressions, pass through.
            if (trimmed.Contains('(') || trimmed.Contains(')') ||
                trimmed.Contains('*') || trimmed.Contains(' '))
            {
                return trimmed;
            }

            // Handle "a.column" or "b.column" aliased references.
            if ((trimmed.StartsWith("a.", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("b.", StringComparison.OrdinalIgnoreCase)) &&
                trimmed.Length > 2)
            {
                var prefix = trimmed.Substring(0, 2).ToLowerInvariant();
                var colName = trimmed.Substring(2);
                return prefix + SqlIdentifier.Quote(colName);
            }

            return SqlIdentifier.Quote(trimmed);
        }

        private static string QuoteTableReference(string tableRef)
        {
            var trimmed = tableRef.Trim();
            var dotIdx = trimmed.IndexOf('.');
            if (dotIdx > 0 && dotIdx < trimmed.Length - 1)
            {
                var schema = trimmed.Substring(0, dotIdx);
                var table = trimmed.Substring(dotIdx + 1);
                return SqlIdentifier.Quote(schema) + "." + SqlIdentifier.Quote(table);
            }

            return SqlIdentifier.Quote(trimmed);
        }
    }
}

