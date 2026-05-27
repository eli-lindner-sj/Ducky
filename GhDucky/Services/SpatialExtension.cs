using System.Collections.Concurrent;

namespace GhDucky.Services
{
    /// Tracks which sessions have already had the spatial extension loaded so we
    /// can avoid issuing INSTALL/LOAD on every component solve.
    internal static class SpatialExtension
    {
        private static readonly ConcurrentDictionary<string, bool> Loaded =
            new(System.StringComparer.Ordinal);

        public static void Ensure(DuckDBSession session)
        {
            if (session == null) return;
            if (Loaded.ContainsKey(session.Id)) return;

            session.Execute(conn =>
            {
                // TryAdd is atomic — only the thread that wins performs INSTALL/LOAD.
                if (!Loaded.TryAdd(session.Id, false))
                    return;

                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "INSTALL spatial; LOAD spatial;";
                    cmd.ExecuteNonQuery();

                    Loaded[session.Id] = true;
                }
                catch
                {
                    // Roll back the marker so a future call can retry.
                    Loaded.TryRemove(session.Id, out _);
                    throw;
                }
            });
        }

        /// <summary>
        /// Removes the cached entry for a session so that closing and re-opening
        /// a session with a new id does not leave stale entries in the dictionary.
        /// Called from <see cref="DuckDBConnectionManager.Close"/>.
        /// </summary>
        internal static void ClearSession(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId))
                Loaded.TryRemove(sessionId, out _);
        }
    }
}
