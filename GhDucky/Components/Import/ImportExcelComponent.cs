using System;
using System.IO;
using GhDucky.Parameters;
using GhDucky.Services;
using GhDucky.Utils;
using Grasshopper.Kernel;

namespace GhDucky.Components.Import
{
    public class ImportExcelComponent : DuckyComponentBase
    {
        public ImportExcelComponent()
            : base(
                "Ducky Import Excel",
                "DuckyXLSX",
                "Loads an Excel (.xlsx) file into a DuckDB table using the DuckDB spatial and excel extensions. " +
                "Specify a sheet name to import a specific worksheet; otherwise the first sheet is used.",
                "Ducky",
                "2 | Import")
        {
        }

        public override Guid ComponentGuid => new Guid("d5f8a3b2-6c4e-4d7a-b9e2-1a3c5d8f7e60");

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => IconFactory.Build("📊", IconFactory.Excel);

        private int _inImport;
        private int _inDatabase;
        private int _inPath;
        private int _inTable;
        private int _inSheet;
        private int _inHasHeader;
        private int _inSchema;
        private int _inOverwrite;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            _inImport = pManager.AddBooleanParameter("Import?", "I?",
                "Set to true to perform the import.",
                GH_ParamAccess.item, false);
            _inDatabase = pManager.AddParameter(new ParamDuckyDbConnection(), "Database", "DB",
                "Database connection.",
                GH_ParamAccess.item);
            _inTable = pManager.AddTextParameter("Table", "T",
                "Target table name.",
                GH_ParamAccess.item);
            _inPath = pManager.AddTextParameter("Path", "P",
                "Absolute path to source Excel (.xlsx) file.",
                GH_ParamAccess.item);
            _inSheet = pManager.AddTextParameter("Sheet", "Sh",
                "Worksheet name to import. Leave empty for the first sheet.",
                GH_ParamAccess.item, string.Empty);
            _inHasHeader = pManager.AddBooleanParameter("Headers?", "H?",
                "Whether the first row contains column headers.",
                GH_ParamAccess.item, true);
            _inSchema = pManager.AddTextParameter("Schema", "S",
                "Target schema (default: main). Created automatically if missing.",
                GH_ParamAccess.item, "main");
            _inOverwrite = pManager.AddBooleanParameter("Overwrite?", "O?",
                "If true (default), the table is dropped and recreated; otherwise rows are appended.",
                GH_ParamAccess.item, true);

            pManager[_inSheet].Optional = true;
            pManager[_inSchema].Optional = true;
        }

        private int _outDatabase;
        private int _outTable;
        private int _outRows;

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            _outDatabase = pManager.AddParameter(new ParamDuckyDbConnection(), "Database", "DB",
                "Database connection (passthrough).",
                GH_ParamAccess.item);
            _outTable = pManager.AddTextParameter("Table", "T",
                "Imported table name.",
                GH_ParamAccess.item);
            _outRows = pManager.AddIntegerParameter("Rows", "R",
                "Row count of the table after the import.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var import = false;
            da.GetData(_inImport, ref import);
            if (!import)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Import is false; no action taken.");
                return;
            }

            if (!TryGetSession(da, _inDatabase, out var session, out var dbConnection))
                return;

            var table = string.Empty;
            if (!da.GetData(_inTable, ref table) || string.IsNullOrWhiteSpace(table))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Table name required.");
                return;
            }

            var path = string.Empty;
            if (!da.GetData(_inPath, ref path) || string.IsNullOrWhiteSpace(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Path to Excel file required.");
                return;
            }
            if (!File.Exists(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Excel file not found: " + path);
                return;
            }

            var sheet = string.Empty;
            var hasHeader = true;
            var schema = "main";
            var overwrite = true;
            da.GetData(_inSheet, ref sheet);
            da.GetData(_inHasHeader, ref hasHeader);
            da.GetData(_inSchema, ref schema);
            da.GetData(_inOverwrite, ref overwrite);

            var quotedTable = SqlIdentifier.QuoteTable(schema, table);
            var pathLiteral = SqlIdentifier.QuoteLiteral(Path.GetFullPath(path));
            var headerArg = hasHeader ? "true" : "false";

            // Build the st_read call with optional sheet parameter.
            var sourceExpr = string.IsNullOrWhiteSpace(sheet)
                ? $"st_read({pathLiteral}, open_options = ['HEADERS={headerArg.ToUpperInvariant()}'])"
                : $"st_read({pathLiteral}, layer = {SqlIdentifier.QuoteLiteral(sheet)}, open_options = ['HEADERS={headerArg.ToUpperInvariant()}'])";

            try
            {
                // The excel extension requires spatial; ensure both are loaded.
                ExcelExtension.Ensure(session);

                session.Execute(conn =>
                {
                    var rowCount = RunSelectImport(conn, schema, table, quotedTable, sourceExpr, overwrite);

                    da.SetData(_outDatabase, dbConnection);
                    da.SetData(_outTable, table);
                    da.SetData(_outRows, rowCount);
                });
            }
            catch (Exception ex)
            {
                ReportError("Excel import failed", ex);
            }
        }
    }
}

