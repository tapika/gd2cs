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
        var elements = new List<SyntaxElement>();
        foreach (var element in declaration.Elements)
            elements.Add(NormalizeClassElement(element, scope));
        return declaration with { Elements = elements };
    }

    // Recurses only into class elements that can contain expressions or nested scopes.
    private SyntaxElement NormalizeClassElement(SyntaxElement element, BindingScope scope) => element switch
    {
        SignalDeclaration signal => signal with { Parameters = NormalizeParameters(signal.Parameters, scope) },
        ConstantDeclaration constant when constant.Value is DictionaryExpression => constant with
        {
            Value = NormalizeTypedValue(constant.Type, constant.Value, scope)!
        },
        FieldDeclaration field => field with
        {
            Initializer = NormalizeTypedValue(field.Type, field.Initializer, scope)
        },
        NestedClassDeclaration nested => NormalizeNestedClass(nested, scope),
        ConstructorDeclaration constructor => constructor with
        {
            Parameters = NormalizeParameters(constructor.Parameters, scope),
            Elements = NormalizeCallable(constructor.Parameters, constructor.Elements, scope)
        },
        MethodDeclaration method => method with
        {
            Parameters = NormalizeParameters(method.Parameters, scope),
            Elements = NormalizeCallable(method.Parameters, method.Elements, scope, method.IsStatic)
        },
        DestructorDeclaration destructor => destructor with
        {
            Elements = NormalizeCallable(destructor.Parameters, destructor.Elements, scope)
        },
        _ => element
    };

    // Reuses root-class binding so nested classes receive their own member namespace.
    private NestedClassDeclaration NormalizeNestedClass(NestedClassDeclaration nested, BindingScope parentScope)
    {
        var normalized = NormalizeClass(new ClassDeclaration(nested.Name, nested.BaseType, false, nested.Elements), parentScope.UserTypes);
        return nested with { Elements = normalized.Elements };
    }

    // Normalizes optional values without making later parameters visible to earlier defaults.
    private ParameterList NormalizeParameters(ParameterList parameters, BindingScope scope) => parameters with
    {
        Parameters = parameters.Parameters.Select(parameter => parameter with
        {
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
        return NormalizeExpression(value, scope);
    }

    // Parameters and locals live only for the duration of their owning callable.
    private List<SyntaxElement> NormalizeCallable(
        ParameterList parameters,
        List<SyntaxElement> elements,
        BindingScope scope,
        bool isStatic = false)
    {
        var hadCurrentInstance = scope.HasCurrentInstance;
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

    // The assignment target supplies the type needed to interpret an empty value safely.
    private AssignmentStatement NormalizeAssignment(AssignmentStatement statement, BindingScope scope)
    {
        // Storing a dictionary value does not read it and therefore requires no cast.
        var target = NormalizeExpression(statement.Target, scope, allowVariantDictionaryRead: true);
        var value = NormalizeTypedValue(ResolveType(target, scope), statement.Value, scope)!;
        return statement with { Target = target, Value = value };
    }

    // Retains static typing when GDScript cannot infer it from the emitted expression.
    private LocalVariableStatement NormalizeLocal(LocalVariableStatement statement, BindingScope scope)
    {
        var initializer = NormalizeTypedValue(statement.Type, statement.Initializer, scope);
        // Typed GDScript locals start empty; CSharpParser already folds their emitted default initializer.
        if (initializer is TypeDefaultExpression && statement.Typing == LocalTyping.Explicit)
            initializer = null;
        var type = statement.Type ?? ResolveType(initializer, scope);
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
    private Expression NormalizeExpression(Expression expression, BindingScope scope, bool allowVariantDictionaryRead = false)
    {
        var normalized = expression switch
        {
            // A lambda adds a lexical scope; captured names resolve through its enclosing frames.
            // GDScript snapshots captured locals, while C# closures share them. Reassignment of
            // captured locals is outside this translation subset; shared object mutation is valid.
            LambdaExpression lambda => lambda with
            {
                Parameters = NormalizeParameters(lambda.Parameters, scope),
                Elements = NormalizeCallable(lambda.Parameters, lambda.Elements, scope)
            },
            CallableInvocationExpression callable => callable with
            {
                Target = NormalizeExpression(callable.Target, scope),
                Arguments = NormalizeArguments(callable.Arguments, scope)
            },
            CallableMethodGroupExpression callable => callable with { Target = NormalizeExpression(callable.Target, scope) },
            CallableNullExpression callable => callable with
            {
                Target = NormalizeExpression(callable.Target, scope)
            },
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
       by capitalizing .connect chains. Named handlers use their own declared parameter
       types in Callable.From, rather than the native delegate's types: Godot Variant
       marshaling can deliver an int64 signal argument to a C# int handler. Only arity is
       checked across that boundary. Preserve Callable/lambda callbacks directly, and
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
            && invocation.Target is MemberAccessExpression { Member: "connect", Target: MemberAccessExpression signal })
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
        });
        return true;
    }

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
            // Use the handler's signature, not the native event delegate's numeric widths.
            // Static bodies may bind static methods, but cannot capture an instance method.
            var types = declaration.Parameters.Parameters.Select(parameter => parameter.Type).ToList();
            if (declaration.ReturnType.Kind != TypeKind.Void || types.Count != signal.ParameterTypes.Count)
                throw SourceLocations.Error(invocation, "Signal handler must return void and match the signal's argument count.");
            // Explicit C# factory arguments must describe the actual handler, not guess another overload.
            if (handler is CallableMethodGroupExpression explicitGroup && explicitGroup.ParameterTypes.Count > 0
                && (explicitGroup.ParameterTypes.Count != types.Count
                    || !explicitGroup.ParameterTypes.Zip(types).All(pair => SameHandlerType(pair.First, pair.Second))))
                throw SourceLocations.Error(invocation, "Callable.From types must match the declared handler parameters.");
            return new CallableMethodGroupExpression(target, types);
        }
        if (handler is LambdaExpression lambda)
        {
            // Lambda parameters provide a checkable signature without requiring a named method.
            if (lambda.ReturnType.Kind != TypeKind.Void || lambda.Parameters.Parameters.Count != signal.ParameterTypes.Count)
                throw SourceLocations.Error(invocation, "Signal handler must return void and match the signal's argument count.");
            return lambda;
        }
        // An existing Callable can have runtime-dependent arity; preserve it rather than wrapping it again.
        if (handler is not CallableMethodGroupExpression && ResolveType(handler, scope)?.Kind == TypeKind.Callable)
            return handler;
        throw SourceLocations.Error(invocation, "Signal connection requires an unambiguous named void handler in this class or a Callable.");
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
        TypeDefaultExpression defaultValue => defaultValue.Type,
        LambdaExpression => new TypeReference("Callable"),
        CallableInvocationExpression => new TypeReference("Variant"),
        CallableMethodGroupExpression => new TypeReference("Callable"),
        CallableNullExpression => new TypeReference("bool"),
        CurrentInstanceExpression => scope.CurrentInstanceType,
        ValueExpression value when value.Text.StartsWith('"') => new TypeReference("string"),
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
        public Dictionary<string, SignalDeclaration> Signals { get; }
        public HashSet<string> UserTypes { get; }

        // Instance methods are unavailable in static bodies and outside callable bodies.
        public bool HasCurrentInstance { get; set; }

        public bool IsUserMethod(string name) => methods.ContainsKey(name);

        // Overloaded names need explicit disambiguation; never choose the first handler arbitrarily.
        public MethodDeclaration? FindHandler(string name) =>
            methods.TryGetValue(name, out var overloads) && overloads.Count == 1 ? overloads[0] : null;

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
