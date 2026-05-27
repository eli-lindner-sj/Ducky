using System;
using System.IO;
using GhDucky.Parameters;
using GhDucky.Services;
using GhDucky.Utils;
using Grasshopper.Kernel;

namespace GhDucky.Components.Export
{
    public class ExportExcelComponent : DuckyComponentBase
    {
        public ExportExcelComponent()
            : base(
                "Ducky Export Excel",
                "DuckyExXL",
                "Exports a table or query result to an Excel (.xlsx) file via DuckDB's COPY TO statement " +
                "with the spatial and excel extensions. The Source input accepts either a table name or a full SELECT statement.",
                "Ducky",
                "4 | Export")
        {
        }

        public override Guid ComponentGuid => new Guid("b6d9e4c3-7a5f-4e8b-a0d3-2b4c6e9f8a71");

        protected override System.Drawing.Bitmap Icon => IconFactory.Build("📊", IconFactory.Excel);

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        private int _inExport;
        private int _inDatabase;
        private int _inSource;
        private int _inPath;
        private int _inOverwrite;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            _inExport = pManager.AddBooleanParameter("Export?", "E?",
                "Set to true to perform the export.",
                GH_ParamAccess.item, false);
            _inDatabase = pManager.AddParameter(new ParamDuckyDbConnection(), "Database", "DB",
                "Database connection.",
                GH_ParamAccess.item);
            _inSource = pManager.AddTextParameter("Source", "S",
                "Table name OR a SELECT statement (anything that can be wrapped in COPY (...) TO).",
                GH_ParamAccess.item);
            _inPath = pManager.AddTextParameter("Path", "P",
                "Absolute output file path (.xlsx).",
                GH_ParamAccess.item);
            _inOverwrite = pManager.AddBooleanParameter("Overwrite?", "O?",
                "If true, an existing output file is overwritten.",
                GH_ParamAccess.item, true);
        }

        private int _outDatabase;
        private int _outPath;

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            _outDatabase = pManager.AddParameter(new ParamDuckyDbConnection(), "Database", "DB",
                "Database connection (passthrough).",
                GH_ParamAccess.item);
            _outPath = pManager.AddTextParameter("Path", "P",
                "Output file path.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var export = false;
            da.GetData(_inExport, ref export);
            if (!export)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Export is false; no action taken.");
                return;
            }

            if (!TryGetSession(da, _inDatabase, out var session, out var dbConnection))
                return;

            var source = string.Empty;
            if (!da.GetData(_inSource, ref source) || string.IsNullOrWhiteSpace(source))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Source table name or SELECT statement required.");
                return;
            }

            var path = string.Empty;
            if (!da.GetData(_inPath, ref path) || string.IsNullOrWhiteSpace(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Path for export file required.");
                return;
            }

            var overwrite = true;
            da.GetData(_inOverwrite, ref overwrite);

            var absolutePath = Path.GetFullPath(path);
            if (File.Exists(absolutePath))
            {
                if (!overwrite)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "Output file already exists and Overwrite is false: " + absolutePath);
                    return;
                }
                try { File.Delete(absolutePath); }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "Could not remove existing output file: " + ex.Message);
                    return;
                }
            }

            var dir = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "Could not create output directory: " + ex.Message);
                    return;
                }
            }

            var sourceExpression = WrapSource(source);

            try
            {
                // The excel extension requires spatial; ensure both are loaded.
                ExcelExtension.Ensure(session);

                session.Execute(conn =>
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText =
                        $"COPY {sourceExpression} TO {SqlIdentifier.QuoteLiteral(absolutePath)} WITH (FORMAT GDAL, DRIVER 'xlsx')";
                    cmd.ExecuteNonQuery();
                });

                da.SetData(_outDatabase, dbConnection);
                da.SetData(_outPath, path);
            }
            catch (Exception ex)
            {
                ReportError("Excel export failed", ex);
            }
        }
    }
}

