using System;
using GhDucky.Parameters;
using GhDucky.Services;
using GhDucky.Utils;
using Grasshopper.Kernel;

namespace GhDucky.Components.Connect
{
    public class DisconnectComponent : DuckyComponentBase
    {
        public DisconnectComponent()
            : base(
                "Ducky Disconnect",
                "DuckyOff",
                "Closes a Database session and releases any file locks held by a file-backed database. " +
                "In-memory databases are discarded.",
                "Ducky",
                "1 | Connect")
        {
        }
        
        public override Guid ComponentGuid => new Guid("9b71e62c-3a08-4b6b-9c0e-2b3f6a5a1e2e");

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => IconFactory.Build("🧹", IconFactory.Disconnect);
        
        private int _inDatabase;
        private int _inDisconnect;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            _inDisconnect = pManager.AddBooleanParameter("Disconnect?", "D?",
                "Set to true to disconnect the specified connection.",
                GH_ParamAccess.item, false);
            _inDatabase = pManager.AddParameter(new ParamDuckyDbConnection(), "Database", "DB",
                "Database connection.", 
                GH_ParamAccess.item);
        }
        
        private int _outClosed;
        private int _outInfo;
        
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            _outClosed = pManager.AddBooleanParameter("Closed?", "C?",
                "True if the connection was closed (or was already closed).", 
                GH_ParamAccess.item);
            _outInfo = pManager.AddTextParameter("Info", "I",
                "Database connection status message.", 
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var disconnect = false;
            
            if (!TryGetSession(da, _inDatabase, out _, out var dbConnection))
                return;
            
            da.GetData(_inDisconnect, ref disconnect);
            if (!disconnect)
            {
                var currentSession = dbConnection.Session;
                var isOpen = currentSession?.IsOpen ?? false;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    isOpen
                        ? "Pending — set Disconnect to true to close."
                        : "Database connection is already closed.");
                da.SetData(_outClosed, !isOpen);
                return;
            }
            
            try
            {
                var session = dbConnection.Session;
                var label = session?.DisplayName ?? dbConnection.SessionId;
                DuckDBConnectionManager.Close(dbConnection.SessionId);
                da.SetData(_outClosed, true);
                da.SetData(_outInfo, $"Closed session [{label}].");
            }
            catch (Exception ex)
            {
                ReportError("Disconnect failed", ex);
            }
        }
    }
}
