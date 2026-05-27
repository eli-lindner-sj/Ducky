namespace GhDucky.Utils
{
    /// <summary>
    /// SQL column types that the Ducky import path understands. Restricted to
    /// the types the DuckDB Appender can accept directly through
    /// <see cref="TypeMapping.CoerceForAppender"/>. Anything outside this set
    /// is treated as <see cref="Varchar"/> and routed through string coercion.
    /// </summary>
    public enum DuckyColumnType
    {
        Boolean,
        Integer,
        BigInt,
        Double,
        Timestamp,
        Varchar,
    }

    public static class DuckyColumnTypeExtensions
    {
        /// <summary>
        /// Returns the canonical DuckDB SQL keyword for this type
        /// (e.g. <see cref="DuckyColumnType.BigInt"/> → <c>"BIGINT"</c>).
        /// </summary>
        public static string ToSqlText(this DuckyColumnType type) => type switch
        {
            DuckyColumnType.Boolean   => "BOOLEAN",
            DuckyColumnType.Integer   => "INTEGER",
            DuckyColumnType.BigInt    => "BIGINT",
            DuckyColumnType.Double    => "DOUBLE",
            DuckyColumnType.Timestamp => "TIMESTAMP",
            _ => "VARCHAR",
        };

        /// <summary>
        /// Parses a user-supplied SQL type name (case-insensitive, accepts a
        /// few common aliases) into <see cref="DuckyColumnType"/>.
        /// Returns <c>false</c> for null / whitespace / unknown values and sets
        /// <paramref name="type"/> to <see cref="DuckyColumnType.Varchar"/>.
        /// </summary>
        public static bool TryParse(string text, out DuckyColumnType type)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                switch (text.Trim().ToUpperInvariant())
                {
                    case "BOOL":
                    case "BOOLEAN":
                        type = DuckyColumnType.Boolean;
                        return true;

                    case "INT":
                    case "INT4":
                    case "INTEGER":
                    case "TINYINT":
                    case "SMALLINT":
                        type = DuckyColumnType.Integer;
                        return true;

                    case "INT8":
                    case "LONG":
                    case "BIGINT":
                    case "HUGEINT":
                        type = DuckyColumnType.BigInt;
                        return true;

                    case "REAL":
                    case "FLOAT":
                    case "FLOAT4":
                    case "FLOAT8":
                    case "DOUBLE":
                    case "DECIMAL":
                        type = DuckyColumnType.Double;
                        return true;

                    case "DATETIME":
                    case "TIMESTAMP":
                    case "TIMESTAMP_NS":
                    case "TIMESTAMP_MS":
                    case "TIMESTAMP_S":
                        type = DuckyColumnType.Timestamp;
                        return true;

                    case "TEXT":
                    case "STRING":
                    case "VARCHAR":
                        type = DuckyColumnType.Varchar;
                        return true;
                }
            }

            type = DuckyColumnType.Varchar;
            return false;
        }

        /// <summary>
        /// Convenience wrapper around <see cref="TryParse(string, out DuckyColumnType)"/>
        /// that returns <see cref="DuckyColumnType.Varchar"/> for unknown input.
        /// </summary>
        public static DuckyColumnType ParseOrVarchar(string text)
        {
            TryParse(text, out var t);
            return t;
        }
    }
}

