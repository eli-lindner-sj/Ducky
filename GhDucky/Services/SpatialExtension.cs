namespace GhDucky.Services
{
    /// <summary>
    /// Convenience facade for the DuckDB <c>spatial</c> extension.
    /// Delegates to a shared <see cref="DuckDbExtensionTracker"/> instance.
    /// </summary>
    internal static class SpatialExtension
    {
        internal static readonly DuckDbExtensionTracker Tracker = new("spatial");

        /// <summary>
        /// Ensures the spatial extension is installed and loaded on the given
        /// session. No-op if already loaded.
        /// </summary>
        public static void Ensure(DuckDBSession session) => Tracker.Ensure(session);
    }
}
