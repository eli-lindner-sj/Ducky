using System;
using System.Globalization;
using System.Text;
using DuckDB.NET.Data;
using GhDucky.Services;
using GhDucky.Utils;
using Grasshopper.Kernel;

namespace GhDucky.Components
{
    /// <summary>
    /// Shared base class for Ducky components. Provides convenience helpers
    /// for the patterns that recur in every component: reading and validating
    /// a <see cref="GH_DuckDBConnection"/> input, and formatting caught
    /// exceptions as runtime error messages.
    /// </summary>
    public abstract class DuckyComponentBase : GH_Component
    {
        protected DuckyComponentBase(string name, string nickname, string description,
            string category, string subCategory)
            : base(name, nickname, description, category, subCategory)
        {
        }

        /// <summary>
        /// Reads a <see cref="GH_DuckDBConnection"/> from the specified input
        /// parameter and validates that it references an open session. If
        /// validation fails, an error message is added and <paramref name="session"/>
        /// is <c>null</c>.
        /// </summary>
        /// <returns><c>true</c> if a valid open session was obtained.</returns>
        protected bool TryGetSession(IGH_DataAccess da, int paramIndex, out DuckDBSession session)
        {
            GH_DuckDBConnection goo = null;
            if (!da.GetData(paramIndex, ref goo))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No Database connection supplied.");
                session = null;
                return false;
            }

            if (goo == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No Database connection supplied.");
                session = null;
                return false;
            }

            session = goo.Session;
            if (session == null || !session.IsOpen)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Database connection has been closed.");
                session = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads a <see cref="GH_DuckDBConnection"/> from the specified input,
        /// validates that it references an open session, and also returns the
        /// raw goo wrapper (useful when the component needs to pass it through
        /// to an output).
        /// </summary>
        protected bool TryGetSession(IGH_DataAccess da, int paramIndex,
            out DuckDBSession session, out GH_DuckDBConnection goo)
        {
            goo = null;
            if (!da.GetData(paramIndex, ref goo) || goo == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No Database connection supplied.");
                session = null;
                return false;
            }

            session = goo.Session;
            if (session == null || !session.IsOpen)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Database connection has been closed.");
                session = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Adds a formatted runtime error message for a caught exception,
        /// prefixed with an operation-specific context string
        /// (e.g. "CSV import failed").
        /// </summary>
        protected void ReportError(string context, Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"{context}: {ex}");

            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                context + ": " + ExceptionFormatter.Format(ex));
        }

        /// <summary>
        /// Returns the row count of the specified table. Useful after an import
        /// to report the resulting row count.
        /// </summary>
        protected static int CountRows(DuckDBConnection conn, string quotedTable)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {quotedTable}";
            var result = cmd.ExecuteScalar();
            return result is null or DBNull
                ? 0
                : Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Ensures a schema exists and optionally drops the target table.
        /// Common preamble for import components.
        /// </summary>
        protected static void EnsureSchemaAndDrop(DuckDBConnection conn, string schema, string quotedTable, bool overwrite)
        {
            using var cmd = conn.CreateCommand();
            if (SqlIdentifier.IsExplicitSchema(schema))
            {
                cmd.CommandText = $"CREATE SCHEMA IF NOT EXISTS {SqlIdentifier.Quote(schema)}";
                cmd.ExecuteNonQuery();
            }

            if (overwrite)
            {
                cmd.CommandText = $"DROP TABLE IF EXISTS {quotedTable}";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Returns true if the named table exists in the given schema. Schema
        /// defaults to <c>main</c> when null/empty.
        /// </summary>
        protected static bool TableExists(DuckDBConnection conn, string schema, string table)
        {
            var resolvedSchema = string.IsNullOrWhiteSpace(schema) ? "main" : schema;
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM information_schema.tables " +
                $"WHERE table_schema = {SqlIdentifier.QuoteLiteral(resolvedSchema)} " +
                $"AND table_name = {SqlIdentifier.QuoteLiteral(table)}";
            var n = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            return n > 0;
        }

        /// <summary>
        /// Executes a SELECT-based import (CSV / JSON / Parquet / arbitrary SELECT):
        /// <list type="bullet">
        ///   <item>Creates the target schema if needed.</item>
        ///   <item>If <paramref name="overwrite"/> is true, drops and recreates the table from <paramref name="sourceSelectExpression"/>.</item>
        ///   <item>If <paramref name="overwrite"/> is false and the table does not yet exist, creates it from the source (so first-time appends do not fail).</item>
        ///   <item>Otherwise inserts the source into the existing table.</item>
        /// </list>
        /// Returns the row count of the resulting table.
        /// </summary>
        protected static int RunSelectImport(
            DuckDBConnection conn,
            string schema,
            string table,
            string quotedTable,
            string sourceSelectExpression,
            bool overwrite)
        {
            EnsureSchemaAndDrop(conn, schema, quotedTable, overwrite);

            var shouldCreate = overwrite || !TableExists(conn, schema, table);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = shouldCreate
                    ? $"CREATE TABLE {quotedTable} AS SELECT * FROM {sourceSelectExpression}"
                    : $"INSERT INTO {quotedTable} SELECT * FROM {sourceSelectExpression}";
                cmd.ExecuteNonQuery();
            }

            return CountRows(conn, quotedTable);
        }

        /// <summary>
        /// Wraps a source string for use in a COPY ... TO statement.
        /// SELECT / WITH statements are wrapped in parentheses; plain table
        /// names (optionally schema-qualified) are double-quoted.
        /// </summary>
        protected static string WrapSource(string source)
        {
            // Strip a trailing semicolon so the wrapped form remains valid:
            //   COPY (SELECT ... ;) TO ...  -- DuckDB rejects this.
            var trimmed = source.TrimEnd().TrimEnd(';').TrimEnd();
            var leading = trimmed.TrimStart();

            // SELECT / WITH must be wrapped in parentheses for COPY (...) TO.
            if (leading.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                leading.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) ||
                leading.StartsWith("("))
            {
                return leading.StartsWith("(") ? trimmed : "(" + trimmed + ")";
            }

            // Otherwise treat as a (possibly schema-qualified) identifier.
            var parts = trimmed.Split('.');
            var sb = new StringBuilder();
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append('.');
                sb.Append(SqlIdentifier.Quote(parts[i].Trim()));
            }
            return sb.ToString();
        }
    }
}
