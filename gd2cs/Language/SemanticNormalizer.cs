using gd2cs.Model;

namespace gd2cs.Language;

/*
Detailed design
---------------
The parsers intentionally retain the source expression shape: GDScript `items.size()` is
an invocation whose target is a member access, while C# `items.Count` is a member access.
Neither parser can safely translate those spellings by itself because a user class may
legitimately declare the same members. This pass binds the receiver before normalizing it.

Binding has two phases for every class. First, all fields and constants are collected so a
method can refer to a member declared later in the file. Second, expressions are rebuilt in
source order while callable scopes track parameters, inferred/explicit locals, and loop
variables. Only an Array or Dictionary receiver may use CollectionMappings. An unresolved
receiver remains ordinary syntax rather than being guessed to be a collection.

The normalized model contains CollectionOperationExpression nodes, never dialect names such
as `size`, `Count`, `append`, or `Add`. Operations that share GDScript syntax but differ in
meaning are separated after type binding: Array.has is ContainsValue, while Dictionary.has
is ContainsKey. Emitters therefore perform a mechanical target-language lookup and never
need to rediscover types. C#'s `Count == 0` and `Count != 0` patterns normalize to IsEmpty and
`not IsEmpty`, making them identical to GDScript `is_empty()` and `not is_empty()`.

Construction is normalized in the same traversal. A simple GDScript call is changed into
ObjectCreationExpression only when GodotTypeCatalog confirms a built-in value constructor
with a compatible argument count. C# `new` expressions receive the same classification.
The model therefore distinguishes semantic value construction from ordinary calls without
relying on capitalization or embedding target-language `new`/`.new` spellings.
*/
internal sealed class SemanticNormalizer
{
    private static readonly ArgumentList noArguments = new(new List<Argument>(), false);
    private readonly ScriptLanguage language;
    private readonly GodotTypeCatalog godotTypes;

    public SemanticNormalizer(ScriptLanguage language, GodotTypeCatalog godotTypes)
    {
        this.language = language;
        this.godotTypes = godotTypes;
    }

    // Preserves top-level trivia while normalizing every class independently.
    public ScriptModel Normalize(ScriptModel model)
    {
        var elements = new List<SyntaxElement>();
        foreach (var element in model.Elements)
        {
            elements.Add(element is ClassDeclaration declaration
                ? NormalizeClass(declaration)
                : element);
        }
        return new ScriptModel(elements);
    }

    // Collects declarations before rewriting any initializer or callable body.
    private ClassDeclaration NormalizeClass(ClassDeclaration declaration)
    {
        var members = new Dictionary<string, TypeReference>(StringComparer.Ordinal);
        var methods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in declaration.Elements)
        {
            if (element is FieldDeclaration field)
                members[field.Name] = field.Type;
            else if (element is ConstantDeclaration constant)
                members[constant.Name] = constant.Type;
            else if (element is MethodDeclaration method)
                methods.Add(method.Name);
        }

        var scope = new BindingScope(members, methods, declaration.BaseType);
        var elements = new List<SyntaxElement>();
        foreach (var element in declaration.Elements)
            elements.Add(NormalizeClassElement(element, scope));
        return declaration with { Elements = elements };
    }

    // Recurses only into class elements that can contain expressions or nested scopes.
    private SyntaxElement NormalizeClassElement(SyntaxElement element, BindingScope scope) => element switch
    {
        FieldDeclaration field => field with
        {
            Initializer = field.Initializer is null ? null : NormalizeExpression(field.Initializer, scope)
        },
        NestedClassDeclaration nested => NormalizeNestedClass(nested),
        ConstructorDeclaration constructor => constructor with
        {
            Elements = NormalizeCallable(constructor.Parameters, constructor.Elements, scope)
        },
        MethodDeclaration method => method with
        {
            Elements = NormalizeCallable(method.Parameters, method.Elements, scope)
        },
        DestructorDeclaration destructor => destructor with
        {
            Elements = NormalizeCallable(destructor.Parameters, destructor.Elements, scope)
        },
        _ => element
    };

    // Reuses root-class binding so nested classes receive their own member namespace.
    private NestedClassDeclaration NormalizeNestedClass(NestedClassDeclaration nested)
    {
        var normalized = NormalizeClass(new ClassDeclaration(nested.Name, nested.BaseType, false, nested.Elements));
        return nested with { Elements = normalized.Elements };
    }

    // Parameters and locals live only for the duration of their owning callable.
    private List<SyntaxElement> NormalizeCallable(
        ParameterList parameters,
        List<SyntaxElement> elements,
        BindingScope scope)
    {
        scope.Push();
        try
        {
            foreach (var parameter in parameters.Parameters)
                scope.Declare(parameter.Name, parameter.Type);
            return NormalizeBody(elements, scope);
        }
        finally
        {
            scope.Pop();
        }
    }

    // Rebuilds statements in order because each local becomes visible only after its declaration.
    private List<SyntaxElement> NormalizeBody(List<SyntaxElement> elements, BindingScope scope)
    {
        var result = new List<SyntaxElement>();
        foreach (var element in elements)
        {
            var normalized = NormalizeBodyElement(element, scope);
            result.Add(normalized);
            if (normalized is LocalVariableStatement local)
            {
                // Inferred locals become bindable from the normalized initializer type.
                var type = local.Type ?? ResolveType(local.Initializer, scope);
                if (type is not null)
                    scope.Declare(local.Name, type);
            }
        }
        return result;
    }

    // Rebuilds expressions and recursively isolates block-local declarations.
    private SyntaxElement NormalizeBodyElement(SyntaxElement element, BindingScope scope) => element switch
    {
        AssignmentStatement statement => statement with
        {
            Target = NormalizeExpression(statement.Target, scope),
            Value = NormalizeExpression(statement.Value, scope)
        },
        ReturnStatement statement => statement with
        {
            Value = statement.Value is null ? null : NormalizeExpression(statement.Value, scope)
        },
        ExpressionStatement statement => statement with
        {
            Expression = NormalizeExpression(statement.Expression, scope)
        },
        LocalVariableStatement statement => NormalizeLocal(statement, scope),
        ForEachStatement statement => NormalizeForEach(statement, scope),
        WhileStatement statement => statement with
        {
            Condition = NormalizeExpression(statement.Condition, scope),
            Elements = NormalizeNestedBody(statement.Elements, scope)
        },
        ConditionalStatement statement => NormalizeConditional(statement, scope),
        _ => element
    };

    // Retains static typing when GDScript cannot infer it from the emitted expression.
    private LocalVariableStatement NormalizeLocal(LocalVariableStatement statement, BindingScope scope)
    {
        var initializer = statement.Initializer is null
            ? null
            : NormalizeExpression(statement.Initializer, scope);
        var type = statement.Type ?? ResolveType(initializer, scope);
        var typing = statement.Typing;
        if (typing == LocalTyping.Inferred && !CanGdScriptInfer(initializer))
            typing = type is null ? LocalTyping.Dynamic : LocalTyping.InferredWithExplicitGdType;
        else if (language == ScriptLanguage.GdScript &&
                 typing == LocalTyping.Explicit &&
                 initializer is not null &&
                 !CanGdScriptInfer(initializer))
            typing = LocalTyping.InferredWithExplicitGdType;
        return statement with
        {
            Type = type,
            Initializer = initializer,
            Typing = typing
        };
    }

    // Indexed collection access can remain Variant to Godot even when the neutral model knows T.
    private static bool CanGdScriptInfer(Expression? expression) => expression switch
    {
        IndexExpression => false,
        ParenthesizedExpression parenthesized => CanGdScriptInfer(parenthesized.Expression),
        _ => true
    };

    // Derives a loop variable from the collection element type when it is available.
    private ForEachStatement NormalizeForEach(ForEachStatement statement, BindingScope scope)
    {
        var collection = NormalizeExpression(statement.Collection, scope);
        var collectionType = ResolveType(collection, scope);
        scope.Push();
        try
        {
            if (collectionType?.TypeArguments.Count > 0)
                scope.Declare(statement.Variable, collectionType.TypeArguments[0]);
            return statement with
            {
                Collection = collection,
                Elements = NormalizeBody(statement.Elements, scope)
            };
        }
        finally
        {
            scope.Pop();
        }
    }

    // Prevents declarations inside a control-flow block from escaping that block.
    private List<SyntaxElement> NormalizeNestedBody(List<SyntaxElement> elements, BindingScope scope)
    {
        scope.Push();
        try
        {
            return NormalizeBody(elements, scope);
        }
        finally
        {
            scope.Pop();
        }
    }

    // Gives every branch a separate local scope while sharing the surrounding symbols.
    private ConditionalStatement NormalizeConditional(ConditionalStatement statement, BindingScope scope)
    {
        var branches = new List<ConditionalBranch>();
        foreach (var branch in statement.Branches)
        {
            branches.Add(branch with
            {
                Condition = NormalizeExpression(branch.Condition, scope),
                Elements = NormalizeNestedBody(branch.Elements, scope)
            });
        }
        return statement with
        {
            Branches = branches,
            ElseElements = statement.ElseElements is null
                ? null
                : NormalizeNestedBody(statement.ElseElements, scope)
        };
    }

    // Reconstructs the complete expression tree before applying parent-level patterns.
    private Expression NormalizeExpression(Expression expression, BindingScope scope)
    {
        var normalized = expression switch
        {
            ObjectCreationExpression creation => creation with
            {
                Arguments = NormalizeArguments(creation.Arguments, scope),
                Kind = godotTypes.IsBuiltInValueConstructor(
                    creation.Type.Name,
                    creation.Arguments.Arguments.Count)
                        ? ConstructionKind.BuiltInValue
                        : ConstructionKind.Object
            },
            ConversionExpression conversion => conversion with
            {
                Value = NormalizeExpression(conversion.Value, scope)
            },
            ArrayExpression array => array with
            {
                Elements = array.Elements
                    .Select(element => element with { Value = NormalizeExpression(element.Value, scope) })
                    .ToList()
            },
            InvocationExpression invocation => NormalizeInvocation(invocation, scope),
            GodotGlobalFunctionExpression global => global with
            {
                Arguments = NormalizeArguments(global.Arguments, scope)
            },
            GodotMethodInvocationExpression method => method with
            {
                Target = NormalizeExpression(method.Target, scope),
                Arguments = NormalizeArguments(method.Arguments, scope)
            },
            BinaryExpression binary => binary with
            {
                Left = NormalizeExpression(binary.Left, scope),
                Right = NormalizeExpression(binary.Right, scope)
            },
            UnaryExpression unary => unary with
            {
                Operand = NormalizeExpression(unary.Operand, scope)
            },
            ConditionalExpression conditional => conditional with
            {
                Condition = NormalizeExpression(conditional.Condition, scope),
                WhenTrue = NormalizeExpression(conditional.WhenTrue, scope),
                WhenFalse = NormalizeExpression(conditional.WhenFalse, scope)
            },
            InterpolatedStringExpression interpolated => NormalizeInterpolated(interpolated, scope),
            ParenthesizedExpression parenthesized => parenthesized with
            {
                Expression = NormalizeExpression(parenthesized.Expression, scope)
            },
            MemberAccessExpression member => NormalizeMemberAccess(member, scope),
            GodotMemberAccessExpression member => member with
            {
                Target = NormalizeExpression(member.Target, scope)
            },
            IndexExpression index => index with
            {
                Target = NormalizeExpression(index.Target, scope),
                Index = NormalizeExpression(index.Index, scope)
            },
            _ => expression
        };
        return normalized is BinaryExpression binaryExpression
            ? NormalizeEmptyComparison(binaryExpression)
            : normalized;
    }

    // Converts a method spelling only after its receiver resolves to a supported collection.
    private Expression NormalizeInvocation(InvocationExpression invocation, BindingScope scope)
    {
        var target = NormalizeExpression(invocation.Target, scope);
        var arguments = NormalizeArguments(invocation.Arguments, scope);
        if (language == ScriptLanguage.GdScript &&
            target is ValueExpression typeName &&
            godotTypes.IsBuiltInValueConstructor(typeName.Text, arguments.Arguments.Count))
        {
            return new ObjectCreationExpression(
                new TypeReference(typeName.Text),
                arguments,
                ConstructionKind.BuiltInValue);
        }
        if (TryGetGlobalCallName(target, out var globalName) &&
            !scope.IsUserMethod(globalName) &&
            godotTypes.TryResolveGlobalFunction(
                globalName,
                language,
                arguments.Arguments.Count,
                out var function))
        {
            return new GodotGlobalFunctionExpression(function, arguments);
        }
        if (target is not MemberAccessExpression member)
            return invocation with { Target = target, Arguments = arguments };

        var receiverType = ResolveType(member.Target, scope);
        if (receiverType is null)
            return invocation with { Target = target, Arguments = arguments };
        if (CollectionMappings.TryInvocation(
                language,
                receiverType.Kind,
                member.Member,
                arguments.Arguments.Count,
                out var operation))
        {
            return new CollectionOperationExpression(member.Target, operation, arguments);
        }
        if (godotTypes.TryResolveMethod(
                receiverType.Name,
                member.Member,
                language,
                arguments.Arguments.Count,
                out var method))
        {
            return new GodotMethodInvocationExpression(member.Target, method, arguments);
        }
        // Unknown and user-defined receivers retain their original callable structure.
        return invocation with { Target = target, Arguments = arguments };
    }

    // Extracts only the two syntactic forms that can denote a Godot global function.
    private static bool TryGetGlobalCallName(Expression target, out string name)
    {
        if (target is ValueExpression value)
        {
            name = value.Text;
            return true;
        }
        if (target is MemberAccessExpression { Target: ValueExpression owner } member)
        {
            name = owner.Text + "." + member.Member;
            return true;
        }
        name = "";
        return false;
    }

    /*
    A parameter, field, local, construction, or already-bound member supplies the receiver
    type. The catalog returns an exact public field/property and its result type, allowing the
    next access in a chain to bind. The model stores that reflected identity rather than a
    `position` or `Position` spelling. Unknown and user-defined accesses remain unbound.
    */
    private Expression NormalizeMemberAccess(MemberAccessExpression member, BindingScope scope)
    {
        var target = NormalizeExpression(member.Target, scope);
        var receiverType = ResolveType(target, scope);
        if (language == ScriptLanguage.CSharp &&
            receiverType is not null &&
            CollectionMappings.TryCSharpProperty(receiverType.Kind, member.Member, out var operation))
        {
            return new CollectionOperationExpression(target, operation, noArguments);
        }
        if (receiverType is not null &&
            godotTypes.TryResolveMember(
                receiverType.Name,
                member.Member,
                language,
                out var reference))
        {
            return new GodotMemberAccessExpression(target, reference);
        }
        return member with { Target = target };
    }

    // Folds C#'s property comparison into the same boolean operation as GDScript is_empty().
    private static Expression NormalizeEmptyComparison(BinaryExpression binary)
    {
        var count = binary.Left as CollectionOperationExpression;
        var zero = binary.Right as ValueExpression;
        if (count?.Operation != CollectionOperation.Count || zero?.Text != "0")
            return binary;
        var isEmpty = count with { Operation = CollectionOperation.IsEmpty };
        return binary.Operator == "=="
            ? isEmpty
            : binary.Operator == "!="
                ? new UnaryExpression("not", isEmpty)
                : binary;
    }

    // Embedded expressions participate in binding without changing interpolation trivia.
    private InterpolatedStringExpression NormalizeInterpolated(
        InterpolatedStringExpression interpolated,
        BindingScope scope)
    {
        var parts = new List<InterpolationPart>();
        foreach (var part in interpolated.Parts)
        {
            parts.Add(part is InterpolationValue value
                ? value with { Value = NormalizeExpression(value.Value, scope) }
                : part);
        }
        return interpolated with { Parts = parts };
    }

    // Rewrites argument values while retaining their individual line-break metadata.
    private ArgumentList NormalizeArguments(ArgumentList arguments, BindingScope scope) => arguments with
    {
        Arguments = arguments.Arguments
            .Select(argument => argument with { Value = NormalizeExpression(argument.Value, scope) })
            .ToList()
    };

    // Infers only types needed for safe downstream collection binding.
    private static TypeReference? ResolveType(Expression? expression, BindingScope scope) => expression switch
    {
        CurrentInstanceExpression => scope.CurrentInstanceType,
        ValueExpression value => scope.Resolve(value.Text),
        ConversionExpression conversion => conversion.Type,
        ObjectCreationExpression creation => creation.Type,
        GodotGlobalFunctionExpression global => global.Function.ResultType,
        GodotMethodInvocationExpression method => method.Method.ResultType,
        ArrayExpression array => new TypeReference(
            "Array",
            array.Elements.Count == 0 || ResolveType(array.Elements[0].Value, scope) is not { } elementType
                ? new List<TypeReference>()
                : new List<TypeReference> { elementType }),
        IndexExpression index => ResolveType(index.Target, scope)?.TypeArguments.FirstOrDefault(),
        GodotMemberAccessExpression member => member.Member.ResultType,
        CollectionOperationExpression { Operation: CollectionOperation.Count } => new TypeReference("int"),
        CollectionOperationExpression { Operation: CollectionOperation.IsEmpty or CollectionOperation.ContainsValue or CollectionOperation.ContainsKey } => new TypeReference("bool"),
        ParenthesizedExpression parenthesized => ResolveType(parenthesized.Expression, scope),
        _ => null
    };

    // Resolves inner lexical frames before falling back to the class-wide symbol table.
    private sealed class BindingScope
    {
        private readonly Dictionary<string, TypeReference> members;
        private readonly HashSet<string> methods;
        private readonly List<Dictionary<string, TypeReference>> frames = new();

        public BindingScope(
            Dictionary<string, TypeReference> members,
            HashSet<string> methods,
            TypeReference currentInstanceType)
        {
            this.members = members;
            this.methods = methods;
            CurrentInstanceType = currentInstanceType;
        }

        public TypeReference CurrentInstanceType { get; }

        public bool IsUserMethod(string name) => methods.Contains(name);

        public void Push() => frames.Add(new Dictionary<string, TypeReference>(StringComparer.Ordinal));

        public void Pop() => frames.RemoveAt(frames.Count - 1);

        public void Declare(string name, TypeReference type) => frames[^1][name] = type;

        public TypeReference? Resolve(string name)
        {
            for (var index = frames.Count - 1; index >= 0; index--)
            {
                if (frames[index].TryGetValue(name, out var local))
                    return local;
            }
            return members.GetValueOrDefault(name);
        }
    }
}
