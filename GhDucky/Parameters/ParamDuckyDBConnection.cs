using System;
using GhDucky.Utils;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

namespace GhDucky.Parameters
{
    public sealed class ParamDuckyDbConnection : Param_GenericObject
    {
        public override Guid ComponentGuid => new Guid("3e9fb04a-0e1f-49ad-b8ed-1f4ad6c5d4b0");
        
        public override GH_Exposure Exposure => GH_Exposure.hidden;
        
        protected override System.Drawing.Bitmap Icon => IconFactory.Build("🦆", IconFactory.Neutral);

        public override string Name => "Ducky Database Connection";
        public override string NickName => "DuckyDB";
        public override string Description => "A Ducky Database connection handle.";
        public override string Category => "Ducky";
        public override string SubCategory => "Params";

        protected override GH_GetterResult Prompt_Singular(ref IGH_Goo value) => GH_GetterResult.cancel;
        protected override GH_GetterResult Prompt_Plural(ref System.Collections.Generic.List<IGH_Goo> values)
            => GH_GetterResult.cancel;
    }
}
