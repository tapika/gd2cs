using System.Text;
using gd2cs.Language;
using gd2cs.Model;
using gd2cs.Parsing;

namespace gd2cs.Emission;

public sealed class CSharpEmitter : IScriptEmitter
{
    private readonly GodotTypeCatalog godotTypes;
    private bool requiresGdArray;
    private bool requiresCallableValidity;
    private bool requiresArrayBuffers;
    private bool requiresCustomSort;
    private bool requiresStringify;
    private readonly HashSet<CollectionOperation> dictionaryArrayHelpers = new();
    // Each test gets a unique declaration: C# pattern variables also occupy the containing block's scope.
    private readonly Dictionary<TypeCheckExpression, string> patternNames = new(ReferenceEqualityComparer.Instance);
    private HashSet<string> occupiedNames = new(StringComparer.Ordinal);

    public CSharpEmitter(GodotTypeCatalog godotTypes)
    {
        this.godotTypes = godotTypes;
    }

    public string Emit(ScriptModel model)
    {
        requiresGdArray = false;
        requiresCallableValidity = false;
        requiresArrayBuffers = false;
        requiresCustomSort = false;
        requiresStringify = false;
        dictionaryArrayHelpers.Clear();
        patternNames.Clear();
        occupiedNames = EventParameterConversions.Names(model.Elements);
        var output = new StringBuilder();
        output.AppendLine("using Godot;");
        output.AppendLine("using System;");
        var usingOffset = output.Length;
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
        if (requiresArrayBuffers)
            output.Insert(usingOffset, "using System.Linq;" + Environment.NewLine);
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
                case PropertyDeclaration property:
                    EmitProperty(output, property, indent);
                    break;
                case SignalDeclaration signal:
                    output.AppendLine(indent + "    [Signal]");
                    output.Append(indent + "    public delegate void ");
                    output.Append(signal.Name);
                    output.Append("EventHandler");
                    EmitParameters(output, signal.Parameters, indent);
                    output.AppendLine(");");
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
                    if (method.Kind == MethodKind.StringConversion)
                        output.Append("override ");
                    EmitType(output, method.ReturnType);
                    output.Append(' ');
                    output.Append(method.Kind == MethodKind.StringConversion ? "ToString" : method.Name);
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
        if (isRoot && requiresCallableValidity)
            EmitCallableValidityHelper(output, indent);
        if (isRoot && requiresCustomSort)
            EmitCustomSortHelper(output, indent);
        if (isRoot && requiresStringify)
            EmitStringifyHelper(output, indent);
        if (isRoot)
            foreach (var operation in dictionaryArrayHelpers.Order())
                EmitDictionaryArrayHelper(output, operation, indent);
        output.AppendLine(indent + "}");
    }

    private void EmitConstant(StringBuilder output, ConstantDeclaration constant, string indent)
    {
        var isScalar = constant.Type.Kind is TypeKind.Bool or TypeKind.Int32 or TypeKind.Int64
            or TypeKind.Float32 or TypeKind.String;
        output.Append(indent + "    public ");
        output.Append((isScalar || constant.Value is GodotEnumExpression) && IsConstantExpression(constant.Value)
            ? "const " : "static readonly ");
        EmitType(output, constant.Type);
        output.Append(' ');
        output.Append(constant.Name);
        output.Append(" = ");
        EmitExpression(output, constant.Value, indent);
        output.AppendLine(";");
    }

    // Constructors and calls are runtime initializers, even when GDScript accepts them as const.
    // Keep scalar/enum constant expressions as C# const; use readonly for everything else.
    private static bool IsConstantExpression(Expression expression) => expression switch
    {
        ValueExpression or GodotEnumExpression => true,
        GodotStaticMemberExpression member => member.Member.Kind == GodotStaticMemberKind.Constant,
        UnaryExpression unary => IsConstantExpression(unary.Operand),
        BinaryExpression binary => IsConstantExpression(binary.Left) && IsConstantExpression(binary.Right),
        ParenthesizedExpression parenthesized => IsConstantExpression(parenthesized.Expression),
        ConversionExpression conversion => IsConstantExpression(conversion.Value),
        _ => false
    };

    private void EmitField(StringBuilder output, FieldDeclaration field, string indent)
    {
        output.Append(indent + "    public ");
        if (field.IsStatic)
            output.Append("static ");
        EmitType(output, field.Type);
        output.Append(' ');
        output.Append(field.Name);
        if (field.Initializer is null)
        {
            if (field.Type.Kind is TypeKind.Array or TypeKind.Dictionary)
                EmitDefaultInitializer(output, field.Type);
            else
                output.AppendLine(";");
            return;
        }
        output.Append(" = ");
        EmitExpression(output, field.Initializer, indent);
        output.AppendLine(";");
    }

    // Explicit accessor blocks share statement emission; setter aliases are a C#-only detail.
    private void EmitProperty(StringBuilder output, PropertyDeclaration property, string indent)
    {
        output.Append(indent + "    public ");
        if (property.IsStatic)
            output.Append("static ");
        EmitType(output, property.Type);
        output.Append(' ');
        output.AppendLine(property.Name);
        output.AppendLine(indent + "    {");
        foreach (var element in property.Elements)
        {
            if (element is not PropertyAccessorDeclaration accessor)
            {
                EmitBody(output, new List<SyntaxElement> { element }, indent + "        ");
                continue;
            }
            output.AppendLine(indent + "        " + (accessor.IsGetter ? "get" : "set"));
            output.AppendLine(indent + "        {");
            var body = accessor.Elements;
            if (!accessor.IsGetter && accessor.ParameterName != "value")
            {
                body = new List<SyntaxElement>
                {
                    new LocalVariableStatement(accessor.ParameterName!, property.Type, new ValueExpression("value"), LocalTyping.Inferred)
                };
                body.AddRange(accessor.Elements);
            }
            EmitBody(output, body, indent + "            ");
            output.AppendLine(indent + "        }");
        }
        output.AppendLine(indent + "    }");
    }

    private void EmitExpression(StringBuilder output, Expression expression, string indent)
    {
        switch (expression)
        {
            case BitmaskConditionExpression condition:
                EmitBitmaskTest(output, condition, indent, negated: false);
                break;
            case TypeCheckExpression test:
                EmitExpression(output, test.Value, indent);
                output.Append(test.IsNegated ? " is not " : " is ");
                EmitType(output, test.Type);
                if (test.Value is ValueExpression variable)
                {
                    var name = variable.Text + "Typed";
                    for (var suffix = 2; !occupiedNames.Add(name); suffix++)
                        name = variable.Text + "Typed" + suffix;
                    patternNames[test] = name;
                    output.Append(' ');
                    output.Append(name);
                }
                break;
            case NarrowedValueExpression narrowed:
                output.Append(patternNames[narrowed.Test]);
                break;
            case SignalConnectionExpression connection:
                EmitSignalConnection(output, connection, indent);
                break;
            case CallableMethodGroupExpression callable:
                EmitMethodGroup(output, callable, indent);
                break;
            case SignalEmissionExpression signal:
                output.Append("EmitSignal");
                var arguments = new List<Argument>
                {
                    new(new ValueExpression("SignalName." + signal.Name), false)
                };
                arguments.AddRange(signal.Arguments.Arguments);
                EmitArguments(output, signal.Arguments with { Arguments = arguments }, indent);
                break;
            case VariantCastExpression cast:
                EmitExpression(output, cast.Value, indent);
                if (cast.IsObjectCast)
                {
                    output.Append(".AsGodotObject() as ");
                    EmitType(output, cast.Type);
                }
                else
                {
                    output.Append(".As<");
                    EmitType(output, cast.Type);
                    output.Append(">()");
                }
                break;
            case SafeCastExpression cast:
                EmitExpression(output, cast.Value, indent);
                output.Append(" as ");
                EmitType(output, cast.Type);
                break;
            case TypeDefaultExpression:
                output.Append("default");
                break;
            case LambdaExpression lambda:
                EmitLambda(output, lambda, indent);
                break;
            case CallableInvocationExpression callable:
                EmitExpression(output, callable.Target, indent);
                output.Append(".Call");
                EmitArguments(output, callable.Arguments, indent);
                break;
            case StringifyExpression stringify:
                requiresStringify = true;
                output.Append("gdStr");
                // Explicit construction avoids params treating a lone default as a null array.
                EmitArguments(output, stringify.Arguments with
                {
                    Arguments = stringify.Arguments.Arguments.Select(argument => argument with
                    {
                        Value = argument.Value is ValueExpression { Text: "null" }
                            ? new ObjectCreationExpression(new TypeReference("Variant"), new ArgumentList(new List<Argument>(), false))
                            : argument.Value
                    }).ToList()
                }, indent);
                break;
            case ArrayBufferExpression buffer:
                requiresArrayBuffers = true;
                EmitExpression(output, buffer.Value, indent);
                output.Append(".ToArray()");
                break;
            case CallableValidityExpression validity:
                requiresCallableValidity = true;
                output.Append("gdCallableIsValid(");
                EmitExpression(output, validity.Target, indent);
                output.Append(')');
                break;
            case CallableNullExpression nullCheck:
                EmitExpression(output, nullCheck.Target, indent);
                output.Append(".Delegate is null");
                break;
            case CurrentInstanceExpression:
                output.Append("this");
                break;
            case ValueExpression value:
                output.Append(EscapeIdentifier(value.Text));
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
            case DictionaryExpression dictionary:
                EmitDictionary(output, dictionary, indent);
                break;
            case ArrayExpression array:
                EmitArray(output, array, indent);
                break;
            case InvocationExpression invocation:
                EmitInvocationTarget(output, invocation.Target, indent);
                EmitArguments(output, invocation.Arguments, indent);
                break;
            case ClampExpression clamp:
                output.Append(MathFunctionNames.ToCSharp("clamp"));
                EmitArguments(output, clamp.Arguments, indent);
                break;
            case MathFunctionExpression math:
                output.Append(MathFunctionNames.ToCSharp(math.Name));
                EmitArguments(output, math.Arguments, indent);
                break;
            case CustomSortExpression sort:
                requiresCustomSort = true;
                output.Append("gdSortCustom");
                EmitSortArguments(output, sort, indent);
                break;
            case GodotStaticMethodExpression method:
                output.Append(godotTypes.StaticMethodName(method.Method, ScriptLanguage.CSharp));
                EmitArguments(output, method.Arguments, indent);
                break;
            case GodotGlobalFunctionExpression global:
                output.Append(godotTypes.GlobalFunctionName(global.Function, ScriptLanguage.CSharp));
                EmitArguments(output, global.Arguments, indent);
                break;
            case GodotMethodInvocationExpression method:
                if (!method.IsImplicitReceiver)
                {
                    EmitExpression(output, method.Target, indent);
                    output.Append('.');
                }
                output.Append(godotTypes.MethodName(method.Method, ScriptLanguage.CSharp));
                EmitArguments(output, method.Arguments, indent);
                break;
            case StringOperationExpression operation:
                EmitExpression(output, operation.Target, indent);
                output.Append('.');
                output.Append(StringMappings.Member(operation.Operation, ScriptLanguage.CSharp));
                EmitArguments(output, StringMappings.CSharpArguments(operation.Operation, operation.Arguments), indent);
                break;
            case CollectionOperationExpression collection:
                EmitCollectionOperation(output, collection, indent);
                break;
            case BinaryExpression binary:
                EmitOperand(output, binary.Left, binary.Operator, false, indent);
                if (binary.LineBreaksBeforeOperator > 0)
                    EmitLineBreaks(output, binary.LineBreaksBeforeOperator, indent);
                else
                    output.Append(' ');
                output.Append(binary.Operator switch { "and" => "&&", "or" => "||", _ => binary.Operator });
                if (binary.LineBreaksAfterOperator > 0)
                    EmitLineBreaks(output, binary.LineBreaksAfterOperator, indent);
                else
                    output.Append(' ');
                EmitOperand(output, binary.Right, binary.Operator, true, indent);
                break;
            case UnaryExpression unary:
                if (unary is { Operator: "not", Operand: BitmaskConditionExpression mask })
                {
                    EmitBitmaskTest(output, mask, indent, negated: true);
                    break;
                }
                if (unary is { Operator: "not", Operand: CallableNullExpression callableNull })
                {
                    EmitExpression(output, callableNull.Target, indent);
                    output.Append(".Delegate is not null");
                    break;
                }
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
                var groupedIndent = indent + "    ";
                EmitLineBreaks(output, parenthesized.OpeningLineBreaks, groupedIndent);
                EmitExpression(output, parenthesized.Expression, groupedIndent);
                EmitLineBreaks(output, parenthesized.ClosingLineBreaks, indent);
                output.Append(')');
                break;
            case GodotEnumExpression constant:
                output.Append(godotTypes.EnumName(constant.Constant, ScriptLanguage.CSharp));
                break;
            case MemberAccessExpression member:
                EmitExpression(output, member.Target, indent);
                output.Append('.');
                output.Append(member.Member);
                break;
            case GodotStaticMemberExpression member:
                output.Append(godotTypes.StaticMemberName(member.Member, ScriptLanguage.CSharp));
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

    // Explicit generic arguments let C# select Callable.From's Action overload for the declared handler.
    private void EmitMethodGroup(StringBuilder output, CallableMethodGroupExpression callable, string indent)
    {
        output.Append("Callable.From");
        if (callable.ParameterTypes.Count > 0)
        {
            output.Append('<');
            for (var index = 0; index < callable.ParameterTypes.Count; index++)
            {
                if (index > 0)
                    output.Append(", ");
                EmitType(output, callable.ParameterTypes[index]);
            }
            output.Append('>');
        }
        output.Append('(');
        EmitExpression(output, callable.Target, indent);
        output.Append(')');
    }

    // Callable.From uses an Action overload for void; events need the same lambda without its factory.
    private void EmitLambda(StringBuilder output, LambdaExpression lambda, string indent, bool asCallable = true)
    {
        if (!asCallable)
        {
            var converted = EventParameterConversions.ForCSharp(lambda.Parameters, lambda.Elements);
            lambda = lambda with
            {
                Parameters = converted.Parameters,
                Elements = converted.Elements,
                IsInline = lambda.IsInline && converted.Elements.Count == lambda.Elements.Count
            };
        }
        if (asCallable)
            output.Append("Callable.From");
        var types = lambda.Parameters.Parameters.Select(parameter => parameter.Type).ToList();
        if (lambda.ReturnType.Kind != TypeKind.Void)
            types.Add(lambda.ReturnType);
        if (asCallable && types.Count > 0)
        {
            output.Append('<');
            for (var index = 0; index < types.Count; index++)
            {
                if (index > 0)
                    output.Append(", ");
                EmitType(output, types[index]);
            }
            output.Append('>');
        }
        if (asCallable)
            output.Append('(');
        EmitParameters(output, lambda.Parameters, indent);
        output.Append(") =>");
        if (lambda.IsInline)
        {
            output.Append(' ');
            var value = lambda.Elements.Single() switch
            {
                ReturnStatement statement => statement.Value!,
                ExpressionStatement statement => statement.Expression,
                _ => throw new NotSupportedException("Unsupported language dialect.")
            };
            EmitExpression(output, value, indent);
        }
        else
        {
            output.AppendLine();
            output.AppendLine(indent + "{");
            EmitBody(output, lambda.Elements, indent + "    ");
            output.Append(indent + "}");
        }
        if (asCallable)
            output.Append(')');
    }

    // Enum flags and integer masks both support comparison with the zero literal.
    private void EmitBitmaskTest(StringBuilder output, BitmaskConditionExpression condition, string indent, bool negated)
    {
        var grouped = condition.Value is ParenthesizedExpression;
        if (!grouped)
            output.Append('(');
        EmitExpression(output, condition.Value, indent);
        if (!grouped)
            output.Append(')');
        output.Append(negated ? " == 0" : " != 0");
    }

    // Parentheses follow the target language's precedence, never the source spelling.
    private void EmitOperand(StringBuilder output, Expression child, string operation, bool right, string indent)
    {
        var grouped = ExpressionOperators.NeedsParentheses(child, operation, right, ScriptLanguage.CSharp);
        if (grouped)
            output.Append('(');
        EmitExpression(output, child, indent);
        if (grouped)
            output.Append(')');
    }

    // Direct subscriptions preserve method-group identity for later -= removal.
    private void EmitSignalConnection(StringBuilder output, SignalConnectionExpression connection, string indent)
    {
        // Flags have no CLR event equivalent; retain the engine connection API and callback factory.
        if (connection.Arguments.Arguments.Count > 1)
        {
            EmitExpression(output, connection.Target, indent);
            output.Append(".Connect");
            var arguments = new List<Argument>
            {
                new(new MemberAccessExpression(
                    new MemberAccessExpression(new ValueExpression(connection.Signal.DeclaringType), "SignalName"),
                    connection.Signal.Name), false)
            };
            arguments.AddRange(connection.Arguments.Arguments.Select((argument, index) => index == 1
                ? argument with { Value = new ConversionExpression(new TypeReference("uint"), argument.Value) }
                : argument));
            EmitArguments(output, connection.Arguments with { Arguments = arguments }, indent);
            return;
        }
        EmitExpression(output, connection.Target, indent);
        output.Append('.');
        output.Append(connection.Signal.Name);
        output.Append(connection.IsDisconnect ? " -= " : " += ");
        var callback = connection.Arguments.Arguments.Single();
        var hasLayout = callback.StartsOnNewLine || connection.Arguments.ClosingParenthesisStartsOnNewLine;
        if (hasLayout)
            output.Append('(');
        if (callback.StartsOnNewLine)
        {
            output.AppendLine();
            output.Append(indent + "    ");
        }
        if (callback.Value is CallableMethodGroupExpression method)
            EmitExpression(output, method.Target, indent);
        else if (callback.Value is LambdaExpression lambda)
            EmitLambda(output, lambda, indent, asCallable: false);
        else
            throw new NotSupportedException("Signal events require a named method or inline lambda, not a Callable value.");
        if (connection.Arguments.ClosingParenthesisStartsOnNewLine)
        {
            output.AppendLine();
            output.Append(indent);
        }
        if (hasLayout)
            output.Append(')');
    }

    // Global math calls use their C# spelling; member and other call targets remain structural.
    private void EmitInvocationTarget(StringBuilder output, Expression target, string indent)
    {
        if (target is ValueExpression value)
        {
            output.Append(value.Text);
            return;
        }
        EmitExpression(output, target, indent);
    }

    // Named predicates remain method groups; lambda predicates are Func delegates, not Godot Callables.
    private void EmitSortArguments(StringBuilder output, CustomSortExpression sort, string indent)
    {
        output.Append('(');
        EmitExpression(output, sort.Target, indent);
        output.Append(',');
        var argument = sort.Arguments.Arguments.Single();
        if (argument.StartsOnNewLine)
        {
            output.AppendLine();
            output.Append(indent + "    ");
        }
        else
            output.Append(' ');
        if (argument.Value is LambdaExpression lambda)
            EmitLambda(output, lambda, indent, asCallable: false);
        else
            EmitExpression(output, argument.Value, indent);
        if (sort.Arguments.ClosingParenthesisStartsOnNewLine)
        {
            output.AppendLine();
            output.Append(indent);
        }
        output.Append(')');
    }

    // Collection operations are already type-bound, so emission is a dialect-only decision.
    private void EmitCollectionOperation(
        StringBuilder output,
        CollectionOperationExpression expression,
        string indent)
    {
        if (expression.Operation is CollectionOperation.Keys or CollectionOperation.Values)
        {
            dictionaryArrayHelpers.Add(expression.Operation);
            output.Append(CollectionMappings.DictionaryArrayHelper(expression.Operation));
            EmitArguments(output, expression.Arguments with
            {
                Arguments = new List<Argument> { new(expression.Target, false) }
            }, indent);
            return;
        }
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

    // Reconstructs entry boundaries without adding line breaks to compact literals.
    private void EmitDictionary(StringBuilder output, DictionaryExpression dictionary, string indent)
    {
        output.Append("new ");
        EmitType(output, dictionary.Type!);
        output.Append(" {");
        for (var index = 0; index < dictionary.Entries.Count; index++)
        {
            var entry = dictionary.Entries[index];
            if (index > 0)
                output.Append(',');
            for (var line = 0; line < entry.LeadingLineBreaks; line++)
                output.AppendLine();
            if (entry.LeadingLineBreaks > 0)
                output.Append(indent + "    ");
            else if (index > 0)
                output.Append(' ');
            output.Append("{ ");
            EmitExpression(output, entry.Key, indent + "    ");
            output.Append(", ");
            EmitExpression(output, entry.Value, indent + "    ");
            output.Append(" }");
        }
        if (dictionary.HasTrailingComma)
            output.Append(',');
        for (var line = 0; line < dictionary.ClosingLineBreaks; line++)
            output.AppendLine();
        if (dictionary.ClosingLineBreaks > 0)
            output.Append(indent);
        output.Append('}');
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
        var converted = EventParameterConversions.ForCSharp(callable.Parameters, callable.Elements);
        EmitParameters(output, converted.Parameters, indent);
        output.AppendLine(")");
        output.AppendLine(indent + "    {");
        EmitBody(output, converted.Elements, indent + "        ");
        output.AppendLine(indent + "    }");
    }

    // Reconstructs parameter line breaks independently from the callable body.
    private void EmitParameters(StringBuilder output, ParameterList parameters, string indent)
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
            output.Append(EscapeIdentifier(parameters.Parameters[index].Name));
            if (parameters.Parameters[index].DefaultValue is { } defaultValue)
            {
                output.Append(" = ");
                EmitExpression(output, defaultValue, indent);
            }
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
                    output.Append(' ');
                    output.Append(statement.Operator);
                    output.Append(' ');
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
                    var useVar = statement.Typing is
                        LocalTyping.Inferred or
                        LocalTyping.Dynamic;
                    if (useVar)
                        output.Append("var");
                    else
                        EmitType(output, statement.Type ?? throw SourceLocations.Error(statement, "Explicit local requires a type."));
                    output.Append(' ');
                    output.Append(statement.Name);
                    if (statement.Initializer is null)
                    {
                        EmitDefaultInitializer(output, statement.Type ?? throw SourceLocations.Error(statement, "Explicit local requires a type."));
                        break;
                    }
                    output.Append(" = ");
                    EmitExpression(output, statement.Initializer, indent);
                    output.AppendLine(";");
                    break;
                case ForEachStatement statement:
                    output.Append(indent + "foreach (");
                    if (statement.VariableType is { } variableType)
                        EmitType(output, variableType);
                    else
                        output.Append("var");
                    output.Append(' ');
                    output.Append(statement.Variable);
                    output.Append(" in ");
                    EmitExpression(output, statement.Collection, indent);
                    if (statement.IteratesDictionaryKeys)
                        output.Append(".Keys");
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
        // Godot containers start empty, unlike ordinary object references which start null.
        if (type.Kind is TypeKind.Array or TypeKind.Dictionary)
        {
            output.Append("new ");
            EmitType(output, type);
            output.AppendLine("();");
            return;
        }
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

    // The concrete Array<T> cast preserves Godot's snapshot without an additional allocation/copy.
    private static void EmitDictionaryArrayHelper(StringBuilder output, CollectionOperation operation, string indent)
    {
        var element = operation == CollectionOperation.Keys ? "TKey" : "TValue";
        var property = operation == CollectionOperation.Keys ? "Keys" : "Values";
        output.AppendLine();
        output.Append(indent + "    private static Godot.Collections.Array<");
        output.Append(element);
        output.Append("> ");
        output.Append(CollectionMappings.DictionaryArrayHelper(operation));
        output.AppendLine("<[MustBeVariant] TKey, [MustBeVariant] TValue>(Godot.Collections.Dictionary<TKey, TValue> dictionary)");
        output.AppendLine(indent + "    {");
        output.AppendLine(indent + "        // Godot internally returns Array<T> from Keys/Values, exposed as ICollection<T>.");
        output.AppendLine(indent + "        // https://github.com/godotengine/godot/blob/master/modules/mono/glue/GodotSharp/GodotSharp/Core/Dictionary.cs");
        output.Append(indent + "        return (Godot.Collections.Array<");
        output.Append(element);
        output.Append(">)dictionary.");
        output.Append(property);
        output.AppendLine(";");
        output.AppendLine(indent + "    }");
    }

    // IList<T> is implemented by Godot Array<T>; using it avoids additional Variant generic constraints.
    private static void EmitCustomSortHelper(StringBuilder output, string indent)
    {
        output.AppendLine();
        output.AppendLine(indent + "    private static void gdSortCustom<T>(System.Collections.Generic.IList<T> values, Func<T, T, bool> before)");
        output.AppendLine(indent + "    {");
        output.AppendLine(indent + "        // Godot's C# Array API is missing GDScript's sort_custom function.");
        output.AppendLine(indent + "        // Adapt its strict Boolean ordering; equal-key order is not guaranteed.");
        output.AppendLine(indent + "        ArgumentNullException.ThrowIfNull(before);");
        output.AppendLine(indent + "        if (values.IsReadOnly) throw new InvalidOperationException(\"The array is read-only.\");");
        output.AppendLine(indent + "        var sorted = new System.Collections.Generic.List<T>(values);");
        output.AppendLine(indent + "        sorted.Sort((first, second) => before(first, second) ? -1 : before(second, first) ? 1 : 0);");
        output.AppendLine(indent + "        // Write back into the same array so aliases still observe the sorted contents.");
        output.AppendLine(indent + "        for (var index = 0; index < sorted.Count; index++) values[index] = sorted[index];");
        output.AppendLine(indent + "    }");
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

    // Variant.ToString uses Godot's conversion, including nil, rather than CLR object formatting.
    private static void EmitStringifyHelper(StringBuilder output, string indent)
    {
        output.AppendLine();
        output.AppendLine(indent + "    private static string gdStr(params Variant[] values)");
        output.AppendLine(indent + "    {");
        output.AppendLine(indent + "        var result = new System.Text.StringBuilder();");
        output.AppendLine(indent + "        foreach (var value in values)");
        output.AppendLine(indent + "            result.Append(value.ToString());");
        output.AppendLine(indent + "        return result.ToString();");
        output.AppendLine(indent + "    }");
    }

    // Callable has no public IsValid API. Match the supported native callable representations:
    // delegates may have no Godot target; standard callables require a live target and method.
    // Passing by value evaluates a receiver expression exactly once, including in short-circuit tests.
    private static void EmitCallableValidityHelper(StringBuilder output, string indent)
    {
        output.AppendLine();
        output.AppendLine(indent + "    private static bool gdCallableIsValid(Callable callback)");
        output.AppendLine(indent + "    {");
        output.AppendLine(indent + "        // Callable.IsValid() is not exposed by Godot's C# API yet.");
        output.AppendLine(indent + "        // Mirrors the supported cases of Callable::is_valid() and CallableCustom::is_valid():");
        output.AppendLine(indent + "        // https://github.com/godotengine/godot/blob/master/core/variant/callable.cpp");
        output.AppendLine(indent + "        // Managed delegates may target an ordinary object or no object at all.");
        output.AppendLine(indent + "        if (callback.Delegate is not null)");
        output.AppendLine(indent + "            return callback.Target is null || GodotObject.IsInstanceValid(callback.Target);");
        output.AppendLine(indent + "        return GodotObject.IsInstanceValid(callback.Target) && callback.Target.HasMethod(callback.Method);");
        output.AppendLine(indent + "    }");
    }

    // Indent only the resumed line, retaining intentional blank continuation lines.
    private static void EmitLineBreaks(StringBuilder output, int count, string indent)
    {
        for (var index = 0; index < count; index++)
            output.AppendLine();
        if (count > 0)
            output.Append(indent);
    }

    private static void EmitType(StringBuilder output, TypeReference type)
    {
        if (type.Kind == TypeKind.ArrayBuffer)
        {
            EmitType(output, type.TypeArguments.Single());
            output.Append("[]");
            return;
        }
        var name = type.Kind switch { TypeKind.String => "string", TypeKind.Int64 => "long", TypeKind.Float64 => "double", _ => type.Name };
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

    // Godot input callbacks conventionally use event, which is a reserved C# keyword.
    private static string EscapeIdentifier(string name) => name == "event" ? "@event" : name;
}
