using System;
using GhDucky.Parameters;
using GhDucky.Services;
using GhDucky.Utils;
using Grasshopper.Kernel;

namespace GhDucky.Components.Connect
{
    public class ExcelEnableComponent : DuckyComponentBase
    {
        public ExcelEnableComponent()
            : base(
                "Ducky Enable Excel",
                "DuckyExcel",
                "Installs (first run, requires internet) and loads the DuckDB spatial and excel extensions on the connection. " +
                "The Import/Export Excel components auto-enable these extensions, so this component is only needed " +
                "when using Excel-related SQL functions (e.g. st_read with .xlsx) directly in a Ducky Query.",
                "Ducky",
                "1 | Connect")
        {
        }

        public override Guid ComponentGuid => new Guid("a4e7c2d1-3b8f-4a6e-9d1c-5f2a8b7e4c30");

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => IconFactory.Build("📊", IconFactory.Excel);

        private int _inEnable;
        private int _inDatabase;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            _inEnable = pManager.AddBooleanParameter("Enable?", "E?",
                "Set to true to install and load the excel extension.",
                GH_ParamAccess.item, true);
            _inDatabase = pManager.AddParameter(new ParamDuckyDbConnection(), "Database", "DB",
                "Database connection.",
                GH_ParamAccess.item);
        }

        private int _outDatabase;
        private int _outEnabled;

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            _outDatabase = pManager.AddParameter(new ParamDuckyDbConnection(), "Database", "DB",
                "Connection (passthrough).",
                GH_ParamAccess.item);
            _outEnabled = pManager.AddBooleanParameter("Enabled?", "E?",
                "True if the excel extension is now loaded.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var enable = true;
            da.GetData(_inEnable, ref enable);
            if (!enable)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Enable is disabled, set Enable to true to load.");
                da.SetData(_outEnabled, false);
                return;
            }

            if (!TryGetSession(da, _inDatabase, out var session, out var dbConnection))
                return;

            try
            {
                ExcelExtension.Ensure(session);
                da.SetData(_outDatabase, dbConnection);
                da.SetData(_outEnabled, true);
            }
            catch (Exception ex)
            {
                ReportError("Could not enable excel extension", ex);
                da.SetData(_outEnabled, false);
            }
        }
    }
}

