namespace gd2cs.Model;

// Stores every translated script element in source order.
public sealed record ScriptModel(List<SyntaxElement> Elements)
{
    // Resolves the single root class without removing surrounding trivia from Elements.
    public ClassDeclaration Class => Elements.OfType<ClassDeclaration>().Single();
}

// Provides one ordered base type for declarations, statements, comments, and blank lines.
public abstract record SyntaxElement;

// Preserves a standalone comment independently of the declaration following it.
public sealed record CommentElement(string Text) : SyntaxElement;

// Preserves one intentional empty source line.
public sealed record BlankLineElement : SyntaxElement;

// Represents either the root Godot class or a class reconstructed from a nested declaration.
public sealed record ClassDeclaration(
    string Name,
    TypeReference BaseType,
    bool IsGlobal,
    List<SyntaxElement> Elements) : SyntaxElement;

// Stores a dialect-neutral type name and any nested generic type arguments.
public sealed record TypeReference(string Name, List<TypeReference> TypeArguments)
{
    // Creates a non-generic type reference without requiring an empty argument list at call sites.
    public TypeReference(string name) : this(name, new List<TypeReference>())
    {
    }

    // Classifies equivalent C# and GDScript spellings as the same semantic type.
    public TypeKind Kind => Name switch
    {
        "void" or "Void" => TypeKind.Void,
        "bool" or "Bool" => TypeKind.Bool,
        "int" or "Int32" => TypeKind.Int32,
        "int64" or "Int64" => TypeKind.Int64,
        "float" or "Float32" => TypeKind.Float32,
        "float64" or "Float64" => TypeKind.Float64,
        "String" or "string" => TypeKind.String,
        "StringName" => TypeKind.StringName,
        "NodePath" => TypeKind.NodePath,
        "Variant" => TypeKind.Variant,
        "Callable" => TypeKind.Callable,
        "Array" => TypeKind.Array,
        "Dictionary" => TypeKind.Dictionary,
        "RefCounted" or "GodotObject" => TypeKind.GodotObject,
        _ => TypeKind.UserObject
    };
}

// Identifies the supported semantic type families independently of source spelling.
public enum TypeKind
{
    Unknown,
    Void,
    Bool,
    Int32,
    Int64,
    Float32,
    Float64,
    String,
    StringName,
    NodePath,
    Variant,
    Callable,
    GodotObject,
    UserObject,
    Array,
    Dictionary
}

// Stores a constant literal and whether its source type was inferred.
public sealed record ConstantDeclaration(
    string Name,
    TypeReference Type,
    Expression Value,
    bool IsInferred) : MemberDeclaration;

// Stores a class field whose initializer may be intentionally absent.
public sealed record FieldDeclaration(
    string Name,
    TypeReference Type,
    Expression? Initializer,
    bool IsInferred) : MemberDeclaration;

// Provides the language-neutral base for supported initializer expressions.
public abstract record Expression;

// Preserves the canonical text of a scalar literal or identifier expression.
public sealed record ValueExpression(string Text) : Expression;

// Represents a scalar conversion independently of each language's cast syntax.
public sealed record ConversionExpression(
    TypeReference Type,
    Expression Value) : Expression;

// Represents construction while retaining whether Godot treats the type as a built-in value.
public sealed record ObjectCreationExpression(
    TypeReference Type,
    ArgumentList Arguments,
    ConstructionKind Kind = ConstructionKind.Object) : Expression;

// Separates reference construction from Godot's directly callable built-in value types.
public enum ConstructionKind
{
    Object,
    BuiltInValue
}

// Preserves line breaks before individual array elements and before the closing delimiter.
public sealed record ArrayExpression(
    List<ArrayElement> Elements,
    bool ClosingDelimiterStartsOnNewLine) : Expression;

// Stores one array value and whether it starts on a new source line.
public sealed record ArrayElement(
    Expression Value,
    bool StartsOnNewLine);

// Invokes a structural target without flattening away its receiver expression.
public sealed record InvocationExpression(
    Expression Target,
    ArgumentList Arguments) : Expression;

// Identifies a reflected Godot global function without storing either language's spelling.
public sealed record GodotGlobalFunctionReference(
    string DeclaringType,
    string Name,
    TypeReference ResultType);

// Invokes a Godot global function after its source spelling has been semantically resolved.
public sealed record GodotGlobalFunctionExpression(
    GodotGlobalFunctionReference Function,
    ArgumentList Arguments) : Expression;

// Identifies a reflected Godot instance method independently of its source-language name.
public sealed record GodotMethodReference(
    string DeclaringType,
    string Name,
    TypeReference ResultType);

// Invokes a type-bound Godot method while retaining its receiver expression.
public sealed record GodotMethodInvocationExpression(
    Expression Target,
    GodotMethodReference Method,
    ArgumentList Arguments) : Expression;

// Represents collection behavior independently of method and property spellings.
public sealed record CollectionOperationExpression(
    Expression Target,
    CollectionOperation Operation,
    ArgumentList Arguments) : Expression;

// Distinguishes operations whose source spelling depends on both language and collection kind.
public enum CollectionOperation
{
    Count,
    IsEmpty,
    Append,
    Clear,
    RemoveValue,
    RemoveKey,
    ContainsValue,
    ContainsKey
}

// Preserves line breaks before individual arguments and before the closing parenthesis.
public sealed record ArgumentList(
    List<Argument> Arguments,
    bool ClosingParenthesisStartsOnNewLine);

// Stores one argument and whether it starts on a new source line.
public sealed record Argument(
    Expression Value,
    bool StartsOnNewLine);

// Represents arithmetic while preserving operator precedence in the expression tree.
public sealed record BinaryExpression(
    Expression Left,
    string Operator,
    Expression Right) : Expression;

// Represents a prefix operator using its language-neutral spelling.
public sealed record UnaryExpression(
    string Operator,
    Expression Operand) : Expression;

// Normalizes C# ?: and GDScript's value-if-condition-else expression order.
public sealed record ConditionalExpression(
    Expression Condition,
    Expression WhenTrue,
    Expression WhenFalse) : Expression;

// Stores text and evaluated values in their original interpolation order.
public sealed record InterpolatedStringExpression(
    List<InterpolationPart> Parts) : Expression;

// Provides the language-neutral base for interpolated string contents.
public abstract record InterpolationPart;

// Preserves literal text between interpolated values.
public sealed record InterpolationText(string Text) : InterpolationPart;

// Stores an interpolated value and an optional canonical C#-style format.
public sealed record InterpolationValue(
    Expression Value,
    string? Format) : InterpolationPart;

// Preserves explicit grouping even when precedence would produce the same result.
public sealed record ParenthesizedExpression(Expression Expression) : Expression;

// Preserves member access structurally so it can also be used as an assignment target.
public sealed record MemberAccessExpression(
    Expression Target,
    string Member) : Expression;

// Identifies one reflected Godot member without storing either language's spelling.
public sealed record GodotMemberReference(
    string DeclaringType,
    string Name,
    TypeReference ResultType);

// Preserves a type-bound Godot property or field access for dialect-specific emission.
public sealed record GodotMemberAccessExpression(
    Expression Target,
    GodotMemberReference Member) : Expression;

// Applies an index expression to another expression, including chained indexed access.
public sealed record IndexExpression(
    Expression Target,
    Expression Index) : Expression;

// Keeps a nested class in the ordered element list of its containing class.
public sealed record NestedClassDeclaration(
    string Name,
    TypeReference BaseType,
    List<SyntaxElement> Elements) : MemberDeclaration;

// Shares ordered parameters and body elements across constructors, methods, and destructors.
public abstract record CallableDeclaration(
    ParameterList Parameters,
    List<SyntaxElement> Elements) : MemberDeclaration;

// Maps a C# constructor to GDScript's _init function.
public sealed record ConstructorDeclaration(
    ParameterList Parameters,
    List<SyntaxElement> Elements) : CallableDeclaration(Parameters, Elements);

// Represents a named callable with an explicit language-neutral return type.
public sealed record MethodDeclaration(
    string Name,
    TypeReference ReturnType,
    ParameterList Parameters,
    List<SyntaxElement> Elements,
    bool IsStatic = false) : CallableDeclaration(Parameters, Elements);

// Maps a C# finalizer to the supported GDScript cleanup function.
public sealed record DestructorDeclaration(
    List<SyntaxElement> Elements) : CallableDeclaration(new ParameterList(new List<ParameterDeclaration>(), false), Elements);

// Stores ordered callable parameters and the closing-parenthesis line layout.
public sealed record ParameterList(
    List<ParameterDeclaration> Parameters,
    bool ClosingParenthesisStartsOnNewLine);

// Represents one callable parameter and whether it starts on a new source line.
public sealed record ParameterDeclaration(
    string Name,
    TypeReference Type,
    bool StartsOnNewLine = false);

// Provides the language-neutral base for callable-body operations.
public abstract record Statement : SyntaxElement;

// Represents assignment to any supported assignable expression.
public sealed record AssignmentStatement(Expression Target, Expression Value) : Statement;

// Represents either a value-returning statement or a bare return.
public sealed record ReturnStatement(Expression? Value) : Statement;

// Represents a function call whose result is intentionally ignored.
public sealed record ExpressionStatement(Expression Expression) : Statement;

// Normalizes typed or inferred locals whose initializer may be intentionally absent.
public sealed record LocalVariableStatement(
    string Name,
    TypeReference? Type,
    Expression? Initializer,
    bool IsInferred) : Statement;

// Iterates over a collection while preserving the ordered nested body.
public sealed record ForEachStatement(
    string Variable,
    Expression Collection,
    List<SyntaxElement> Elements) : Statement;

// Repeats an ordered body while its condition remains true.
public sealed record WhileStatement(
    Expression Condition,
    List<SyntaxElement> Elements) : Statement;

// Stores one condition and body in an if or else-if chain.
public sealed record ConditionalBranch(
    Expression Condition,
    List<SyntaxElement> Elements);

// Preserves an if chain and its optional final else body.
public sealed record ConditionalStatement(
    List<ConditionalBranch> Branches,
    List<SyntaxElement>? ElseElements) : Statement;

// Exits the nearest supported loop.
public sealed record BreakStatement : Statement;

// Advances the nearest supported loop to its next iteration.
public sealed record ContinueStatement : Statement;

// Marks declarations that are valid inside an ordered class body.
public abstract record MemberDeclaration : SyntaxElement;
