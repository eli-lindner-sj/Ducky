using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GhDucky.Utils;
using Grasshopper.Kernel;

namespace GhDucky.Components.Query
{
    public class QueryBuilderComponent : GH_Component
    {
        public QueryBuilderComponent()
            : base(
                "Ducky Query Builder",
                "DuckyQB",
                "Builds a SQL SELECT query from simple inputs. " +
                "Connect the output to the Ducky Query component's Query input. " +
                "No SQL knowledge required!",
                "Ducky",
                "3 | Query")
        {
        }

        public override Guid ComponentGuid => new Guid("a7f3c812-54d9-4e8b-b1a3-6d9e0f4c7b2a");

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => IconFactory.Build("🛠", IconFactory.Query);

        private int _inTable;
        private int _inColumns;
        private int _inWhere;
        private int _inFilterMode;
        private int _inGroupBy;
        private int _inAggregate;
        private int _inHaving;
        private int _inOrderBy;
        private int _inDescending;
        private int _inLimit;
        private int _inOffset;
        private int _inDistinct;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            _inTable = pManager.AddTextParameter("Table", "T",
                "Name of the table to query (e.g. \"my_table\"). " +
                "You can also use schema-qualified names like \"main.my_table\".",
                GH_ParamAccess.item);

            _inColumns = pManager.AddTextParameter("Columns", "C",
                "Column names to include in the result (e.g. \"name\", \"age\"). " +
                "Leave empty or unconnected to select all columns (*).",
                GH_ParamAccess.list);

            _inWhere = pManager.AddTextParameter("Filters", "F",
                "Filter conditions that rows must satisfy. Each item is a condition, e.g.:\n" +
                "  • \"age > 30\"\n" +
                "  • \"name = 'Alice'\"\n" +
                "  • \"population BETWEEN 1000 AND 5000\"\n" +
                "  • \"city IS NOT NULL\"\n" +
                "Multiple filters are combined using the Filter Mode (AND or OR).\n" +
                "Tip: Use the Ducky Filter component to build these without SQL!",
                GH_ParamAccess.list);

            _inFilterMode = pManager.AddTextParameter("Filter Mode", "FM",
                "How to combine multiple filters:\n" +
                "  • \"AND\" (default) — ALL conditions must be true\n" +
                "  • \"OR\" — ANY condition can be true",
                GH_ParamAccess.item, "AND");

            _inGroupBy = pManager.AddTextParameter("Group By", "G",
                "Column names to group results by (for aggregation). " +
                "When grouping, non-grouped columns should use an Aggregate function.",
                GH_ParamAccess.list);

            _inAggregate = pManager.AddTextParameter("Aggregates", "A",
                "Aggregate expressions added to the SELECT when grouping, e.g.:\n" +
                "  • \"COUNT(*)\"\n" +
                "  • \"SUM(population)\"\n" +
                "  • \"AVG(score)\"\n" +
                "  • \"MIN(date)\"\n" +
                "  • \"MAX(price)\"",
                GH_ParamAccess.list);

            _inHaving = pManager.AddTextParameter("Having", "H",
                "Filter conditions applied AFTER grouping (only useful with Group By). " +
                "Example: \"COUNT(*) > 5\" to keep only groups with more than 5 items.",
                GH_ParamAccess.list);

            _inOrderBy = pManager.AddTextParameter("Order By", "O",
                "Column names to sort the results by. Multiple columns are applied in order.",
                GH_ParamAccess.list);

            _inDescending = pManager.AddBooleanParameter("Descending?", "D?",
                "If true, sort order is descending (largest/newest first). " +
                "If false (default), order is ascending (smallest/oldest first).",
                GH_ParamAccess.item, false);

            _inLimit = pManager.AddIntegerParameter("Limit", "L",
                "Maximum number of rows to return. 0 means no limit (return all rows).",
                GH_ParamAccess.item, 0);

            _inOffset = pManager.AddIntegerParameter("Offset", "Of",
                "Number of rows to skip before returning results. " +
                "Useful for pagination (e.g. Offset=100 with Limit=50 gives rows 101–150).",
                GH_ParamAccess.item, 0);

            _inDistinct = pManager.AddBooleanParameter("Distinct?", "U?",
                "If true, duplicate rows are removed from the result.",
                GH_ParamAccess.item, false);

            // All inputs except Table are optional.
            pManager[_inColumns].Optional = true;
            pManager[_inWhere].Optional = true;
            pManager[_inFilterMode].Optional = true;
            pManager[_inGroupBy].Optional = true;
            pManager[_inAggregate].Optional = true;
            pManager[_inHaving].Optional = true;
            pManager[_inOrderBy].Optional = true;
            pManager[_inDescending].Optional = true;
            pManager[_inLimit].Optional = true;
            pManager[_inOffset].Optional = true;
            pManager[_inDistinct].Optional = true;
        }

        private int _outQuery;
        private int _outExplanation;

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            _outQuery = pManager.AddTextParameter("Query", "Q",
                "The generated SQL query. Connect this to the Ducky Query component.",
                GH_ParamAccess.item);

            _outExplanation = pManager.AddTextParameter("Explanation", "E",
                "A plain-English explanation of what the query does.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            // --- Table (required) ---
            var table = string.Empty;
            if (!da.GetData(_inTable, ref table) || string.IsNullOrWhiteSpace(table))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A table name is required.");
                return;
            }

            // --- Columns ---
            var columns = new List<string>();
            da.GetDataList(_inColumns, columns);
            columns = columns.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();

            // --- Filters ---
            var filters = new List<string>();
            da.GetDataList(_inWhere, filters);
            filters = filters.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();

            // --- Filter Mode ---
            var filterMode = "AND";
            da.GetData(_inFilterMode, ref filterMode);
            filterMode = (filterMode ?? "AND").Trim().ToUpperInvariant();
            if (filterMode != "OR") filterMode = "AND";

            // --- Group By ---
            var groupBy = new List<string>();
            da.GetDataList(_inGroupBy, groupBy);
            groupBy = groupBy.Where(g => !string.IsNullOrWhiteSpace(g)).ToList();

            // --- Aggregates ---
            var aggregates = new List<string>();
            da.GetDataList(_inAggregate, aggregates);
            aggregates = aggregates.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();

            // --- Having ---
            var having = new List<string>();
            da.GetDataList(_inHaving, having);
            having = having.Where(h => !string.IsNullOrWhiteSpace(h)).ToList();

            // --- Order By ---
            var orderBy = new List<string>();
            da.GetDataList(_inOrderBy, orderBy);
            orderBy = orderBy.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();

            // --- Descending ---
            var descending = false;
            da.GetData(_inDescending, ref descending);

            // --- Limit ---
            var limit = 0;
            da.GetData(_inLimit, ref limit);

            // --- Offset ---
            var offset = 0;
            da.GetData(_inOffset, ref offset);

            // --- Distinct ---
            var distinct = false;
            da.GetData(_inDistinct, ref distinct);

            // ===== Build the SQL =====
            var sql = new StringBuilder();
            var explanation = new StringBuilder();

            // SELECT clause
            sql.Append("SELECT ");
            if (distinct) sql.Append("DISTINCT ");

            var selectParts = new List<string>();
            if (groupBy.Count > 0)
            {
                // When grouping, select the grouped columns plus any aggregates.
                selectParts.AddRange(groupBy.Select(QuoteIdentifier));
                selectParts.AddRange(aggregates);

                if (selectParts.Count == 0)
                    selectParts.Add("*");
            }
            else if (columns.Count > 0)
            {
                selectParts.AddRange(columns.Select(QuoteIdentifier));
            }
            else
            {
                selectParts.Add("*");
            }

            sql.Append(string.Join(", ", selectParts));

            // FROM clause
            sql.Append("\nFROM ");
            sql.Append(QuoteTableReference(table));

            // WHERE clause
            if (filters.Count > 0)
            {
                var connector = $"\n  {filterMode} ";
                sql.Append("\nWHERE ");
                sql.Append(string.Join(connector, filters));
            }

            // GROUP BY clause
            if (groupBy.Count > 0)
            {
                sql.Append("\nGROUP BY ");
                sql.Append(string.Join(", ", groupBy.Select(QuoteIdentifier)));
            }

            // HAVING clause
            if (having.Count > 0 && groupBy.Count > 0)
            {
                sql.Append("\nHAVING ");
                sql.Append(string.Join("\n  AND ", having));
            }

            // ORDER BY clause
            if (orderBy.Count > 0)
            {
                sql.Append("\nORDER BY ");
                var orderParts = orderBy.Select(col =>
                    QuoteIdentifier(col) + (descending ? " DESC" : " ASC"));
                sql.Append(string.Join(", ", orderParts));
            }

            // LIMIT clause
            if (limit > 0)
            {
                sql.Append("\nLIMIT ");
                sql.Append(limit);
            }

            // OFFSET clause
            if (offset > 0)
            {
                sql.Append("\nOFFSET ");
                sql.Append(offset);
            }

            // ===== Build explanation =====
            explanation.Append("Get ");
            if (distinct) explanation.Append("unique ");

            if (groupBy.Count > 0)
            {
                explanation.Append(aggregates.Count > 0
                    ? string.Join(", ", aggregates)
                    : "all columns");
                explanation.Append($" grouped by {string.Join(", ", groupBy)}");
            }
            else if (columns.Count > 0)
            {
                explanation.Append(columns.Count <= 5
                    ? string.Join(", ", columns)
                    : $"{columns.Count} columns");
            }
            else
            {
                explanation.Append("all columns");
            }

            explanation.Append($" from table \"{table}\"");

            if (filters.Count > 0)
            {
                var modeWord = filterMode == "OR" ? " or " : " and ";
                explanation.Append($" where {string.Join(modeWord, filters)}");
            }

            if (having.Count > 0 && groupBy.Count > 0)
                explanation.Append($", keeping only groups where {string.Join(" and ", having)}");

            if (orderBy.Count > 0)
                explanation.Append($", sorted by {string.Join(", ", orderBy)}" +
                                   (descending ? " (descending)" : " (ascending)"));

            if (limit > 0)
                explanation.Append($", returning at most {limit} rows");

            if (offset > 0)
                explanation.Append($", skipping the first {offset} rows");

            explanation.Append(".");

            // ===== Output =====
            da.SetData(_outQuery, sql.ToString());
            da.SetData(_outExplanation, explanation.ToString());
        }

        /// <summary>
        /// Quotes a column identifier safely for SQL.
        /// If it already looks like an expression (contains parentheses, spaces, or operators),
        /// it is passed through as-is to allow things like "COUNT(*)" or "price * qty".
        /// </summary>
        private static string QuoteIdentifier(string name)
        {
            var trimmed = name.Trim();
            // Pass through expressions/aggregates that contain SQL syntax characters.
            if (trimmed.Contains('(') || trimmed.Contains(')') ||
                trimmed.Contains(' ') || trimmed.Contains('*') ||
                trimmed.Contains('+') || trimmed.Contains('-'))
            {
                return trimmed;
            }

            return SqlIdentifier.Quote(trimmed);
        }

        /// <summary>
        /// Handles table references that may be schema-qualified ("schema.table")
        /// or plain table names.
        /// </summary>
        private static string QuoteTableReference(string tableRef)
        {
            var trimmed = tableRef.Trim();
            // If it contains a dot, treat as schema.table
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

