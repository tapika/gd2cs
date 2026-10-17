using gd2cs.Model;
using gd2cs.Parsing;

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
    private ClassDeclaration NormalizeClass(ClassDeclaration declaration, HashSet<string>? enclosingTypes = null)
    {
        var members = new Dictionary<string, TypeReference>(StringComparer.Ordinal);
        var methods = new Dictionary<string, List<MethodDeclaration>>(StringComparer.Ordinal);
        var signals = new Dictionary<string, SignalDeclaration>(StringComparer.Ordinal);
        // Only types visible in this lexical class scope may shadow a Godot API owner.
        var userTypes = enclosingTypes is null ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(enclosingTypes, StringComparer.Ordinal);
        userTypes.Add(declaration.Name);
        foreach (var nested in declaration.Elements.OfType<NestedClassDeclaration>())
            userTypes.Add(nested.Name);
        // Bind declarations before constructing symbol tables, so receivers use CLR identities too.
        declaration = declaration with
        {
            BaseType = NormalizeType(declaration.BaseType, userTypes),
            Elements = declaration.Elements.Select(element => NormalizeDeclarationTypes(element, userTypes)).ToList()
        };
        foreach (var element in declaration.Elements)
        {
            if (element is FieldDeclaration field)
                members[field.Name] = field.Type;
            else if (element is ConstantDeclaration constant)
                members[constant.Name] = constant.Type;
            else if (element is MethodDeclaration method)
            {
                if (!methods.TryGetValue(method.Name, out var overloads))
                    methods[method.Name] = overloads = new List<MethodDeclaration>();
                overloads.Add(method);
            }
            else if (element is SignalDeclaration signal)
                signals.Add(signal.Name, signal);
        }

        var scope = new BindingScope(members, methods, signals, userTypes, declaration.BaseType);
        // Inference follows binding: constructors and static reads acquire their result
        // types from metadata. Resolve class-wide declarations before any method body.
        // Revisit forward dependencies until types stabilize, bounded by declaration count.
        var values = declaration.Elements.Where(element => element is ConstantDeclaration or FieldDeclaration).ToList();
        for (var pass = 0; pass < values.Count; pass++)
        {
            var changed = false;
            foreach (var value in values)
            {
                var bound = NormalizeClassElement(value, scope);
                var (name, type) = bound switch
                {
                    ConstantDeclaration constant => (constant.Name, constant.Type),
                    FieldDeclaration field => (field.Name, field.Type),
                    _ => throw new InvalidOperationException("Expected a value declaration.")
                };
                changed |= !SameHandlerType(members[name], type);
                members[name] = type;
            }
            if (!changed)
                break;
        }
        /* Discover event-handler signatures before normalizing the final method bodies.
           Connections may follow their handler declarations, or occur inside nested blocks.
           A first binding pass uses normal lexical scopes to avoid matching shadowed names.
           The second pass records native widths separately from script parameter types,
           so both source dialects produce identical models, independent of declaration order.
           Conflicting event signatures are rejected rather than generating identity-breaking
           lambda adapters. Conversion locals are added only by the C# emitter. */
        foreach (var element in declaration.Elements.Where(element => element is not NestedClassDeclaration))
            NormalizeClassElement(element, scope);
        var sourceElements = declaration.Elements.Select(element =>
        {
            if (element is not MethodDeclaration method || !scope.HandlerTypes.TryGetValue(method.Name, out var types))
                return element;
            var bound = EventParameterConversions.Bind(method.Parameters, method.Elements, types,
                fold: language == ScriptLanguage.CSharp);
            var updated = method with { Parameters = bound.Parameters, Elements = bound.Elements };
            methods[method.Name] = new List<MethodDeclaration> { updated };
            return (SyntaxElement)updated;
        }).ToList();
        var elements = new List<SyntaxElement>();
        foreach (var element in sourceElements)
            elements.Add(NormalizeClassElement(element, scope));
        return declaration with { Elements = elements };
    }

    // Canonicalize signatures before callable binding; nested classes bind independently.
    private SyntaxElement NormalizeDeclarationTypes(SyntaxElement element, HashSet<string> userTypes) => element switch
    {
        FieldDeclaration field => field with { Type = NormalizeType(field.Type, userTypes) },
        ConstantDeclaration constant => constant with { Type = NormalizeType(constant.Type, userTypes) },
        MethodDeclaration method => method with
        {
            ReturnType = NormalizeType(method.ReturnType, userTypes),
            Parameters = NormalizeParameterTypes(method.Parameters, userTypes)
        },
        ConstructorDeclaration constructor => constructor with { Parameters = NormalizeParameterTypes(constructor.Parameters, userTypes) },
        SignalDeclaration signal => signal with { Parameters = NormalizeParameterTypes(signal.Parameters, userTypes) },
        _ => element
    };

    // A Godot alias is bound only in type positions, never in identifiers, comments or strings.
    private TypeReference NormalizeType(TypeReference type, HashSet<string> userTypes)
    {
        var name = type.Name;
        var native = type.GodotName;
        if (!userTypes.Contains(name) && godotTypes.TryResolveTypeName(name, language, out var managed, out var godotName))
        {
            name = managed;
            native = godotName == managed ? null : godotName;
        }
        return type with
        {
            Name = name,
            GodotName = native,
            TypeArguments = type.TypeArguments.Select(argument => NormalizeType(argument, userTypes)).ToList()
        };
    }

    private ParameterList NormalizeParameterTypes(ParameterList parameters, HashSet<string> userTypes) => parameters with
    {
        Parameters = parameters.Parameters.Select(parameter => parameter with
        {
            Type = NormalizeType(parameter.Type, userTypes)
        }).ToList()
    };

    // Recurses only into class elements that can contain expressions or nested scopes.
    private SyntaxElement NormalizeClassElement(SyntaxElement element, BindingScope scope) => element switch
    {
        SignalDeclaration signal => signal with { Parameters = NormalizeParameters(signal.Parameters, scope) },
        ConstantDeclaration constant => NormalizeConstant(constant, scope),
        FieldDeclaration field => NormalizeField(field, scope),
        NestedClassDeclaration nested => NormalizeNestedClass(nested, scope),
        ConstructorDeclaration constructor => constructor with
        {
            Parameters = NormalizeParameters(constructor.Parameters, scope),
            Elements = NormalizeCallable(constructor.Parameters, constructor.Elements, scope)
        },
        MethodDeclaration method => method with
        {
            Parameters = NormalizeParameters(method.Parameters, scope),
            Elements = NormalizeCallable(method.Parameters, method.Elements, scope, method.IsStatic, method.ReturnType)
        },
        DestructorDeclaration destructor => destructor with
        {
            Elements = NormalizeCallable(destructor.Parameters, destructor.Elements, scope)
        },
        _ => element
    };

    private ConstantDeclaration NormalizeConstant(ConstantDeclaration constant, BindingScope scope)
    {
        var value = NormalizeTypedValue(constant.Type, constant.Value, scope)!;
        var type = language == ScriptLanguage.GdScript && constant.IsInferred
            ? ResolveType(value, scope) ?? constant.Type
            : constant.Type;
        return constant with { Type = NormalizeType(type, scope.UserTypes), Value = value };
    }

    // C# declarations remain authoritative; only inferred GDScript fields adopt bound result types.
    private FieldDeclaration NormalizeField(FieldDeclaration field, BindingScope scope)
    {
        var initializer = NormalizeTypedValue(field.Type, field.Initializer, scope);
        var type = language == ScriptLanguage.GdScript && field.IsInferred
            ? ResolveType(initializer, scope) ?? field.Type
            : field.Type;
        return field with { Type = NormalizeType(type, scope.UserTypes), Initializer = initializer };
    }

    // Reuses root-class binding so nested classes receive their own member namespace.
    private NestedClassDeclaration NormalizeNestedClass(NestedClassDeclaration nested, BindingScope parentScope)
    {
        var normalized = NormalizeClass(new ClassDeclaration(nested.Name, nested.BaseType, false, nested.Elements), parentScope.UserTypes);
        return nested with { BaseType = normalized.BaseType, Elements = normalized.Elements };
    }

    // Normalizes optional values without making later parameters visible to earlier defaults.
    private ParameterList NormalizeParameters(ParameterList parameters, BindingScope scope) => parameters with
    {
        Parameters = parameters.Parameters.Select(parameter => parameter with
        {
            Type = NormalizeType(parameter.Type, scope.UserTypes),
            DefaultValue = NormalizeTypedValue(parameter.Type, parameter.DefaultValue, scope)
        }).ToList()
    };

    // C# default and GDScript's empty built-in constructor denote the same typed value.
    // Callable is a value type: null is not a valid replacement for an empty Callable().
    private Expression? NormalizeTypedValue(TypeReference? type, Expression? value, BindingScope scope)
    {
        if (value is null)
            return null;
        // Null containers cannot represent GDScript's non-null empty default.
        if (type?.Kind is TypeKind.Array or TypeKind.Dictionary
            && value is ValueExpression { Text: "null" or "default" })
            throw SourceLocations.Error(value, "Null containers are not supported; use an empty container constructor or omit the initializer.");
        // GDScript literals obtain their key/value types from an explicit declaration.
        if (value is DictionaryExpression dictionary && type?.Kind == TypeKind.Dictionary
            && type.TypeArguments.Count == 2)
            value = SourceLocations.Copy(dictionary, dictionary with { Type = type });
        var isEmptyValueType = type?.Kind is TypeKind.Callable or TypeKind.StringName or TypeKind.NodePath;
        if (isEmptyValueType)
        {
            if (value is ValueExpression { Text: "default" })
                return new TypeDefaultExpression(type!);
            if (value is InvocationExpression invocation &&
                invocation.Target is ValueExpression target &&
                target.Text == type!.Name &&
                invocation.Arguments.Arguments.Count == 0)
                return new TypeDefaultExpression(type!);
        }
        return type?.Kind == TypeKind.Bool ? NormalizeBoolean(value, scope) : NormalizeExpression(value, scope);
    }

    // Parameters and locals live only for the duration of their owning callable.
    private List<SyntaxElement> NormalizeCallable(
        ParameterList parameters,
        List<SyntaxElement> elements,
        BindingScope scope,
        bool isStatic = false,
        TypeReference? returnType = null)
    {
        parameters = NormalizeParameterTypes(parameters, scope.UserTypes);
        var hadCurrentInstance = scope.HasCurrentInstance;
        var previousReturnType = scope.ReturnType;
        scope.ReturnType = returnType;
        scope.HasCurrentInstance = !isStatic;
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
            scope.HasCurrentInstance = hadCurrentInstance;
            scope.ReturnType = previousReturnType;
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
                scope.Declare(local.Name, type ?? new TypeReference(""));
            }
        }
        return result;
    }

    // Rebuilds expressions and recursively isolates block-local declarations.
    private SyntaxElement NormalizeBodyElement(SyntaxElement element, BindingScope scope) => element switch
    {
        AssignmentStatement statement => NormalizeAssignment(statement, scope),
        ReturnStatement statement => statement with
        {
            Value = NormalizeTypedValue(scope.ReturnType, statement.Value, scope)
        },
        ExpressionStatement statement => statement with
        {
            Expression = NormalizeExpression(statement.Expression, scope)
        },
        LocalVariableStatement statement => NormalizeLocal(statement, scope),
        ForEachStatement statement => NormalizeForEach(statement, scope),
        WhileStatement statement => NormalizeWhile(statement, scope),
        ConditionalStatement statement => NormalizeConditional(statement, scope),
        _ => element
    };

    // The assignment target supplies the type needed to interpret an empty value safely.
    private SyntaxElement NormalizeAssignment(AssignmentStatement statement, BindingScope scope)
    {
        if (statement.Target is ValueExpression variable && scope.FindNarrowed(variable.Text) is not null)
            throw SourceLocations.Error(statement, "Reassigning a type-narrowed variable is not supported.");
        if (language == ScriptLanguage.CSharp && statement.Operator is "+=" or "-="
            && TryNormalizeEventAssignment(statement, scope, out var connection))
            return new ExpressionStatement(connection);
        // Compound assignment also reads its target; Variant dictionary reads still require a cast.
        var target = NormalizeExpression(statement.Target, scope, allowVariantDictionaryRead: statement.Operator == "=");
        var value = NormalizeTypedValue(ResolveType(target, scope), statement.Value, scope)!;
        return statement with { Target = target, Value = value };
    }

    // Retains static typing when GDScript cannot infer it from the emitted expression.
    private LocalVariableStatement NormalizeLocal(LocalVariableStatement statement, BindingScope scope)
    {
        if (statement.Type is not null)
            statement = statement with { Type = NormalizeType(statement.Type, scope.UserTypes) };
        var initializer = NormalizeTypedValue(statement.Type, statement.Initializer, scope);
        // Typed GDScript locals start empty; CSharpParser already folds their emitted default initializer.
        if (initializer is TypeDefaultExpression && statement.Typing == LocalTyping.Explicit)
            initializer = null;
        var type = statement.Type ?? ResolveType(initializer, scope);
        if (type is not null)
            type = NormalizeType(type, scope.UserTypes);
        var typing = statement.Typing;
        if (initializer is DictionaryExpression)
            typing = LocalTyping.Explicit;
        if (typing == LocalTyping.Inferred && !CanGdScriptInfer(initializer))
            typing = type is null ? LocalTyping.Dynamic : LocalTyping.InferredWithExplicitGdType;
        else if (language == ScriptLanguage.GdScript &&
                 typing == LocalTyping.Explicit &&
                 initializer is not null &&
                 !CanGdScriptInfer(initializer))
            typing = LocalTyping.InferredWithExplicitGdType;
        return SourceLocations.Copy(statement, statement with
        {
            Type = type,
            Initializer = initializer,
            Typing = typing
        });
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
        scope.Push();
        try
        {
            foreach (var branch in statement.Branches)
            {
                var condition = NormalizeBoolean(branch.Condition, scope);
                var body = UnderCondition(condition, true, scope, () => NormalizeNestedBody(branch.Elements, scope));
                branches.Add(branch with { Condition = condition, Elements = body });
                // Later elif/else branches execute only after every earlier condition failed.
                ApplyTypeFacts(condition, false, scope);
            }
            return statement with
            {
                Branches = branches,
                ElseElements = statement.ElseElements is null ? null : NormalizeNestedBody(statement.ElseElements, scope)
            };
        }
        finally
        {
            scope.Pop();
        }
    }

    private WhileStatement NormalizeWhile(WhileStatement statement, BindingScope scope)
    {
        var condition = NormalizeBoolean(statement.Condition, scope);
        return statement with
        {
            Condition = condition,
            Elements = UnderCondition(condition, true, scope, () => NormalizeNestedBody(statement.Elements, scope))
        };
    }

    /* Type facts are lexical, not global substitutions. For A && B, B sees facts proving
       A true; for A || B, it sees facts proving A false. A successful conjunction proves
       both operands; a failed disjunction disproves both. Other paths provide no safe
       narrowing. Each narrowed read retains the exact proving test, so emission can use
       its definitely-assigned C# pattern variable even when tests repeat or are negated.
       C# pattern names are aliases only: they disappear from the language-neutral model. */
    private T UnderCondition<T>(Expression condition, bool truth, BindingScope scope, Func<T> action)
    {
        scope.Push();
        try
        {
            ApplyTypeFacts(condition, truth, scope);
            return action();
        }
        finally
        {
            scope.Pop();
        }
    }

    private static void ApplyTypeFacts(Expression condition, bool truth, BindingScope scope)
    {
        switch (condition)
        {
            case TypeCheckExpression test when test.IsNegated != truth && test.Value is ValueExpression variable:
                if (scope.IsLocal(variable.Text))
                {
                    var narrowed = new NarrowedValueExpression(variable, test.Type, test);
                    scope.Narrow(variable.Text, narrowed);
                    if (scope.PatternNames.TryGetValue(test, out var alias) && alias is not null)
                        scope.Narrow(alias, narrowed);
                }
                break;
            case ParenthesizedExpression grouped:
                ApplyTypeFacts(grouped.Expression, truth, scope);
                break;
            case UnaryExpression { Operator: "not" } unary:
                ApplyTypeFacts(unary.Operand, !truth, scope);
                break;
            case BinaryExpression binary when binary.Operator == (truth ? "and" : "or"):
                ApplyTypeFacts(binary.Left, truth, scope);
                ApplyTypeFacts(binary.Right, truth, scope);
                break;
        }
    }

    private Expression NormalizeTypeCheck(TypeCheckExpression expression, BindingScope scope)
    {
        var value = NormalizeExpression(expression.Value, scope);
        // A nested test still tests the original variable, not a C#-only alias.
        if (value is NarrowedValueExpression narrowed)
            value = narrowed.Value;
        var test = expression with { Value = value, Type = NormalizeType(expression.Type, scope.UserTypes), PatternName = null };
        scope.PatternNames[test] = expression.PatternName;
        return test;
    }

    private Expression NormalizeBinary(BinaryExpression expression, BindingScope scope)
    {
        var logical = expression.Operator is "and" or "or";
        var left = logical ? NormalizeBoolean(expression.Left, scope) : NormalizeExpression(expression.Left, scope);
        var right = expression.Operator is "and" or "or"
            ? UnderCondition(left, expression.Operator == "and", scope, () => NormalizeBoolean(expression.Right, scope))
            : NormalizeExpression(expression.Right, scope);
        left = ExpressionOperators.NormalizeOperand(left, expression.Operator, false);
        right = ExpressionOperators.NormalizeOperand(right, expression.Operator, true);
        // C#'s explicit test and GDScript's implicit numeric condition share one boolean node.
        if (expression.Operator is "==" or "!=")
        {
            var tested = right is ValueExpression { Text: "0" } ? left
                : left is ValueExpression { Text: "0" } ? right : null;
            if (tested is not null && ExpressionOperators.IsBitwise(tested))
            {
                var condition = new BitmaskConditionExpression(ExpressionOperators.BitmaskValue(tested));
                return expression.Operator == "!=" ? condition : new UnaryExpression("not", condition);
            }
        }
        return expression with { Left = left, Right = right };
    }

    // Limit implicit truthiness conversion to bitmask expressions, not arbitrary numbers or objects.
    private Expression NormalizeBoolean(Expression expression, BindingScope scope)
    {
        var normalized = NormalizeExpression(expression, scope);
        if (!ExpressionOperators.IsBitwise(normalized) || ResolveType(normalized, scope)?.Kind == TypeKind.Bool)
            return normalized;
        return new BitmaskConditionExpression(ExpressionOperators.BitmaskValue(normalized));
    }

    private Expression NormalizeConditionalExpression(ConditionalExpression expression, BindingScope scope)
    {
        var condition = NormalizeBoolean(expression.Condition, scope);
        return expression with
        {
            Condition = condition,
            WhenTrue = UnderCondition(condition, true, scope, () => NormalizeExpression(expression.WhenTrue, scope)),
            WhenFalse = UnderCondition(condition, false, scope, () => NormalizeExpression(expression.WhenFalse, scope))
        };
    }

    // Reconstructs the complete expression tree before applying parent-level patterns.
    private Expression NormalizeExpression(Expression expression, BindingScope scope, bool allowVariantDictionaryRead = false)
    {
        var normalized = expression switch
        {
            TypeCheckExpression test => NormalizeTypeCheck(test, scope),
            ValueExpression value when scope.FindNarrowed(value.Text) is { } narrowed => narrowed,
            // A lambda adds a lexical scope; captured names resolve through its enclosing frames.
            // GDScript snapshots captured locals, while C# closures share them. Reassignment of
            // captured locals is outside this translation subset; shared object mutation is valid.
            LambdaExpression lambda => lambda with
            {
                Parameters = NormalizeParameters(lambda.Parameters, scope),
                ReturnType = NormalizeType(lambda.ReturnType, scope.UserTypes),
                Elements = NormalizeCallable(lambda.Parameters, lambda.Elements, scope, returnType: lambda.ReturnType)
            },
            CallableInvocationExpression callable => callable with
            {
                Target = NormalizeExpression(callable.Target, scope),
                Arguments = NormalizeArguments(callable.Arguments, scope)
            },
            CallableMethodGroupExpression callable => callable with
            {
                Target = NormalizeExpression(callable.Target, scope),
                ParameterTypes = callable.ParameterTypes.Select(type => NormalizeType(type, scope.UserTypes)).ToList()
            },
            CallableNullExpression callable => callable with
            {
                Target = NormalizeExpression(callable.Target, scope)
            },
            ObjectCreationExpression creation => creation with
            {
                Type = NormalizeType(creation.Type, scope.UserTypes),
                Arguments = NormalizeArguments(creation.Arguments, scope),
                Kind = godotTypes.IsBuiltInValueConstructor(
                    NormalizeType(creation.Type, scope.UserTypes).Name,
                    creation.Arguments.Arguments.Count)
                        ? ConstructionKind.BuiltInValue
                        : ConstructionKind.Object
            },
            ConversionExpression conversion => conversion with
            {
                Type = NormalizeType(conversion.Type, scope.UserTypes),
                Value = NormalizeExpression(conversion.Value, scope)
            },
            SafeCastExpression cast => NormalizeCast(cast.Type, cast.Value, scope),
            VariantCastExpression cast => NormalizeCast(cast.Type, cast.Value, scope),
            ArrayExpression array => array with
            {
                Elements = array.Elements
                    .Select(element => element with { Value = NormalizeExpression(element.Value, scope) })
                    .ToList()
            },
            DictionaryExpression dictionary => NormalizeDictionary(dictionary, scope),
            InvocationExpression invocation => NormalizeInvocation(invocation, scope),
            SignalEmissionExpression signal => signal with { Arguments = NormalizeArguments(signal.Arguments, scope) },
            SignalConnectionExpression connection => connection with
            {
                Target = NormalizeExpression(connection.Target, scope),
                Arguments = NormalizeArguments(connection.Arguments, scope)
            },
            GodotGlobalFunctionExpression global => global with
            {
                Arguments = NormalizeArguments(global.Arguments, scope)
            },
            GodotStaticMethodExpression method => method with { Arguments = NormalizeArguments(method.Arguments, scope) },
            GodotMethodInvocationExpression method => method with
            {
                Target = NormalizeExpression(method.Target, scope),
                Arguments = NormalizeArguments(method.Arguments, scope)
            },
            BinaryExpression binary => NormalizeBinary(binary, scope),
            UnaryExpression unary => unary with
            {
                Operand = unary.Operator == "not" ? NormalizeBoolean(unary.Operand, scope) : NormalizeExpression(unary.Operand, scope)
            },
            ConditionalExpression conditional => NormalizeConditionalExpression(conditional, scope),
            InterpolatedStringExpression interpolated => NormalizeInterpolated(interpolated, scope),
            ParenthesizedExpression parenthesized => parenthesized with
            {
                Expression = NormalizeExpression(parenthesized.Expression, scope, allowVariantDictionaryRead)
            },
            ValueExpression value when TryNormalizeEnum(value, scope, out var constant) => constant,
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
        if (!allowVariantDictionaryRead && IsVariantDictionaryRead(normalized, scope))
            throw SourceLocations.Error(expression,
                "Variant-valued dictionary reads require an explicit cast; use 'entry.key as Node' or 'entry[\"key\"] as Type' with the appropriate concrete type.");
        return normalized is BinaryExpression binaryExpression
            ? NormalizeEmptyComparison(binaryExpression)
            : normalized;
    }

    /* A dictionary schema types all values alike, not each individual string key. Never infer
       a key's type from one initializer. Only a direct cast authorizes a Variant read; casts
       around calls or compound expressions cannot hide an uncast read inside them. C# object
       extraction first unwraps Variant, then uses as so incompatible objects still yield null.
       Assignment targets are exempt, while their receivers and keys are validated normally. */
    private Expression NormalizeCast(TypeReference type, Expression value, BindingScope scope)
    {
        type = NormalizeType(type, scope.UserTypes);
        if (language == ScriptLanguage.CSharp && value is InvocationExpression invocation
            && invocation.Target is MemberAccessExpression { Member: "AsGodotObject" } extraction
            && invocation.Arguments.Arguments.Count == 0)
            value = extraction.Target;
        var normalized = NormalizeExpression(value, scope, allowVariantDictionaryRead: true);
        if (!IsVariantDictionaryRead(normalized, scope))
            return new SafeCastExpression(type, normalized);
        if (type.Kind is TypeKind.Variant or TypeKind.Void)
            throw SourceLocations.Error(value, "Specify a concrete type for the dictionary value cast, not Variant or void.");
        var isObject = type.Kind == TypeKind.GodotObject
            || type.Kind == TypeKind.UserObject && !godotTypes.IsBuiltInValueType(type.Name);
        return new VariantCastExpression(type, normalized, isObject);
    }

    private static bool IsVariantDictionaryRead(Expression expression, BindingScope scope)
    {
        if (expression is ParenthesizedExpression parenthesized)
            return IsVariantDictionaryRead(parenthesized.Expression, scope);
        return expression is IndexExpression index
            && ResolveType(index.Target, scope)?.Kind == TypeKind.Dictionary
            && ResolveType(index, scope)?.Kind == TypeKind.Variant;
    }

    // Anonymous literals cannot establish a safe dictionary schema; declarations must supply it.
    private DictionaryExpression NormalizeDictionary(DictionaryExpression dictionary, BindingScope scope)
    {
        if (dictionary.Type is not null)
            dictionary = dictionary with { Type = NormalizeType(dictionary.Type, scope.UserTypes) };
        if (dictionary.Type?.Kind != TypeKind.Dictionary || dictionary.Type.TypeArguments.Count != 2)
            throw SourceLocations.Error(dictionary, "Untyped dictionaries are not supported; specify Dictionary[KeyType, ValueType] or use a class for mixed fields.");
        return dictionary with
        {
            Entries = dictionary.Entries.Select(entry => entry with
            {
                Key = NormalizeTypedValue(dictionary.Type.TypeArguments[0], entry.Key, scope)!,
                Value = NormalizeTypedValue(dictionary.Type.TypeArguments[1], entry.Value, scope)!
            }).ToList()
        };
    }

    // Binds global calls and instance methods only when their receiver or implicit instance is known.
    private Expression NormalizeInvocation(InvocationExpression invocation, BindingScope scope)
    {
        if (TryNormalizeSignalConnection(invocation, scope, out var connection))
            return connection;
        if (TryNormalizeSignalEmission(invocation, scope, out var emission))
            return emission;
        Expression target;
        // A dictionary method call is not dictionary-dot key access (size() versus .size).
        if (invocation.Target is MemberAccessExpression calledMember
            && ResolveType(calledMember.Target, scope)?.Kind == TypeKind.Dictionary
            && CollectionMappings.TryInvocation(language, TypeKind.Dictionary, calledMember.Member,
                invocation.Arguments.Arguments.Count, out _))
            target = calledMember with { Target = NormalizeExpression(calledMember.Target, scope) };
        else
            target = NormalizeExpression(invocation.Target, scope);
        var arguments = NormalizeArguments(invocation.Arguments, scope);
        /* Singleton APIs are exposed as qualified static calls in C#. Reflection establishes
           the owner and method identity; local/field/type shadowing is checked before lookup.
           Do not add these APIs to the global table or blindly PascalCase member names. */
        if (target is MemberAccessExpression { Target: ValueExpression owner } qualified
            && scope.Resolve(owner.Text) is null && !scope.IsUserMethod(owner.Text)
            && !scope.UserTypes.Contains(owner.Text)
            && godotTypes.TryResolveStaticMethod(owner.Text, qualified.Member, language,
                arguments.Arguments.Count, out var staticMethod))
            return new GodotStaticMethodExpression(staticMethod, arguments);
        if (language == ScriptLanguage.GdScript &&
            target is ValueExpression typeName &&
            scope.Resolve(typeName.Text) is null && !scope.IsUserMethod(typeName.Text) && !scope.UserTypes.Contains(typeName.Text) &&
            godotTypes.IsBuiltInValueConstructor(NormalizeType(new TypeReference(typeName.Text), scope.UserTypes).Name,
                arguments.Arguments.Count))
        {
            return new ObjectCreationExpression(
                NormalizeType(new TypeReference(typeName.Text), scope.UserTypes),
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
        if (target is ValueExpression implicitMethod &&
            scope.HasCurrentInstance &&
            !scope.IsUserMethod(implicitMethod.Text) &&
            scope.Resolve(implicitMethod.Text) is null &&
            godotTypes.TryResolveMethod(
                scope.CurrentInstanceType.Name,
                implicitMethod.Text,
                language,
                arguments.Arguments.Count,
                out var inheritedMethod))
        {
            return new GodotMethodInvocationExpression(
                new CurrentInstanceExpression(), inheritedMethod, arguments, IsImplicitReceiver: true);
        }
        if (target is not MemberAccessExpression member)
            return invocation with { Target = target, Arguments = arguments };

        if (member.Target is CurrentInstanceExpression && scope.IsUserMethod(member.Member))
            return invocation with { Target = target, Arguments = arguments };

        var receiverType = ResolveType(member.Target, scope);
        if (receiverType is null)
            return invocation with { Target = target, Arguments = arguments };
        if (receiverType.Kind == TypeKind.Callable &&
            member.Member == (language == ScriptLanguage.CSharp ? "Call" : "call"))
            return new CallableInvocationExpression(member.Target, arguments);
        if (language == ScriptLanguage.GdScript &&
            receiverType.Kind == TypeKind.Callable &&
            member.Member == "is_null" &&
            arguments.Arguments.Count == 0)
            return new CallableNullExpression(member.Target);
        if (receiverType.Kind == TypeKind.String &&
            StringMappings.TryInvocation(language, member.Member, arguments, out var stringOperation, out var stringArguments))
        {
            return new StringOperationExpression(member.Target, stringOperation, stringArguments);
        }
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

    /* Bind signals through their class declarations, never by replacing every .emit call.
       The source name is kept exactly, including case, because external GDScript connects
       to that registered name. C# SignalName and EventHandler are generated API details.
       Locals can shadow a signal and nested classes own independent signal tables. */
    private bool TryNormalizeSignalEmission(InvocationExpression invocation, BindingScope scope, out Expression emission)
    {
        emission = invocation;
        if (!scope.HasCurrentInstance)
            return false;
        string? name = null;
        var arguments = invocation.Arguments;
        if (language == ScriptLanguage.GdScript
            && invocation.Target is MemberAccessExpression { Member: "emit" } member)
        {
            if (member.Target is ValueExpression value && scope.Resolve(value.Text) is null)
                name = value.Text;
            else if (member.Target is MemberAccessExpression { Target: CurrentInstanceExpression } explicitSignal)
                name = explicitSignal.Member;
        }
        else if (language == ScriptLanguage.CSharp && !scope.IsUserMethod("EmitSignal")
            && (invocation.Target is ValueExpression { Text: "EmitSignal" }
                || invocation.Target is MemberAccessExpression { Target: CurrentInstanceExpression, Member: "EmitSignal" })
            && arguments.Arguments.Count > 0
            && arguments.Arguments[0].Value is MemberAccessExpression { Target: ValueExpression { Text: "SignalName" } } signalName)
        {
            name = signalName.Member;
            arguments = arguments with { Arguments = arguments.Arguments.Skip(1).ToList() };
        }
        if (name is null || !scope.Signals.TryGetValue(name, out var declaration))
            return false;
        if (arguments.Arguments.Count != declaration.Parameters.Parameters.Count)
            throw SourceLocations.Error(invocation, $"Signal '{name}' requires {declaration.Parameters.Parameters.Count} argument(s).");
        emission = new SignalEmissionExpression(name, NormalizeArguments(arguments, scope));
        return true;
    }

    /* Native events are resolved by receiver type and reflected SignalName identity, not
       by capitalizing .connect chains. Native parameter types align handler declarations
       before final binding. Preserve method-group identity for event unsubscription and
       leave unrelated user-defined .connect methods untouched. */
    private bool TryNormalizeSignalConnection(InvocationExpression invocation, BindingScope scope, out Expression connection)
    {
        connection = invocation;
        Expression receiver;
        string signalName;
        string? constantOwner = null;
        var arguments = invocation.Arguments;
        // GDScript names the signal in the receiver chain; C# passes a SignalName constant.
        if (language == ScriptLanguage.GdScript
            && invocation.Target is MemberAccessExpression { Target: MemberAccessExpression signal } gdMethod
            && gdMethod.Member is "connect" or "disconnect")
        {
            receiver = NormalizeExpression(signal.Target, scope);
            signalName = signal.Member;
        }
        else if (language == ScriptLanguage.CSharp
            && invocation.Target is MemberAccessExpression { Member: "Connect" } method
            && arguments.Arguments.Count > 0
            && arguments.Arguments[0].Value is MemberAccessExpression
            {
                Target: MemberAccessExpression { Target: ValueExpression owner, Member: "SignalName" }
            } constant)
        {
            receiver = NormalizeExpression(method.Target, scope);
            signalName = constant.Member;
            constantOwner = owner.Text;
            // Remove the C#-only signal-name argument, retaining the callback's source layout.
            arguments = arguments with { Arguments = arguments.Arguments.Skip(1).ToList() };
        }
        else
            return false;
        var receiverType = ResolveType(receiver, scope);
        // Unknown receivers and script-defined lookalikes must remain ordinary member calls.
        if (receiverType is null || scope.UserTypes.Contains(receiverType.Name)
            || !godotTypes.TryResolveSignal(receiverType.Name, signalName, language, out var reference))
            return false;
        // Inherited signals allow either the declaring class or concrete receiver as constant owner.
        // A local or user type with that name must not be interpreted as a native SignalName class.
        if (constantOwner is not null && (scope.Resolve(constantOwner) is not null
            || scope.UserTypes.Contains(constantOwner)
            || constantOwner != reference.DeclaringType && constantOwner != receiverType.Name))
            return false;
        if (arguments.Arguments.Count != 1)
            throw SourceLocations.Error(invocation, "Signal connections currently require exactly one callback and no flags.");
        var callback = arguments.Arguments[0];
        var handler = NormalizeSignalHandler(callback.Value, reference, scope, invocation);
        connection = new SignalConnectionExpression(receiver, reference, arguments with
        {
            Arguments = new List<Argument> { callback with { Value = handler } }
        }, language == ScriptLanguage.GdScript && invocation.Target is MemberAccessExpression { Member: "disconnect" });
        return true;
    }

    // Native event subscriptions reuse the same receiver-driven binding as GDScript connections.
    private bool TryNormalizeEventAssignment(AssignmentStatement statement, BindingScope scope, out Expression connection)
    {
        connection = null!;
        if (statement.Target is not MemberAccessExpression member)
            return false;
        var receiver = NormalizeExpression(member.Target, scope);
        var receiverType = ResolveType(receiver, scope);
        if (receiverType is null || scope.UserTypes.Contains(receiverType.Name)
            || !godotTypes.TryResolveSignal(receiverType.Name, member.Member, language, out var signal))
            return false;
        // Only reflected events interpret +=/-= as subscriptions; arithmetic keeps the original target.
        var callback = statement.Value;
        var startsOnNewLine = false;
        var closesOnNewLine = false;
        if (callback is ParenthesizedExpression grouped)
        {
            startsOnNewLine = grouped.OpeningLineBreaks > 0;
            closesOnNewLine = grouped.ClosingLineBreaks > 0;
            callback = grouped.Expression;
        }
        var arguments = new ArgumentList(new List<Argument> { new(callback, startsOnNewLine) }, closesOnNewLine);
        var location = SourceLocations.Copy(statement, new InvocationExpression(statement.Target, arguments));
        var handler = NormalizeSignalHandler(callback, signal, scope, location);
        connection = new SignalConnectionExpression(receiver, signal, arguments with
        {
            Arguments = new List<Argument> { new(handler, startsOnNewLine) }
        }, statement.Operator == "-=");
        return true;
    }

    // Width differences are dialect details; unrelated types cannot be made compatible by renaming.
    private static bool SameSignalType(TypeReference left, TypeReference right) =>
        SameHandlerType(left, right)
        || left.Kind is TypeKind.Int32 or TypeKind.Int64 && right.Kind is TypeKind.Int32 or TypeKind.Int64;

    // Makes named methods into Callables while retaining already-constructed Callable expressions.
    private Expression NormalizeSignalHandler(Expression expression, GodotSignalReference signal,
        BindingScope scope, InvocationExpression invocation)
    {
        var handler = NormalizeExpression(expression, scope);
        // Unwrap the C# factory so both dialects resolve the same underlying method reference.
        var target = handler is CallableMethodGroupExpression group ? group.Target : handler;
        string? name = target switch
        {
            // A local/parameter named like a method shadows it; explicit self/this does not.
            ValueExpression value when scope.Resolve(value.Text) is null => value.Text,
            MemberAccessExpression { Target: CurrentInstanceExpression } member => member.Member,
            _ => null
        };
        if (name is not null && scope.FindHandler(name) is { } declaration
            && (scope.HasCurrentInstance || declaration.IsStatic && target is ValueExpression))
        {
            // Static bodies may bind static methods, but cannot capture an instance method.
            var types = declaration.Parameters.Parameters.Select(parameter => parameter.Type).ToList();
            if (declaration.ReturnType.Kind != TypeKind.Void || types.Count != signal.ParameterTypes.Count)
                throw SourceLocations.Error(invocation, "Signal handler must return void and match the signal's argument count.");
            if (!types.Zip(signal.ParameterTypes).All(pair => SameSignalType(pair.First, pair.Second)))
                throw SourceLocations.Error(invocation, "Signal handler parameter types are incompatible with the native event.");
            // One method must satisfy every event it handles, including later disconnect calls.
            if (scope.HandlerTypes.TryGetValue(name, out var existing)
                && (existing.Count != signal.ParameterTypes.Count
                    || !existing.Zip(signal.ParameterTypes).All(pair => SameHandlerType(pair.First, pair.Second))))
                throw SourceLocations.Error(invocation, "Signal handler is used with conflicting native event signatures.");
            scope.HandlerTypes[name] = signal.ParameterTypes;
            // Explicit C# factory arguments must describe the actual handler, not guess another overload.
            if (handler is CallableMethodGroupExpression explicitGroup && explicitGroup.ParameterTypes.Count > 0
                && (explicitGroup.ParameterTypes.Count != types.Count
                    || !explicitGroup.ParameterTypes.Zip(types).All(pair => SameSignalType(pair.First, pair.Second))))
                throw SourceLocations.Error(invocation, "Callable.From types must match the declared handler parameters.");
            return new CallableMethodGroupExpression(target, signal.ParameterTypes);
        }
        if (handler is LambdaExpression lambda)
        {
            // Lambda parameters provide a checkable signature without requiring a named method.
            if (lambda.ReturnType.Kind != TypeKind.Void || lambda.Parameters.Parameters.Count != signal.ParameterTypes.Count)
                throw SourceLocations.Error(invocation, "Signal handler must return void and match the signal's argument count.");
            if (!lambda.Parameters.Parameters.Select(parameter => parameter.Type).Zip(signal.ParameterTypes)
                .All(pair => SameSignalType(pair.First, pair.Second)))
                throw SourceLocations.Error(invocation, "Signal handler parameter types are incompatible with the native event.");
            var bound = EventParameterConversions.Bind(lambda.Parameters, lambda.Elements, signal.ParameterTypes,
                fold: language == ScriptLanguage.CSharp);
            // A generated block wrapping one inline expression returns to the original script lambda.
            return lambda with
            {
                Parameters = bound.Parameters,
                Elements = NormalizeCallable(bound.Parameters, bound.Elements, scope, returnType: lambda.ReturnType),
                IsInline = lambda.IsInline || bound.Folded && bound.Elements.Count == 1
                    && bound.Elements[0] is ExpressionStatement or ReturnStatement
            };
        }
        // A runtime Callable is not a CLR delegate; supporting it would require an adapter.
        if (handler is not CallableMethodGroupExpression && ResolveType(handler, scope)?.Kind == TypeKind.Callable)
            throw SourceLocations.Error(invocation, "Signal events require a named method or inline lambda, not a Callable value.");
        throw SourceLocations.Error(invocation, "Signal connection requires an unambiguous named void handler in this class or an inline lambda.");
    }

    // Independently parsed generic argument lists need structural comparison, not record/list identity.
    private static bool SameHandlerType(TypeReference left, TypeReference right) =>
        left.Name == right.Name && left.TypeArguments.Count == right.TypeArguments.Count
        && left.TypeArguments.Zip(right.TypeArguments).All(pair => SameHandlerType(pair.First, pair.Second));

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
        if (TryNormalizeEnum(member, scope, out var constant))
            return constant;
        if (TryNormalizeStaticMember(member, scope, out var staticMember))
            return staticMember;
        var target = NormalizeExpression(member.Target, scope);
        var receiverType = ResolveType(target, scope);
        // Dictionary dot access is a string-key lookup, never a reflected object member.
        if (language == ScriptLanguage.GdScript && receiverType?.Kind == TypeKind.Dictionary)
            return SourceLocations.Copy(member, new IndexExpression(target, new ValueExpression("\"" + member.Member + "\"")));
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

    // Bind qualified type owners only after lexical shadowing checks; instances keep their own members.
    private bool TryNormalizeStaticMember(Expression expression, BindingScope scope, out Expression result)
    {
        result = null!;
        var parts = new List<string>();
        var root = expression;
        while (root is MemberAccessExpression member)
        {
            parts.Insert(0, member.Member);
            root = member.Target;
        }
        if (root is not ValueExpression owner || scope.Resolve(owner.Text) is not null
            || scope.IsUserMethod(owner.Text) || scope.UserTypes.Contains(owner.Text))
            return false;
        parts.Insert(0, owner.Text);
        // Fully qualified C# names must honor user-type shadowing after the namespace too.
        if (language == ScriptLanguage.CSharp && parts.Count > 2 && parts[0] == "Godot"
            && scope.UserTypes.Contains(parts[1]))
            return false;
        var name = string.Join('.', parts);
        if (godotTypes.TryResolveStaticMember(name, language, out var reference))
        {
            result = new GodotStaticMemberExpression(reference with
            {
                ResultType = NormalizeType(reference.ResultType, scope.UserTypes)
            });
            return true;
        }
        if (language == ScriptLanguage.GdScript && godotTypes.IsUnresolvedStaticMember(name))
            throw SourceLocations.Error(expression, $"Godot static member '{name}' has no unique C# counterpart in the loaded API.");
        return false;
    }

    private bool TryNormalizeEnum(Expression expression, BindingScope scope, out Expression constant)
    {
        var parts = new List<string>();
        while (expression is MemberAccessExpression member)
        {
            parts.Insert(0, member.Member);
            expression = member.Target;
        }
        if (expression is ValueExpression root && scope.Resolve(root.Text) is null &&
            !scope.IsUserMethod(root.Text))
        {
            parts.Insert(0, root.Text);
            var name = string.Join('.', parts);
            if (godotTypes.TryResolveEnum(name, language, out var reference) ||
                language == ScriptLanguage.GdScript && parts.Count == 1 &&
                godotTypes.TryResolveEnum(scope.CurrentInstanceType.Name + "." + name, language, out reference))
            {
                constant = new GodotEnumExpression(reference);
                return true;
            }
        }
        constant = null!;
        return false;
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
        NarrowedValueExpression narrowed => narrowed.Type,
        TypeCheckExpression => new TypeReference("bool"),
        BitmaskConditionExpression => new TypeReference("bool"),
        UnaryExpression { Operator: "not" } => new TypeReference("bool"),
        UnaryExpression unary => ResolveType(unary.Operand, scope),
        BinaryExpression { Operator: "and" or "or" or "==" or "!=" or "<" or "<=" or ">" or ">=" } => new TypeReference("bool"),
        BinaryExpression binary => ResolveType(binary.Left, scope) ?? ResolveType(binary.Right, scope),
        TypeDefaultExpression defaultValue => defaultValue.Type,
        LambdaExpression => new TypeReference("Callable"),
        CallableInvocationExpression => new TypeReference("Variant"),
        CallableMethodGroupExpression => new TypeReference("Callable"),
        CallableNullExpression => new TypeReference("bool"),
        CurrentInstanceExpression => scope.CurrentInstanceType,
        ValueExpression value when value.Text.StartsWith('"') => new TypeReference("string"),
        ValueExpression { Text: "true" or "false" } => new TypeReference("bool"),
        ValueExpression value when value.Text.Length > 0 && char.IsDigit(value.Text[0]) =>
            new TypeReference(value.Text.Contains('.') ? "float" : "int"),
        ValueExpression value => scope.Resolve(value.Text),
        ConversionExpression conversion => conversion.Type,
        SafeCastExpression cast => cast.Type,
        VariantCastExpression cast => cast.Type,
        ObjectCreationExpression creation => creation.Type,
        GodotGlobalFunctionExpression global => global.Function.ResultType,
        GodotStaticMethodExpression method => method.Method.ResultType,
        GodotMethodInvocationExpression method => method.Method.ResultType,
        StringOperationExpression operation => StringMappings.ResultType(operation.Operation),
        DictionaryExpression dictionary => dictionary.Type,
        ArrayExpression array => new TypeReference(
            "Array",
            array.Elements.Count == 0 || ResolveType(array.Elements[0].Value, scope) is not { } elementType
                ? new List<TypeReference>()
                : new List<TypeReference> { elementType }),
        IndexExpression index => ResolveType(index.Target, scope) is { } collectionType
            ? collectionType.TypeArguments.ElementAtOrDefault(collectionType.Kind == TypeKind.Dictionary ? 1 : 0)
            : null,
        GodotMemberAccessExpression member => member.Member.ResultType,
        GodotStaticMemberExpression member => member.Member.ResultType,
        GodotEnumExpression constant => new TypeReference(constant.Constant.EnumType),
        CollectionOperationExpression { Operation: CollectionOperation.Count or CollectionOperation.IndexOf } => new TypeReference("int"),
        CollectionOperationExpression { Operation: CollectionOperation.IsEmpty or CollectionOperation.ContainsValue or CollectionOperation.ContainsKey } => new TypeReference("bool"),
        ParenthesizedExpression parenthesized => ResolveType(parenthesized.Expression, scope),
        _ => null
    };

    // Resolves inner lexical frames before falling back to the class-wide symbol table.
    private sealed class BindingScope
    {
        private readonly Dictionary<string, TypeReference> members;
        private readonly Dictionary<string, List<MethodDeclaration>> methods;
        private readonly List<Dictionary<string, TypeReference>> frames = new();
        // Null entries mask outer facts when a new local shadows a narrowed name.
        private readonly List<Dictionary<string, NarrowedValueExpression?>> narrowedFrames = new();

        public BindingScope(
            Dictionary<string, TypeReference> members,
            Dictionary<string, List<MethodDeclaration>> methods,
            Dictionary<string, SignalDeclaration> signals,
            HashSet<string> userTypes,
            TypeReference currentInstanceType)
        {
            this.members = members;
            this.methods = methods;
            Signals = signals;
            UserTypes = userTypes;
            CurrentInstanceType = currentInstanceType;
        }

        public TypeReference CurrentInstanceType { get; }
        public TypeReference? ReturnType { get; set; }
        public Dictionary<string, SignalDeclaration> Signals { get; }
        // Shared across callable scopes within one class; records the required native handler signatures.
        public Dictionary<string, List<TypeReference>> HandlerTypes { get; } = new(StringComparer.Ordinal);
        // Parser aliases are attached to exact tests, not to names that can be shadowed or reused.
        public Dictionary<TypeCheckExpression, string?> PatternNames { get; } = new(ReferenceEqualityComparer.Instance);
        public HashSet<string> UserTypes { get; }

        // Instance methods are unavailable in static bodies and outside callable bodies.
        public bool HasCurrentInstance { get; set; }

        public bool IsUserMethod(string name) => methods.ContainsKey(name);

        // Overloaded names need explicit disambiguation; never choose the first handler arbitrarily.
        public MethodDeclaration? FindHandler(string name) =>
            methods.TryGetValue(name, out var overloads) && overloads.Count == 1 ? overloads[0] : null;

        public void Push()
        {
            frames.Add(new Dictionary<string, TypeReference>(StringComparer.Ordinal));
            narrowedFrames.Add(new Dictionary<string, NarrowedValueExpression?>(StringComparer.Ordinal));
        }

        public void Pop()
        {
            frames.RemoveAt(frames.Count - 1);
            narrowedFrames.RemoveAt(narrowedFrames.Count - 1);
        }

        public void Declare(string name, TypeReference type)
        {
            frames[^1][name] = type;
            narrowedFrames[^1][name] = null;
        }

        public bool IsLocal(string name) => frames.Any(frame => frame.ContainsKey(name));

        public void Narrow(string name, NarrowedValueExpression value)
        {
            frames[^1][name] = value.Type;
            narrowedFrames[^1][name] = value;
        }

        public NarrowedValueExpression? FindNarrowed(string name)
        {
            for (var index = narrowedFrames.Count - 1; index >= 0; index--)
                if (narrowedFrames[index].TryGetValue(name, out var value))
                    return value;
            return null;
        }

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
