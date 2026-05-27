using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace GhDucky.Utils
{
    /// Produces a single-line, debuggable representation of an exception by
    /// walking the InnerException chain. The default ToString output is too
    /// noisy for a Grasshopper runtime message and ex.Message often hides the
    /// real cause (TypeInitializationException, TargetInvocationException, and
    /// wrapped DuckDB native errors all do this).
    internal static class ExceptionFormatter
    {
        public static string Format(Exception ex)
        {
            if (ex == null) return "(no exception)";

            var sb = new StringBuilder();
            var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);

            for (var current = ex; current != null && seen.Add(current); current = current.InnerException)
            {
                if (sb.Length > 0) sb.Append("  ←  ");

                var name = current.GetType().Name;

                switch (current)
                {
                    case TypeInitializationException tie:
                    {
                        sb.Append(name).Append(" [").Append(tie.TypeName ?? "?").Append("]");
                        if (!string.IsNullOrWhiteSpace(current.Message) &&
                            !current.Message.Contains("threw an exception", StringComparison.Ordinal))
                        {
                            sb.Append(": ").Append(current.Message);
                        }

                        break;
                    }
                    case TargetInvocationException:
                        sb.Append(name);
                        break;
                    default:
                        sb.Append(name).Append(": ").Append(current.Message?.Trim());
                        break;
                }
            }

            return sb.ToString();
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<Exception>
        {
            public static readonly ReferenceEqualityComparer Instance = new();
            public bool Equals(Exception x, Exception y) => ReferenceEquals(x, y);
            public int GetHashCode(Exception obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
