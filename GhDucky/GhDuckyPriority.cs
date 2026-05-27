using System;
using System.Threading;
using GhDucky.Services;
using Grasshopper.Kernel;

namespace GhDucky
{
    /// Ensures all sessions are closed when the host AppDomain is unloading.
    public sealed class GhDuckyPriority : GH_AssemblyPriority
    {
        private static int _shutdownHooksWired;

        public override GH_LoadingInstruction PriorityLoad()
        {
            if (Interlocked.CompareExchange(ref _shutdownHooksWired, 1, 0) == 0)
            {
                AppDomain.CurrentDomain.ProcessExit += OnDomainShutdown;
                AppDomain.CurrentDomain.DomainUnload += OnDomainShutdown;
            }

            return GH_LoadingInstruction.Proceed;
        }

        private static void OnDomainShutdown(object sender, EventArgs e)
        {
            try
            {
                DuckDBConnectionManager.CloseAll();
            }
            catch
            {
                // Best-effort cleanup during process shutdown.
            }
        }
    }
}

