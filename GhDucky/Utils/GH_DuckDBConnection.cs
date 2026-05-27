using GhDucky.Services;
using Grasshopper.Kernel.Types;

namespace GhDucky.Utils
{
    /// Grasshopper-native wrapper around a DuckDB session reference.
    /// The wrapper itself is immutable and only stores the session id; the
    /// underlying session is owned by <see cref="DuckDBConnectionManager"/>.
    public sealed class GH_DuckDBConnection : GH_Goo<string>
    {
        public GH_DuckDBConnection() { }

        public GH_DuckDBConnection(string sessionId) { Value = sessionId; }

        public GH_DuckDBConnection(DuckDBSession session) { Value = session?.Id; }

        public string SessionId => Value;

        public DuckDBSession Session =>
            DuckDBConnectionManager.TryGet(Value, out var s) ? s : null;

        public override bool IsValid =>
            !string.IsNullOrEmpty(Value) &&
            DuckDBConnectionManager.TryGet(Value, out var s) && s.IsOpen;

        public override string TypeName => "Ducky Database Connection";

        public override string TypeDescription =>
            "A handle to a DuckDB database connection managed by Ducky.";

        public override IGH_Goo Duplicate() => new GH_DuckDBConnection(Value);

        public override string ToString()
        {
            var s = Session;
            if (s == null) return "Ducky Database Connection (closed)";

            return s.IsInMemory
                ? $"DuckyDB :memory: [{s.DisplayName}]"
                : $"DuckyDB file [{s.DisplayName}] @ {s.Source}";
        }

        public override bool CastFrom(object source)
        {
            switch (source)
            {
                case null:
                    return false;
                case GH_DuckDBConnection wrapper:
                    Value = wrapper.Value;
                    return true;
                case DuckDBSession session:
                    Value = session.Id;
                    return true;
                case string id when !string.IsNullOrWhiteSpace(id):
                    Value = id;
                    return true;
                case GH_String s when !string.IsNullOrWhiteSpace(s.Value):
                    Value = s.Value;
                    return true;
                default:
                    return false;
            }
        }

        public override bool CastTo<T>(ref T target)
        {
            if (typeof(T).IsAssignableFrom(typeof(DuckDBSession)))
            {
                var session = Session;
                if (session == null) return false;
                target = (T)(object)session;
                return true;
            }

            if (typeof(T) == typeof(string))
            {
                target = (T)(object)(Value ?? string.Empty);
                return true;
            }

            if (typeof(T) == typeof(GH_String))
            {
                target = (T)(object)new GH_String(Value ?? string.Empty);
                return true;
            }

            return base.CastTo(ref target);
        }

        public override object ScriptVariable() => Session;
    }
}
