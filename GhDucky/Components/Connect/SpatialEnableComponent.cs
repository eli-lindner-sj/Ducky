using System;
using GhDucky.Parameters;
using GhDucky.Services;
using GhDucky.Utils;
using Grasshopper.Kernel;

namespace GhDucky.Components.Connect
{
    public class SpatialEnableComponent : DuckyComponentBase
    {
        public SpatialEnableComponent()
            : base(
                "Ducky Enable Spatial",
                "DuckySpatial",
                "Installs (first run, requires internet) and loads the DuckDB spatial extension on the connection. " +
                "Required before using GEOMETRY columns or ST_* functions.",
                "Ducky",
                "1 | Connect")
        {
        }

        public override Guid ComponentGuid => new Guid("13b1b5b1-8a83-4f3d-9b3c-c0a6e1f23ab1");

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => IconFactory.Build("📐", IconFactory.Spatial);

        private int _inEnable;
        private int _inDatabase;
        
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            _inEnable = pManager.AddBooleanParameter("Enable?", "E?",
                "Set to true to install and load the spatial extension.",
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
                "True if the spatial extension is now loaded.", 
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
                SpatialExtension.Ensure(session);
                da.SetData(_outDatabase, dbConnection);
                da.SetData(_outEnabled, true);
            }
            catch (Exception ex)
            {
                ReportError("Could not enable spatial extension", ex);
                da.SetData(_outEnabled, false);
            }
        }
    }
}
