﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.OData.UriParser;
using Moon.OData.Edm;

namespace Moon.OData.Sql
{
    /// <summary>
    /// The <c>WHERE</c> SQL clause builder.
    /// </summary>
    public class WhereClause : SqlClauseBase
    {
        private readonly IList<object> arguments;
        private readonly string oprator;

        /// <summary>
        /// Initializes a new instance of the <see cref="WhereClause" /> class.
        /// </summary>
        /// <param name="oprator">The operator to start with.</param>
        /// <param name="arguments">The list where to store values of SQL arguments.</param>
        /// <param name="options">The OData query options.</param>
        public WhereClause(string oprator, IList<object> arguments, IODataOptions options)
            : base(options)
        {
            Requires.NotNull(oprator, nameof(oprator));
            Requires.NotNull(arguments, nameof(arguments));

            this.oprator = oprator;
            this.arguments = arguments;
        }

        /// <summary>
        /// Builds a <c>WHERE</c> SQL clause using the given OData query options.
        /// </summary>
        /// <param name="startWith">The operator to start with.</param>
        /// <param name="arguments">The list where to store values of SQL arguments.</param>
        /// <param name="options">The OData query options.</param>
        public static string Build(string startWith, IList<object> arguments, IODataOptions options)
            => Build(startWith, arguments, options, null);

        /// <summary>
        /// Builds a <c>WHERE</c> SQL clause using the given OData query options.
        /// </summary>
        /// <param name="startWith">The operator to start with.</param>
        /// <param name="arguments">The list where to store values of SQL arguments.</param>
        /// <param name="options">The OData query options.</param>
        /// <param name="resolveColumn">A function used to resolve column names.</param>
        public static string Build(string startWith, IList<object> arguments, IODataOptions options, Func<PropertyInfo, string> resolveColumn)
        {
            var clause = new WhereClause(startWith, arguments, options);

            if (resolveColumn != null)
            {
                clause.ResolveColumn = resolveColumn;
            }

            return clause.Build();
        }

        /// <summary>
        /// Builds a <c>WHERE</c> SQL clause. The method returns an empty string when $filter option
        /// is not defined.
        /// </summary>
        public override string Build()
        {
            var builder = new StringBuilder();

            if (Options.Filter != null)
            {
                builder.Append($"{oprator.ToUpperInvariant()} ");
                AppendQueryNode(builder, Options.Filter.Expression);
            }

            return builder.ToString();
        }

        private void AppendQueryNode(StringBuilder builder, QueryNode node)
        {
            switch (node.Kind)
            {
                case QueryNodeKind.Convert:
                    AppendQueryNode(builder, (node as ConvertNode).Source);
                    return;

                case QueryNodeKind.Constant:
                    AppendConstantNode(builder, node as ConstantNode);
                    return;

                case QueryNodeKind.BinaryOperator:
                    AppendBinaryOperatorNode(builder, node as BinaryOperatorNode);
                    return;

                case QueryNodeKind.UnaryOperator:
                    AppendUnaryOperatorNode(builder, node as UnaryOperatorNode);
                    return;

                case QueryNodeKind.SingleValuePropertyAccess:
                    AppendSingleValuePropertyAccessNode(builder, node as SingleValuePropertyAccessNode);
                    return;

                case QueryNodeKind.SingleValueFunctionCall:
                    AppendSingleValueFunctionCallNode(builder, node as SingleValueFunctionCallNode);
                    break;

                default:
                    throw new ODataException($"The '{node.GetType().Name}' node is not supported.");
            }
        }

        private void AppendConstantNode(StringBuilder builder, ConstantNode node)
        {
            if (node.Value == null)
            {
                builder.Append("NULL");
            }
            else
            {
                builder.AppendArgument(arguments.Count);
                arguments.Add(node.Value);
            }
        }

        private void AppendBinaryOperatorNode(StringBuilder builder, BinaryOperatorNode node)
        {
            builder.Append("(");

            AppendQueryNode(builder, node.Left);

            if (!IsMethodCall(node.Left))
            {
                var constantNode = GetConstantNode(node.Right);

                if (constantNode != null && constantNode.Value == null)
                {
                    if (node.OperatorKind == BinaryOperatorKind.Equal)
                    {
                        builder.Append(" IS ");
                    }
                    else if (node.OperatorKind == BinaryOperatorKind.NotEqual)
                    {
                        builder.Append(" IS NOT ");
                    }
                }
                else
                {
                    builder.Append($" {ToSqlOperator(node.OperatorKind)} ");
                }

                AppendQueryNode(builder, node.Right);
            }
            else
            {
                builder.Append($" {ToSqlOperator(node.OperatorKind)} ");
                AppendQueryNode(builder, node.Right);
            }

            builder.Append(")");
        }

        private void AppendUnaryOperatorNode(StringBuilder builder, UnaryOperatorNode node)
        {
            builder.Append($"{ToSqlOperator(node.OperatorKind)} ");
            AppendQueryNode(builder, node.Operand);
        }

        private void AppendSingleValuePropertyAccessNode(StringBuilder builder, SingleValuePropertyAccessNode node)
        {
            var property = GetProperty(node);
            var column = ResolveColumn(property.Property);

            if (column == null)
            {
                throw new ODataException("The column name couldn't be resolved.");
            }

            builder.Append(column);
        }

        private void AppendSingleValueFunctionCallNode(StringBuilder builder, SingleValueFunctionCallNode node)
        {
            var parameters = node.Parameters.ToList();

            switch (node.Name)
            {
                case "contains":
                    AppendQueryNode(builder, parameters[0]);
                    builder.Append(" LIKE ('%' + ");
                    AppendQueryNode(builder, parameters[1]);
                    builder.Append(" + '%')");
                    break;

                case "endswith":
                    AppendQueryNode(builder, parameters[0]);
                    builder.Append(" LIKE ('%' + ");
                    AppendQueryNode(builder, parameters[1]);
                    builder.Append(")");
                    break;

                case "startswith":
                    AppendQueryNode(builder, parameters[0]);
                    builder.Append(" LIKE (");
                    AppendQueryNode(builder, parameters[1]);
                    builder.Append(" + '%')");
                    break;

                case "indexof":
                    builder.Append("CHARINDEX(");
                    AppendQueryNode(builder, parameters[1]);
                    builder.Append(", ");
                    AppendQueryNode(builder, parameters[0]);
                    builder.Append(")");
                    break;

                case "trim":
                    builder.Append("LTRIM(RTRIM(");
                    AppendQueryNode(builder, parameters[0]);
                    builder.Append("))");
                    break;

                case "hour":
                case "minute":
                case "second":
                    builder.Append($"DATEPART({node.Name}, ");
                    AppendQueryNode(builder, parameters[0]);
                    builder.Append(")");
                    break;

                case "date":
                case "time":
                    builder.Append("CAST(");
                    AppendQueryNode(builder, parameters[0]);
                    builder.Append($" AS {node.Name})");
                    break;

                case "totaloffsetminutes":
                    builder.Append("DATEPART(TZoffset, ");
                    AppendQueryNode(builder, parameters[0]);
                    builder.Append(")");
                    break;

                case "totalseconds":
                    builder.Append("DATEDIFF(second, 0, ");
                    AppendQueryNode(builder, parameters[0]);
                    builder.Append(")");
                    break;

                case "length":
                case "substring":
                case "tolower":
                case "toupper":
                case "concat":
                case "year":
                case "month":
                case "day":
                case "now":
                case "round":
                case "floor":
                case "ceiling":
                    builder.Append($"{ToSqlFunction(node.Name)}(");

                    for (var i = 0; i < parameters.Count; i++)
                    {
                        AppendQueryNode(builder, parameters[i]);

                        if (i < parameters.Count - 1)
                        {
                            builder.Append(", ");
                        }
                    }

                    builder.Append(")");
                    break;

                default:
                    throw new ODataException($"The function '{node.Name}' is not supported.");
            }
        }

        private bool IsMethodCall(SingleValueNode node)
        {
            var callNode = node as SingleValueFunctionCallNode;

            if (callNode != null)
            {
                return callNode.Name == "contains"
                    || callNode.Name == "endswith"
                    || callNode.Name == "startswith";
            }

            return false;
        }

        private ConstantNode GetConstantNode(SingleValueNode node)
        {
            if (node is ConvertNode)
            {
                return GetConstantNode((node as ConvertNode).Source);
            }

            return node as ConstantNode;
        }

        private string ToSqlOperator(BinaryOperatorKind operatorKind)
        {
            switch (operatorKind)
            {
                case BinaryOperatorKind.Or:
                    return "OR";

                case BinaryOperatorKind.And:
                    return "AND";

                case BinaryOperatorKind.Equal:
                    return "=";

                case BinaryOperatorKind.NotEqual:
                    return "<>";

                case BinaryOperatorKind.GreaterThan:
                    return ">";

                case BinaryOperatorKind.GreaterThanOrEqual:
                    return ">=";

                case BinaryOperatorKind.LessThan:
                    return "<";

                case BinaryOperatorKind.LessThanOrEqual:
                    return "<=";

                case BinaryOperatorKind.Add:
                    return "+";

                case BinaryOperatorKind.Subtract:
                    return "-";

                case BinaryOperatorKind.Multiply:
                    return "*";

                case BinaryOperatorKind.Divide:
                    return "/";

                case BinaryOperatorKind.Modulo:
                    return "%";

                default:
                    throw new ODataException($"The operator '{operatorKind}' is not supported.");
            }
        }

        private string ToSqlOperator(UnaryOperatorKind operatorKind)
        {
            if (operatorKind != UnaryOperatorKind.Not)
            {
                throw new ODataException($"The operator '{operatorKind}' is not supported.");
            }

            return "NOT";
        }

        private string ToSqlFunction(string functionName)
        {
            switch (functionName)
            {
                case "length":
                    return "LEN";

                case "tolower":
                    return "LOWER";

                case "toupper":
                    return "UPPER";

                case "now":
                    return "GETUTCDATE";

                default:
                    return functionName.ToUpperInvariant();
            }
        }
    }
}