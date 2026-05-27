namespace GhDucky.Services
{
    /// <summary>
    /// Convenience facade for the DuckDB <c>excel</c> extension.
    /// Declares <see cref="SpatialExtension"/> as a prerequisite so
    /// <c>spatial</c> is always loaded before <c>excel</c>.
    /// </summary>
    internal static class ExcelExtension
    {
        internal static readonly DuckDbExtensionTracker Tracker =
            new("excel", SpatialExtension.Tracker);

        /// <summary>
        /// Ensures the spatial and excel extensions are installed and loaded
        /// on the given session. No-op if already loaded.
        /// </summary>
        public static void Ensure(DuckDBSession session) => Tracker.Ensure(session);
    }
}

