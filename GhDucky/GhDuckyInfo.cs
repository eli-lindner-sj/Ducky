using GhDucky.Utils;
using Grasshopper.Kernel;
using System;
using System.Reflection;

namespace GhDucky
{
    public class GhDuckyInfo : GH_AssemblyInfo
    {
        public override string Name => "Ducky";

        public override System.Drawing.Bitmap Icon => IconFactory.Build("🦆", IconFactory.Neutral);

        public override string Description =>
            "DuckDB integration for Grasshopper. Open in-memory or file-backed databases, load " +
            "CSV/JSON/Parquet and Grasshopper data trees, query in SQL, export results, and " +
            "round-trip Rhino geometry through the DuckDB spatial extension.";

        public override Guid Id => new Guid("7a12c97b-08c8-482f-8e48-3dc5a1a896fd");

        public override string AuthorName => "Mitchell Tesch";

        public override string AuthorContact => "https://github.com/mitchell-tesch/Ducky";

        public override string AssemblyVersion =>
            GetType().Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
            ?? GetType().Assembly.GetName().Version?.ToString()
            ?? "0.1.0";
    }
}