using System;
using System.IO;
using GhDucky.Parameters;
using GhDucky.Utils;
using Grasshopper.Kernel;

namespace GhDucky.Components.Import
{
    public class ImportCsvComponent : DuckyComponentBase
    {
        public ImportCsvComponent()
            : base(
                "Ducky Import CSV",
                "DuckyCSV",
                "Loads a CSV file into a Database table (using DuckDB's native read_csv_auto reader).",
                "Ducky",
                "2 | Import")
        {
        }

        public override Guid ComponentGuid => new Guid("e87c1d7d-19a4-4f6b-9c0a-8b4d4f4f6cf1");

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        
        protected override System.Drawing.Bitmap Icon => IconFactory.Build("📝", IconFactory.ImportFile);
        
        private int _inImport;
        private int _inDatabase;
        private int _inPath;
        private int _inTable;
        private int _inSchema;
        private int _inHasHeader;
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
                "Absolute path to source CSV file.", 
                GH_ParamAccess.item);
            _inHasHeader = pManager.AddBooleanParameter("Headers?", "H?",
                "Whether the first row contains column headers.", 
                GH_ParamAccess.item, true);
            _inSchema = pManager.AddTextParameter("Schema", "S",
                "Target schema (default: main). Created automatically if missing.",
                GH_ParamAccess.item, "main");
            _inOverwrite = pManager.AddBooleanParameter("Overwrite?", "O?",
                "If true (default), the table is dropped and recreated. otherwise rows are appended.",
                GH_ParamAccess.item, true);

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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Path to CSV file required.");
                return;
            }
            if (!File.Exists(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "CSV file not found: " + path);
                return;
            }
            
            var hasHeader = true;
            var schema = "main";
            var overwrite = true;
            da.GetData(_inSchema, ref schema);
            da.GetData(_inHasHeader, ref hasHeader);
            da.GetData(_inOverwrite, ref overwrite);
            
            var quotedTable = SqlIdentifier.QuoteTable(schema, table);
            var pathLiteral = SqlIdentifier.QuoteLiteral(Path.GetFullPath(path));
            var headerArg = hasHeader ? "TRUE" : "FALSE";
            var sourceExpr = $"read_csv_auto({pathLiteral}, header={headerArg}, sample_size=-1)";

            try
            {
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
                ReportError("CSV import failed", ex);
            }
        }
    }
}
