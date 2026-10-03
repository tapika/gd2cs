using System.Text;
using gd2cs.Language;
using gd2cs.Model;

namespace gd2cs.Emission;

public sealed class GdScriptEmitter : IScriptEmitter
{
    private const string space_colon = " : ";
    private const string colon = ": ";
    private readonly GodotTypeCatalog godotTypes;

    public GdScriptEmitter(GodotTypeCatalog godotTypes)
    {
        this.godotTypes = godotTypes;
    }

    public string Emit(ScriptModel model)
    {
        var output = new StringBuilder();
        foreach (var element in model.Elements)
        {
            switch (element)
            {
                case CommentElement comment:
                    output.Append("# ");
                    output.AppendLine(comment.Text);
                    break;
                case BlankLineElement:
                    output.AppendLine();
                    break;
                case ClassDeclaration declaration:
                    EmitClass(output, declaration, "", isRoot: true);
                    break;
                default:
                    throw new NotSupportedException("Unsupported language dialect.");
            }
        }
        return output.ToString().Replace(Environment.NewLine, "\n");
    }

    private void EmitClass(StringBuilder output, ClassDeclaration declaration, string indent, bool isRoot)
    {
        if (isRoot)
            output.Append(indent + "class_name ");
        else
            output.Append(indent + "class ");
        output.Append(declaration.Name);
        if (isRoot)
            output.AppendLine();
        else
            output.AppendLine(":");
        if (isRoot)
        {
            output.Append(indent + "extends ");
            EmitType(output, declaration.BaseType);
            output.AppendLine();
            if (declaration.Elements.Count > 0)
                output.AppendLine();
        }
        foreach (var element in declaration.Elements)
        {
            var memberIndent = indent + (isRoot ? "" : "\t");
            switch (element)
            {
                case CommentElement comment:
                    output.Append(memberIndent + "# ");
                    output.AppendLine(comment.Text);
                    break;
                case BlankLineElement:
                    output.AppendLine();
                    break;
                case ConstantDeclaration constant:
                    output.Append(memberIndent + "const ");
                    output.Append(constant.Name);
                    if (constant.Value is ArrayExpression)
                    {
                        output.Append(space_colon);
                        EmitType(output, constant.Type);
                        output.Append(" = ");
                    }
                    else
                        output.Append(" := ");
                    EmitExpression(output, constant.Value, memberIndent);
                    output.AppendLine();
                    break;
                case FieldDeclaration field:
                    output.Append(memberIndent + "var ");
                    output.Append(field.Name);
                    if (field.Initializer is null)
                    {
                        output.Append(space_colon);
                        EmitType(output, field.Type);
                        output.AppendLine();
                        break;
                    }
                    output.Append(" := ");
                    EmitExpression(output, field.Initializer, memberIndent);
                    output.AppendLine();
                    break;
                case ConstructorDeclaration constructor:
                    output.Append(memberIndent + "func _init");
                    EmitCallable(output, constructor, memberIndent);
                    break;
                case MethodDeclaration method:
                    if (method.IsStatic)
                        output.Append(memberIndent + "static func ");
                    else
                        output.Append(memberIndent + "func ");
                    output.Append(method.Kind == MethodKind.StringConversion ? "_to_string" : method.Name);
                    EmitParameters(output, method.Parameters, memberIndent);
                    if (method.ReturnType.Kind != TypeKind.Void)
                    {
                        output.Append(") -> ");
                        EmitType(output, method.ReturnType);
                    }
                    else
                        output.Append(')');
                    output.AppendLine(":");
                    EmitBody(output, method.Elements, memberIndent + "\t");
                    break;
                case DestructorDeclaration destructor:
                    output.Append(memberIndent + "func _exit_tree");
                    EmitCallable(output, destructor, memberIndent);
                    break;
                case NestedClassDeclaration nested:
                    EmitClass(output, new ClassDeclaration(nested.Name, nested.BaseType, false, nested.Elements), "", false);
                    break;
                default:
                    throw new NotSupportedException("Unsupported language dialect.");
            }
        }
    }

    private void EmitCallable(StringBuilder output, CallableDeclaration callable, string indent)
    {
        EmitParameters(output, callable.Parameters, indent);
        output.AppendLine("):");
        EmitBody(output, callable.Elements, indent + "\t");
    }

    // Reconstructs parameter line breaks independently from the callable body.
    private static void EmitParameters(StringBuilder output, ParameterList parameters, string indent)
    {
        output.Append('(');
        for (var index = 0; index < parameters.Parameters.Count; index++)
        {
            if (index > 0)
                output.Append(',');
            if (parameters.Parameters[index].StartsOnNewLine)
            {
                output.AppendLine();
                output.Append(indent + "\t");
            }
            else if (index > 0)
                output.Append(' ');
            output.Append(parameters.Parameters[index].Name);
            output.Append(colon);
            EmitType(output, parameters.Parameters[index].Type);
        }
        if (parameters.ClosingParenthesisStartsOnNewLine)
        {
            output.AppendLine();
            output.Append(indent);
        }
    }

    private static void EmitType(StringBuilder output, TypeReference type)
    {
        output.Append(type.Kind == TypeKind.String ? "String" : type.Name);
        if (type.TypeArguments.Count == 0)
            return;
        output.Append('[');
        for (var index = 0; index < type.TypeArguments.Count; index++)
        {
            if (index > 0)
                output.Append(", ");
            EmitType(output, type.TypeArguments[index]);
        }
        output.Append(']');
    }

    private void EmitBody(StringBuilder output, List<SyntaxElement> elements, string indent)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case CommentElement comment:
                    output.Append(indent + "# ");
                    output.AppendLine(comment.Text);
                    break;
                case BlankLineElement:
                    output.AppendLine();
                    break;
                case AssignmentStatement statement:
                    output.Append(indent);
                    EmitExpression(output, statement.Target, indent);
                    output.Append(" = ");
                    EmitExpression(output, statement.Value, indent);
                    output.AppendLine();
                    break;
                case ReturnStatement statement:
                    output.Append(indent + "return");
                    if (statement.Value is not null)
                    {
                        output.Append(' ');
                        EmitExpression(output, statement.Value, indent);
                    }
                    output.AppendLine();
                    break;
                case ExpressionStatement statement:
                    output.Append(indent);
                    EmitExpression(output, statement.Expression, indent);
                    output.AppendLine();
                    break;
                case LocalVariableStatement statement:
                    output.Append(indent + "var ");
                    output.Append(statement.Name);
                    if (statement.Typing == LocalTyping.Inferred)
                        output.Append(" := ");
                    else if (statement.Typing is LocalTyping.Explicit or LocalTyping.InferredWithExplicitGdType)
                    {
                        output.Append(space_colon);
                        EmitType(output, statement.Type ?? throw new InvalidOperationException("Explicit local requires a type."));
                        if (statement.Initializer is null)
                        {
                            output.AppendLine();
                            break;
                        }
                        output.Append(" = ");
                    }
                    else
                        output.Append(" = ");
                    EmitExpression(output, statement.Initializer ?? throw new InvalidOperationException("Inferred local requires a value."), indent);
                    output.AppendLine();
                    break;
                case ForEachStatement statement:
                    output.Append(indent + "for ");
                    output.Append(statement.Variable);
                    output.Append(" in ");
                    EmitExpression(output, statement.Collection, indent);
                    output.AppendLine(":");
                    EmitBody(output, statement.Elements, indent + "\t");
                    break;
                case WhileStatement statement:
                    output.Append(indent + "while ");
                    EmitExpression(output, statement.Condition, indent);
                    output.AppendLine(":");
                    EmitBody(output, statement.Elements, indent + "\t");
                    break;
                case ConditionalStatement statement:
                    EmitConditional(output, statement, indent);
                    break;
                case BreakStatement:
                    output.AppendLine(indent + "break");
                    break;
                case ContinueStatement:
                    output.AppendLine(indent + "continue");
                    break;
                default:
                    throw new NotSupportedException("Unsupported language dialect.");
            }
        }
    }

    private void EmitConditional(StringBuilder output, ConditionalStatement statement, string indent)
    {
        for (var index = 0; index < statement.Branches.Count; index++)
        {
            output.Append(indent + (index == 0 ? "if " : "elif "));
            EmitExpression(output, statement.Branches[index].Condition, indent);
            output.AppendLine(":");
            EmitBody(output, statement.Branches[index].Elements, indent + "\t");
        }
        if (statement.ElseElements is null)
            return;
        output.AppendLine(indent + "else:");
        EmitBody(output, statement.ElseElements, indent + "\t");
    }

    private void EmitExpression(StringBuilder output, Expression expression, string indent)
    {
        switch (expression)
        {
            case LambdaExpression lambda:
                EmitLambda(output, lambda, indent);
                break;
            case CallableInvocationExpression callable:
                EmitExpression(output, callable.Target, indent);
                output.Append(".call");
                EmitArguments(output, callable.Arguments, indent);
                break;
            case CurrentInstanceExpression:
                output.Append("self");
                break;
            case ValueExpression value:
                output.Append(value.Text);
                break;
            case ConversionExpression conversion:
                EmitType(output, conversion.Type);
                output.Append('(');
                EmitExpression(output, conversion.Value, indent);
                output.Append(')');
                break;
            case ObjectCreationExpression creation:
                EmitType(output, creation.Type);
                if (creation.Kind == ConstructionKind.Object)
                    output.Append(".new");
                EmitArguments(output, creation.Arguments, indent);
                break;
            case ArrayExpression array:
                EmitArray(output, array, indent);
                break;
            case InvocationExpression invocation:
                EmitExpression(output, invocation.Target, indent);
                EmitArguments(output, invocation.Arguments, indent);
                break;
            case GodotGlobalFunctionExpression global:
                output.Append(godotTypes.GlobalFunctionName(global.Function, ScriptLanguage.GdScript));
                EmitArguments(output, global.Arguments, indent);
                break;
            case GodotMethodInvocationExpression method:
                EmitExpression(output, method.Target, indent);
                output.Append('.');
                output.Append(godotTypes.MethodName(method.Method, ScriptLanguage.GdScript));
                EmitArguments(output, method.Arguments, indent);
                break;
            case CollectionOperationExpression collection:
                EmitCollectionOperation(output, collection, indent);
                break;
            case BinaryExpression binary:
                EmitExpression(output, binary.Left, indent);
                output.Append(' ');
                output.Append(binary.Operator);
                output.Append(' ');
                EmitExpression(output, binary.Right, indent);
                break;
            case UnaryExpression unary:
                output.Append(unary.Operator);
                if (unary.Operator == "not")
                    output.Append(' ');
                EmitExpression(output, unary.Operand, indent);
                break;
            case ConditionalExpression conditional:
                EmitExpression(output, conditional.WhenTrue, indent);
                output.Append(" if ");
                EmitExpression(output, conditional.Condition, indent);
                output.Append(" else ");
                EmitExpression(output, conditional.WhenFalse, indent);
                break;
            case InterpolatedStringExpression interpolated:
                EmitInterpolatedString(output, interpolated, indent);
                break;
            case ParenthesizedExpression parenthesized:
                output.Append('(');
                EmitExpression(output, parenthesized.Expression, indent);
                output.Append(')');
                break;
            case MemberAccessExpression member:
                EmitExpression(output, member.Target, indent);
                output.Append('.');
                output.Append(member.Member);
                break;
            case GodotMemberAccessExpression member:
                EmitExpression(output, member.Target, indent);
                output.Append('.');
                output.Append(godotTypes.MemberName(member.Member, ScriptLanguage.GdScript));
                break;
            case IndexExpression index:
                EmitExpression(output, index.Target, indent);
                output.Append('[');
                EmitExpression(output, index.Index, indent);
                output.Append(']');
                break;
            default:
                throw new NotSupportedException("Unsupported language dialect.");
        }
    }

    // Reuses callable parameters and statements while preserving inline versus multiline layout.
    private void EmitLambda(StringBuilder output, LambdaExpression lambda, string indent)
    {
        output.Append("func ");
        EmitParameters(output, lambda.Parameters, indent);
        output.Append(')');
        if (lambda.ReturnType.Kind != TypeKind.Void)
        {
            output.Append(" -> ");
            EmitType(output, lambda.ReturnType);
        }
        output.Append(':');
        if (lambda.IsInline)
        {
            output.Append(' ');
            if (lambda.Elements.Single() is ReturnStatement statement)
            {
                output.Append("return ");
                EmitExpression(output, statement.Value!, indent);
            }
            else if (lambda.Elements.Single() is ExpressionStatement expression)
                EmitExpression(output, expression.Expression, indent);
            else
                throw new NotSupportedException("Unsupported language dialect.");
        }
        else
        {
            output.AppendLine();
            EmitBody(output, lambda.Elements, indent + "\t");
            // The enclosing declaration writes the last newline after the lambda expression.
            output.Length -= Environment.NewLine.Length;
        }
    }

    // All neutral collection operations are methods in the supported GDScript dialect.
    private void EmitCollectionOperation(
        StringBuilder output,
        CollectionOperationExpression expression,
        string indent)
    {
        EmitExpression(output, expression.Target, indent);
        output.Append('.');
        output.Append(CollectionMappings.GdScriptMember(expression.Operation));
        EmitArguments(output, expression.Arguments, indent);
    }

    private void EmitInterpolatedString(StringBuilder output, InterpolatedStringExpression expression, string indent)
    {
        output.Append('"');
        foreach (var part in expression.Parts)
        {
            if (part is InterpolationText text)
            {
                output.Append(text.Text.Replace("%", "%%"));
                continue;
            }
            var value = (InterpolationValue)part;
            if (value.Format is not null && value.Format.StartsWith('F'))
                output.Append("%." + value.Format[1..] + "f");
            else
                output.Append("%s");
        }
        output.Append("\" % [");
        var valueIndex = 0;
        foreach (var part in expression.Parts)
        {
            if (part is not InterpolationValue value)
                continue;
            if (valueIndex++ > 0)
                output.Append(", ");
            EmitExpression(output, value.Value, indent);
        }
        output.Append(']');
    }

    private void EmitArray(StringBuilder output, ArrayExpression array, string indent)
    {
        output.Append('[');
        for (var index = 0; index < array.Elements.Count; index++)
        {
            if (index > 0)
                output.Append(',');
            if (array.Elements[index].StartsOnNewLine)
            {
                output.AppendLine();
                output.Append(indent + "\t");
            }
            else if (index > 0)
                output.Append(' ');
            EmitExpression(output, array.Elements[index].Value, indent);
        }
        if (array.ClosingDelimiterStartsOnNewLine)
        {
            output.AppendLine();
            output.Append(indent);
        }
        output.Append(']');
    }

    // Reconstructs independent line breaks before arguments and the closing parenthesis.
    private void EmitArguments(StringBuilder output, ArgumentList arguments, string indent)
    {
        output.Append('(');
        for (var index = 0; index < arguments.Arguments.Count; index++)
        {
            if (index > 0)
                output.Append(',');
            if (arguments.Arguments[index].StartsOnNewLine)
            {
                output.AppendLine();
                output.Append(indent + "\t");
            }
            else if (index > 0)
                output.Append(' ');
            EmitExpression(output, arguments.Arguments[index].Value, indent);
        }
        if (arguments.ClosingParenthesisStartsOnNewLine)
        {
            output.AppendLine();
            output.Append(indent);
        }
        output.Append(')');
    }
}
