using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GhDucky.Services
{
    /// <summary>
    /// Generic, thread-safe tracker for DuckDB extensions. Each named extension
    /// is installed and loaded at most once per session, with automatic retry on
    /// failure. Extensions can declare prerequisites (e.g. the excel extension
    /// depends on spatial) which are loaded first.
    /// <para>
    /// Individual extension facades (<see cref="SpatialExtension"/>,
    /// <see cref="ExcelExtension"/>) delegate to singleton instances of this
    /// class so the install/load/clear logic lives in one place.
    /// </para>
    /// </summary>
    internal sealed class DuckDbExtensionTracker
    {
        private readonly string _extensionName;
        private readonly DuckDbExtensionTracker[] _prerequisites;

        /// <summary>
        /// Per-session state: <c>true</c> means fully loaded, <c>false</c> means
        /// load is in progress (used as a guard against concurrent re-entry).
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _loaded =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Global registry of every tracker that has been created, so
        /// <see cref="ClearAllForSession"/> can sweep them all in one call.
        /// </summary>
        private static readonly List<DuckDbExtensionTracker> AllTrackers = new();
        private static readonly object RegistryLock = new();

        /// <summary>
        /// Creates a tracker for the given DuckDB extension name (e.g.
        /// <c>"spatial"</c>, <c>"excel"</c>). Any <paramref name="prerequisites"/>
        /// are ensured before this extension is installed/loaded.
        /// </summary>
        public DuckDbExtensionTracker(string extensionName, params DuckDbExtensionTracker[] prerequisites)
        {
            _extensionName = extensionName ?? throw new ArgumentNullException(nameof(extensionName));
            _prerequisites = prerequisites ?? Array.Empty<DuckDbExtensionTracker>();

            lock (RegistryLock)
            {
                AllTrackers.Add(this);
            }
        }

        /// <summary>
        /// Ensures the extension (and its prerequisites) are installed and loaded
        /// on the given session. No-op if already loaded.
        /// </summary>
        public void Ensure(DuckDBSession session)
        {
            if (session == null) return;
            if (_loaded.TryGetValue(session.Id, out var ready) && ready) return;

            // Load prerequisites first (each is a no-op if already loaded).
            foreach (var pre in _prerequisites)
                pre.Ensure(session);

            session.Execute(conn =>
            {
                // TryAdd is atomic — only the thread that wins performs INSTALL/LOAD.
                if (!_loaded.TryAdd(session.Id, false))
                    return;

                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"INSTALL {_extensionName}; LOAD {_extensionName};";
                    cmd.ExecuteNonQuery();

                    _loaded[session.Id] = true;
                }
                catch
                {
                    // Roll back the marker so a future call can retry.
                    _loaded.TryRemove(session.Id, out _);
                    throw;
                }
            });
        }

        /// <summary>
        /// Removes the cached entry for a session from this tracker.
        /// </summary>
        public void ClearSession(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId))
                _loaded.TryRemove(sessionId, out _);
        }

        /// <summary>
        /// Clears cached extension state for the given session across every
        /// tracker that has been created. Called from
        /// <see cref="DuckDBConnectionManager.Close"/> so that closing a
        /// session does not leave stale entries behind.
        /// </summary>
        public static void ClearAllForSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            lock (RegistryLock)
            {
                foreach (var tracker in AllTrackers)
                    tracker.ClearSession(sessionId);
            }
        }
    }
}

