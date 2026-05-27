using System;
using System.Collections.Generic;
using System.Linq;
using GhDucky.Utils;
using Grasshopper.Kernel;

namespace GhDucky.Components.Query
{
    public class FilterBuilderComponent : GH_Component
    {
        public FilterBuilderComponent()
            : base(
                "Ducky Filter",
                "DuckyFlt",
                "Creates a filter condition for selecting rows. " +
                "Connect the output to the Query Builder's Filters input. No SQL knowledge required!\n\n" +
                "Supported operators:\n" +
                "  = (equals), != (not equals)\n" +
                "  > (greater than), >= (greater or equal)\n" +
                "  < (less than), <= (less or equal)\n" +
                "  contains, starts with, ends with\n" +
                "  is empty, is not empty\n" +
                "  in (value is one of a list)\n" +
                "  between (value is in a range)",
                "Ducky",
                "3 | Query")
        {
        }

        public override Guid ComponentGuid => new Guid("f1a8d34c-7b2e-4c5a-a9d1-3e6f8c0b5d72");

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        protected override System.Drawing.Bitmap Icon => IconFactory.Build("🐟", IconFactory.Query);

        private int _inColumn;
        private int _inOperator;
        private int _inValue;
        private int _inValue2;
        private int _inIsText;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            _inColumn = pManager.AddTextParameter("Column", "C",
                "The column name to filter on (e.g. \"age\", \"city\", \"score\").",
                GH_ParamAccess.item);

            _inOperator = pManager.AddTextParameter("Operator", "Op",
                "The comparison to perform. Options:\n" +
                "  =, !=, >, >=, <, <=\n" +
                "  contains, starts with, ends with\n" +
                "  is empty, is not empty\n" +
                "  in, between",
                GH_ParamAccess.item, "=");

            _inValue = pManager.AddTextParameter("Value", "V",
                "The value to compare against.\n" +
                "• For 'in': supply multiple values as a list.\n" +
                "• For 'between': this is the lower bound.\n" +
                "• For 'is empty'/'is not empty': this input is ignored.",
                GH_ParamAccess.list);

            _inValue2 = pManager.AddTextParameter("Value 2", "V2",
                "Second value — only used for the 'between' operator (upper bound).",
                GH_ParamAccess.item, string.Empty);

            _inIsText = pManager.AddBooleanParameter("Text?", "T?",
                "If true, values are treated as text strings (wrapped in quotes). " +
                "If false, values are treated as numbers/raw SQL values. " +
                "Default is true (safe for most cases).",
                GH_ParamAccess.item, true);

            pManager[_inOperator].Optional = true;
            pManager[_inValue].Optional = true;
            pManager[_inValue2].Optional = true;
            pManager[_inIsText].Optional = true;
        }

        private int _outFilter;
        private int _outExplanation;

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            _outFilter = pManager.AddTextParameter("Filter", "F",
                "The SQL filter expression. Connect to Query Builder's Filters input.",
                GH_ParamAccess.item);

            _outExplanation = pManager.AddTextParameter("Explanation", "E",
                "Plain-English description of what this filter does.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var column = string.Empty;
            if (!da.GetData(_inColumn, ref column) || string.IsNullOrWhiteSpace(column))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Column name is required.");
                return;
            }

            var op = "=";
            da.GetData(_inOperator, ref op);
            op = (op ?? "=").Trim().ToLowerInvariant();

            var values = new List<string>();
            da.GetDataList(_inValue, values);

            var value2 = string.Empty;
            da.GetData(_inValue2, ref value2);

            var isText = true;
            da.GetData(_inIsText, ref isText);

            var quotedCol = SqlIdentifier.Quote(column.Trim());
            string filter;
            string explanation;

            switch (op)
            {
                case "=":
                case "equals":
                    filter = $"{quotedCol} = {FormatValue(FirstValue(values), isText)}";
                    explanation = $"{column} equals {FirstValue(values)}";
                    break;

                case "!=":
                case "<>":
                case "not equals":
                case "not equal":
                    filter = $"{quotedCol} != {FormatValue(FirstValue(values), isText)}";
                    explanation = $"{column} does not equal {FirstValue(values)}";
                    break;

                case ">":
                case "greater than":
                    filter = $"{quotedCol} > {FormatValue(FirstValue(values), isText)}";
                    explanation = $"{column} is greater than {FirstValue(values)}";
                    break;

                case ">=":
                case "greater or equal":
                    filter = $"{quotedCol} >= {FormatValue(FirstValue(values), isText)}";
                    explanation = $"{column} is greater than or equal to {FirstValue(values)}";
                    break;

                case "<":
                case "less than":
                    filter = $"{quotedCol} < {FormatValue(FirstValue(values), isText)}";
                    explanation = $"{column} is less than {FirstValue(values)}";
                    break;

                case "<=":
                case "less or equal":
                    filter = $"{quotedCol} <= {FormatValue(FirstValue(values), isText)}";
                    explanation = $"{column} is less than or equal to {FirstValue(values)}";
                    break;

                case "contains":
                    filter = $"{quotedCol} LIKE '%' || {FormatValue(FirstValue(values), true)} || '%'";
                    explanation = $"{column} contains \"{FirstValue(values)}\"";
                    break;

                case "starts with":
                case "startswith":
                    filter = $"{quotedCol} LIKE {FormatValue(FirstValue(values), true)} || '%'";
                    explanation = $"{column} starts with \"{FirstValue(values)}\"";
                    break;

                case "ends with":
                case "endswith":
                    filter = $"{quotedCol} LIKE '%' || {FormatValue(FirstValue(values), true)}";
                    explanation = $"{column} ends with \"{FirstValue(values)}\"";
                    break;

                case "is empty":
                case "is null":
                case "empty":
                    filter = $"{quotedCol} IS NULL";
                    explanation = $"{column} is empty (null)";
                    break;

                case "is not empty":
                case "is not null":
                case "not empty":
                    filter = $"{quotedCol} IS NOT NULL";
                    explanation = $"{column} is not empty (not null)";
                    break;

                case "in":
                case "one of":
                    if (values.Count == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            "The 'in' operator requires at least one value in the Value list.");
                        return;
                    }
                    var formatted = string.Join(", ", values.Select(v => FormatValue(v, isText)));
                    filter = $"{quotedCol} IN ({formatted})";
                    explanation = $"{column} is one of: {string.Join(", ", values)}";
                    break;

                case "between":
                    if (values.Count == 0 || string.IsNullOrWhiteSpace(value2))
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            "The 'between' operator requires Value (lower bound) and Value 2 (upper bound).");
                        return;
                    }
                    filter = $"{quotedCol} BETWEEN {FormatValue(FirstValue(values), isText)} AND {FormatValue(value2, isText)}";
                    explanation = $"{column} is between {FirstValue(values)} and {value2}";
                    break;

                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Unknown operator: \"{op}\". " +
                        "Supported: =, !=, >, >=, <, <=, contains, starts with, ends with, is empty, is not empty, in, between.");
                    return;
            }

            da.SetData(_outFilter, filter);
            da.SetData(_outExplanation, explanation);
        }

        private static string FirstValue(List<string> values)
        {
            return values.Count > 0 ? values[0] : string.Empty;
        }

        private static string FormatValue(string value, bool isText)
        {
            if (value == null) return "NULL";
            return isText ? SqlIdentifier.QuoteLiteral(value) : value;
        }
    }
}

