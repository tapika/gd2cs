using System.Text;
using gd2cs.Language;
using gd2cs.Model;

namespace gd2cs.Emission;

public sealed class CSharpEmitter : IScriptEmitter
{
    private readonly GodotTypeCatalog godotTypes;
    private bool requiresGdArray;

    public CSharpEmitter(GodotTypeCatalog godotTypes)
    {
        this.godotTypes = godotTypes;
    }

    public string Emit(ScriptModel model)
    {
        requiresGdArray = false;
        var output = new StringBuilder();
        output.AppendLine("using Godot;");
        output.AppendLine("using System;");
        output.AppendLine();
        foreach (var element in model.Elements)
        {
            switch (element)
            {
                case CommentElement comment:
                    output.Append("// ");
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
            output.AppendLine(indent + "[GlobalClass]");
        output.Append(indent + "public partial class ");
        output.Append(declaration.Name);
        output.Append(" : ");
        EmitType(output, declaration.BaseType);
        output.AppendLine();
        output.AppendLine(indent + "{");
        foreach (var element in declaration.Elements)
        {
            switch (element)
            {
                case CommentElement comment:
                    output.Append(indent + "    // ");
                    output.AppendLine(comment.Text);
                    break;
                case BlankLineElement:
                    output.AppendLine();
                    break;
                case ConstantDeclaration constant:
                    EmitConstant(output, constant, indent);
                    break;
                case FieldDeclaration field:
                    EmitField(output, field, indent);
                    break;
                case ConstructorDeclaration constructor:
                    output.Append(indent + "    public ");
                    output.Append(declaration.Name);
                    EmitCallable(output, constructor, indent);
                    break;
                case MethodDeclaration method:
                    output.Append(indent + "    public ");
                    if (method.IsStatic)
                        output.Append("static ");
                    EmitType(output, method.ReturnType);
                    output.Append(' ');
                    output.Append(method.Name);
                    EmitCallable(output, method, indent);
                    break;
                case DestructorDeclaration destructor:
                    output.Append(indent + "    ~");
                    output.Append(declaration.Name);
                    EmitCallable(output, destructor, indent);
                    break;
                case NestedClassDeclaration nested:
                    EmitClass(output, new ClassDeclaration(nested.Name, nested.BaseType, false, nested.Elements), indent + "    ", false);
                    break;
                default:
                    throw new NotSupportedException("Unsupported language dialect.");
            }
        }
        if (isRoot && requiresGdArray)
            EmitGdArrayHelper(output, indent);
        output.AppendLine(indent + "}");
    }

    private void EmitConstant(StringBuilder output, ConstantDeclaration constant, string indent)
    {
        var isArray = constant.Value is ArrayExpression;
        output.Append(indent + "    public ");
        output.Append(isArray ? "static readonly " : "const ");
        EmitType(output, constant.Type);
        output.Append(' ');
        output.Append(constant.Name);
        output.Append(" = ");
        EmitExpression(output, constant.Value, indent);
        output.AppendLine(";");
    }

    private void EmitField(StringBuilder output, FieldDeclaration field, string indent)
    {
        output.Append(indent + "    public ");
        EmitType(output, field.Type);
        output.Append(' ');
        output.Append(field.Name);
        if (field.Initializer is null)
        {
            output.AppendLine(";");
            return;
        }
        output.Append(" = ");
        EmitExpression(output, field.Initializer, indent);
        output.AppendLine(";");
    }

    private void EmitExpression(StringBuilder output, Expression expression, string indent)
    {
        switch (expression)
        {
            case ValueExpression value:
                output.Append(value.Text);
                if (value.Text.Length > 0 && char.IsDigit(value.Text[0]) && value.Text.Contains('.') && !value.Text.EndsWith('f'))
                    output.Append('f');
                break;
            case ConversionExpression conversion:
                output.Append('(');
                EmitType(output, conversion.Type);
                output.Append(")(");
                EmitExpression(output, conversion.Value, indent);
                output.Append(')');
                break;
            case ObjectCreationExpression creation:
                output.Append("new ");
                EmitType(output, creation.Type);
                EmitArguments(output, creation.Arguments, indent);
                break;
            case ArrayExpression array:
                EmitArray(output, array, indent);
                break;
            case InvocationExpression invocation:
                EmitInvocationTarget(output, invocation.Target, indent);
                EmitArguments(output, invocation.Arguments, indent);
                break;
            case GodotGlobalFunctionExpression global:
                output.Append(godotTypes.GlobalFunctionName(global.Function, ScriptLanguage.CSharp));
                EmitArguments(output, global.Arguments, indent);
                break;
            case GodotMethodInvocationExpression method:
                EmitExpression(output, method.Target, indent);
                output.Append('.');
                output.Append(godotTypes.MethodName(method.Method, ScriptLanguage.CSharp));
                EmitArguments(output, method.Arguments, indent);
                break;
            case CollectionOperationExpression collection:
                EmitCollectionOperation(output, collection, indent);
                break;
            case BinaryExpression binary:
                EmitExpression(output, binary.Left, indent);
                output.Append(' ');
                output.Append(binary.Operator switch { "and" => "&&", "or" => "||", _ => binary.Operator });
                output.Append(' ');
                EmitExpression(output, binary.Right, indent);
                break;
            case UnaryExpression unary:
                if (unary is { Operator: "not", Operand: CollectionOperationExpression { Operation: CollectionOperation.IsEmpty } empty })
                {
                    EmitExpression(output, empty.Target, indent);
                    output.Append(".Count != 0");
                    break;
                }
                output.Append(unary.Operator == "not" ? "!" : unary.Operator);
                EmitExpression(output, unary.Operand, indent);
                break;
            case ConditionalExpression conditional:
                EmitExpression(output, conditional.Condition, indent);
                output.Append(" ? ");
                EmitExpression(output, conditional.WhenTrue, indent);
                output.Append(" : ");
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
                output.Append(godotTypes.MemberName(member.Member, ScriptLanguage.CSharp));
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

    // Global math calls use their C# spelling; member and other call targets remain structural.
    private void EmitInvocationTarget(StringBuilder output, Expression target, string indent)
    {
        if (target is ValueExpression value)
        {
            output.Append(MathFunctionNames.ToCSharp(value.Text));
            return;
        }
        EmitExpression(output, target, indent);
    }

    // Collection operations are already type-bound, so emission is a dialect-only decision.
    private void EmitCollectionOperation(
        StringBuilder output,
        CollectionOperationExpression expression,
        string indent)
    {
        EmitExpression(output, expression.Target, indent);
        if (expression.Operation == CollectionOperation.Count)
        {
            output.Append(".Count");
            return;
        }
        if (expression.Operation == CollectionOperation.IsEmpty)
        {
            output.Append(".Count == 0");
            return;
        }
        output.Append('.');
        output.Append(CollectionMappings.CSharpMember(expression.Operation));
        EmitArguments(output, expression.Arguments, indent);
    }

    private void EmitInterpolatedString(StringBuilder output, InterpolatedStringExpression expression, string indent)
    {
        output.Append("$\"");
        foreach (var part in expression.Parts)
        {
            switch (part)
            {
                case InterpolationText text:
                    output.Append(text.Text.Replace("{", "{{").Replace("}", "}}"));
                    break;
                case InterpolationValue value:
                    output.Append('{');
                    EmitExpression(output, value.Value, indent);
                    if (value.Format is not null)
                    {
                        output.Append(':');
                        output.Append(value.Format);
                    }
                    output.Append('}');
                    break;
            }
        }
        output.Append('"');
    }

    private void EmitArray(StringBuilder output, ArrayExpression array, string indent)
    {
        requiresGdArray = true;
        output.Append("gdArray(");
        for (var index = 0; index < array.Elements.Count; index++)
        {
            if (index > 0)
                output.Append(',');
            if (array.Elements[index].StartsOnNewLine)
            {
                output.AppendLine();
                output.Append(indent + "        ");
            }
            else if (index > 0)
                output.Append(' ');
            EmitExpression(output, array.Elements[index].Value, indent);
        }
        if (array.ClosingDelimiterStartsOnNewLine)
        {
            output.AppendLine();
            output.Append(indent + "    ");
        }
        output.Append(')');
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
                output.Append(indent + "    ");
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

    private void EmitCallable(StringBuilder output, CallableDeclaration callable, string indent)
    {
        EmitParameters(output, callable.Parameters, indent);
        output.AppendLine(")");
        output.AppendLine(indent + "    {");
        EmitBody(output, callable.Elements, indent + "        ");
        output.AppendLine(indent + "    }");
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
                output.Append(indent + "        ");
            }
            else if (index > 0)
                output.Append(' ');
            EmitType(output, parameters.Parameters[index].Type);
            output.Append(' ');
            output.Append(parameters.Parameters[index].Name);
        }
        if (parameters.ClosingParenthesisStartsOnNewLine)
        {
            output.AppendLine();
            output.Append(indent + "    ");
        }
    }

    private void EmitBody(StringBuilder output, List<SyntaxElement> elements, string indent)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case CommentElement comment:
                    output.Append(indent + "// ");
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
                    output.AppendLine(";");
                    break;
                case ReturnStatement statement:
                    output.Append(indent + "return");
                    if (statement.Value is not null)
                    {
                        output.Append(' ');
                        EmitExpression(output, statement.Value, indent);
                    }
                    output.AppendLine(";");
                    break;
                case ExpressionStatement statement:
                    output.Append(indent);
                    EmitExpression(output, statement.Expression, indent);
                    output.AppendLine(";");
                    break;
                case LocalVariableStatement statement:
                    output.Append(indent);
                    if (statement.IsInferred)
                        output.Append("var");
                    else
                        EmitType(output, statement.Type ?? throw new InvalidOperationException("Explicit local requires a type."));
                    output.Append(' ');
                    output.Append(statement.Name);
                    if (statement.Initializer is null)
                    {
                        EmitDefaultInitializer(output, statement.Type ?? throw new InvalidOperationException("Explicit local requires a type."));
                        break;
                    }
                    output.Append(" = ");
                    EmitExpression(output, statement.Initializer, indent);
                    output.AppendLine(";");
                    break;
                case ForEachStatement statement:
                    output.Append(indent + "foreach (var ");
                    output.Append(statement.Variable);
                    output.Append(" in ");
                    EmitExpression(output, statement.Collection, indent);
                    output.AppendLine(")");
                    EmitBlock(output, statement.Elements, indent);
                    break;
                case WhileStatement statement:
                    output.Append(indent + "while (");
                    EmitExpression(output, statement.Condition, indent);
                    output.AppendLine(")");
                    EmitBlock(output, statement.Elements, indent);
                    break;
                case ConditionalStatement statement:
                    EmitConditional(output, statement, indent);
                    break;
                case BreakStatement:
                    output.AppendLine(indent + "break;");
                    break;
                case ContinueStatement:
                    output.AppendLine(indent + "continue;");
                    break;
                default:
                    throw new NotSupportedException("Unsupported language dialect.");
            }
        }
    }

    // GDScript initializes typed locals immediately, while C# requires definite assignment before reads.
    private static void EmitDefaultInitializer(StringBuilder output, TypeReference type)
    {
        output.Append(" = ");
        output.Append(type.Kind switch
        {
            TypeKind.Bool => "false",
            TypeKind.Int32 or TypeKind.Int64 => "0",
            TypeKind.Float32 => "0.0f",
            TypeKind.Float64 => "0.0",
            TypeKind.String => "string.Empty",
            _ when type.Kind is TypeKind.StringName or TypeKind.NodePath or TypeKind.Callable => "default",
            _ => "null"
        });
        output.AppendLine(";");
    }

    private void EmitBlock(StringBuilder output, List<SyntaxElement> elements, string indent)
    {
        output.AppendLine(indent + "{");
        EmitBody(output, elements, indent + "    ");
        output.AppendLine(indent + "}");
    }

    private void EmitConditional(StringBuilder output, ConditionalStatement statement, string indent)
    {
        for (var index = 0; index < statement.Branches.Count; index++)
        {
            output.Append(indent + (index == 0 ? "if (" : "else if ("));
            EmitExpression(output, statement.Branches[index].Condition, indent);
            output.AppendLine(")");
            EmitBlock(output, statement.Branches[index].Elements, indent);
        }
        if (statement.ElseElements is null)
            return;
        output.AppendLine(indent + "else");
        EmitBlock(output, statement.ElseElements, indent);
    }

    private static void EmitGdArrayHelper(StringBuilder output, string indent)
    {
        output.AppendLine();
        output.AppendLine(indent + "    private static Godot.Collections.Array<T> gdArray<[MustBeVariant] T>(params T[] values)");
        output.AppendLine(indent + "    {");
        output.AppendLine(indent + "        var result = new Godot.Collections.Array<T>();");
        output.AppendLine(indent + "        foreach (var value in values) result.Add(value);");
        output.AppendLine(indent + "        return result;");
        output.AppendLine(indent + "    }");
    }

    private static void EmitType(StringBuilder output, TypeReference type)
    {
        var name = type.Kind == TypeKind.String ? "string" : type.Name;
        output.Append(type.Kind is TypeKind.Array or TypeKind.Dictionary
            ? "Godot.Collections." + name
            : name);
        if (type.TypeArguments.Count == 0)
            return;
        output.Append('<');
        for (var index = 0; index < type.TypeArguments.Count; index++)
        {
            if (index > 0)
                output.Append(", ");
            EmitType(output, type.TypeArguments[index]);
        }
        output.Append('>');
    }
}
