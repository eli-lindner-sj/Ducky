using System;

namespace GhDucky.Utils
{
    internal static class SqlIdentifier
    {
        /// Returns a safe double-quoted SQL identifier (table or column name).
        /// Throws if the input is null or empty.
        public static string Quote(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("Identifier may not be empty.", nameof(identifier));

            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }

        /// Returns a single-quoted SQL string literal.
        public static string QuoteLiteral(string value)
        {
            if (value is null) return "NULL";
            return "'" + value.Replace("'", "''") + "'";
        }

        /// Returns a quoted table reference. When <paramref name="schema"/> is empty
        /// or the default ("main"), only the table identifier is emitted.
        public static string QuoteTable(string schema, string table)
        {
            if (string.IsNullOrWhiteSpace(schema) ||
                string.Equals(schema, "main", StringComparison.OrdinalIgnoreCase))
            {
                return Quote(table);
            }
            return Quote(schema) + "." + Quote(table);
        }

        /// Returns true if a schema input represents a non-default schema that
        /// the caller may need to CREATE SCHEMA IF NOT EXISTS before using.
        public static bool IsExplicitSchema(string schema)
        {
            return !string.IsNullOrWhiteSpace(schema) &&
                   !string.Equals(schema, "main", StringComparison.OrdinalIgnoreCase);
        }
    }
}
