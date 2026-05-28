using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GhDucky.Services
{
    /// Registers a per-assembly DllImportResolver for DuckDB.NET.Bindings so the
    /// native DuckDB library is located relative to this .gha, not relative to
    /// the host process (Rhino). Without this, loading any DuckDB.NET type fails
    /// inside Grasshopper because the default NativeProbe is anchored at
    /// Rhino's directory and never sees our shipped runtimes\<rid>\native folder.
    internal static class NativeLibraryResolver
    {
        private const string DuckDbLibraryName = "duckdb";
        private static int _initialized;

#pragma warning disable CA2255 // ModuleInitializer is the intended bootstrap point for native-library resolution in a Grasshopper plug-in.
        [ModuleInitializer]
        internal static void Initialize()
        {
            // Atomically claim the initialization slot.  If another thread beat us
            // to it, _initialized will already be 1 and we return immediately.
            if (System.Threading.Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
                return;

            try
            {
                var bindingsAssembly = LocateBindingsAssembly();
                if (bindingsAssembly == null)
                {
                    // Reset so a future call can retry once the assembly is loaded.
                    System.Threading.Volatile.Write(ref _initialized, 0);
                    return;
                }

                NativeLibrary.SetDllImportResolver(bindingsAssembly, Resolve);
            }
            catch (InvalidOperationException)
            {
                // SetDllImportResolver throws InvalidOperationException if a resolver
                // is already registered for that assembly. Treat that as success.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"NativeLibraryResolver: Failed to initialize. {ex}");
                // Other failures: reset so a later call can retry.
                System.Threading.Volatile.Write(ref _initialized, 0);
            }
        }
#pragma warning restore CA2255

        private static Assembly LocateBindingsAssembly()
        {
            // Try direct load first (fast path).
            try { return Assembly.Load(new AssemblyName("DuckDB.NET.Bindings")); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"NativeLibraryResolver: Direct load of DuckDB.NET.Bindings failed. {ex.Message}");
                /* fall through */
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(asm.GetName().Name, "DuckDB.NET.Bindings", StringComparison.Ordinal))
                    return asm;
            }
            return null;
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, DuckDbLibraryName, StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            var pluginDir = Path.GetDirectoryName(typeof(NativeLibraryResolver).Assembly.Location);
            if (string.IsNullOrEmpty(pluginDir))
                return IntPtr.Zero;

            var nativeFileName = GetNativeFileName();
            foreach (var rid in GetRidCandidates())
            {
                var candidate = Path.Combine(pluginDir, "runtimes", rid, "native", nativeFileName);
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                    return handle;
            }

            // Final fallback: same directory as the .gha.
            var flat = Path.Combine(pluginDir, nativeFileName);
            if (File.Exists(flat) && NativeLibrary.TryLoad(flat, out var flatHandle))
                return flatHandle;

            return IntPtr.Zero;
        }

        private static string[] GetRidCandidates()
        {
            var arch = RuntimeInformation.OSArchitecture;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return arch == Architecture.Arm64
                    ? new[] { "win-arm64", "win-x64" }
                    : new[] { "win-x64", "win-arm64" };
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return arch == Architecture.Arm64
                    ? new[] { "osx-arm64", "osx", "osx-x64" }
                    : new[] { "osx-x64", "osx", "osx-arm64" };
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return arch == Architecture.Arm64
                    ? new[] { "linux-arm64", "linux-x64" }
                    : new[] { "linux-x64", "linux-arm64" };
            }

            return new[] { "win-x64" };
        }

        private static string GetNativeFileName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "duckdb.dll";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "libduckdb.dylib";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "libduckdb.so";
            return "duckdb.dll";
        }
    }
}
