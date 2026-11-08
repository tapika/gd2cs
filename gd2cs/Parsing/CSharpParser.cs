using System.Text;
using gd2cs.Language;
using gd2cs.Model;

namespace gd2cs.Parsing;

public sealed class CSharpParser : IScriptParser
{
    // These names are reserved for generated C#-only helpers.
    private static readonly HashSet<string> generatedHelperNames = new(StringComparer.Ordinal)
    {
        "gdCallableIsValid", "gdStr", "gdSortCustom", "gdArray", "gdDictKeys", "gdDictValues"
    };

    private TokenReader tokens = null!;
    private int groupingDepth;
    private readonly HashSet<string> enumTypeNames = new(StringComparer.Ordinal);

    public ScriptModel Parse(string source)
    {
        CollectEnumNames(source);
        tokens = new TokenReader(new Lexer(source, true, cSharpIdentifiers: true));
        groupingDepth = 0;
        ParseUsing("Godot");
        ParseUsing("System");
        if (tokens.Current.Text == "using")
            ParseUsing("System.Linq");
        tokens.TryConsume(TokenKind.NewLine);

        var elements = new List<SyntaxElement>();
        while (ParseTrivia(elements))
        {
        }
        elements.Add(ParseClass(true));
        while (ParseTrivia(elements))
        {
        }
        tokens.Expect(TokenKind.EndOfFile);
        return new ScriptModel(elements);
    }

    // Enum casts may precede their declarations. A token-only prepass distinguishes
    // their cast parentheses from grouped values; scoped type binding still happens later.
    private void CollectEnumNames(string source)
    {
        enumTypeNames.Clear();
        var lexer = new Lexer(source, false, cSharpIdentifiers: true);
        var followsEnum = false;
        for (var token = lexer.Next(); token.Kind != TokenKind.EndOfFile; token = lexer.Next())
        {
            if (followsEnum && token.Kind == TokenKind.Identifier)
                enumTypeNames.Add(token.Text);
            followsEnum = token.Kind == TokenKind.Identifier && token.Text == "enum";
        }
    }

    private bool IsEnumConversion()
    {
        if (tokens.Current.Kind != TokenKind.OpenParenthesis)
            return false;
        var offset = 1;
        return LookAheadType(ref offset) && tokens.PeekAt(offset).Kind == TokenKind.CloseParenthesis
            && enumTypeNames.Contains(tokens.PeekAt(offset - 1).Text)
            && tokens.PeekAt(offset + 1).Kind is TokenKind.Identifier or TokenKind.Number
                or TokenKind.OpenParenthesis or TokenKind.Plus or TokenKind.Minus or TokenKind.Tilde;
    }

    // Uses one grammar path for global and nested partial class declarations.
    private ClassDeclaration ParseClass(bool isGlobal)
    {
        if (isGlobal)
        {
            tokens.Expect(TokenKind.OpenBracket);
            tokens.Expect(TokenKind.Identifier, "GlobalClass");
            tokens.Expect(TokenKind.CloseBracket);
            EndOfLine();
        }
        ExpectPublic();
        tokens.Expect(TokenKind.Identifier, "partial");
        tokens.Expect(TokenKind.Identifier, "class");
        var name = tokens.Expect(TokenKind.Identifier).Text;
        tokens.Expect(TokenKind.Colon);
        var baseType = ParseType();
        EndOfLine();
        tokens.Expect(TokenKind.OpenBrace);
        EndOfLine();

        var elements = new List<SyntaxElement>();
        while (tokens.Current.Kind != TokenKind.CloseBrace)
        {
            if (tokens.Current.Kind == TokenKind.NewLine && IsGeneratedHelper(1))
            {
                tokens.TryConsume(TokenKind.NewLine);
                continue;
            }
            if (IsGeneratedHelper())
            {
                ParseGeneratedHelper();
                continue;
            }
            if (ParseTrivia(elements))
                continue;
            if (tokens.Current.Kind == TokenKind.OpenBracket)
            {
                elements.Add(ParseSignal());
                continue;
            }
            if (tokens.Current.Kind == TokenKind.Tilde)
            {
                tokens.Expect(TokenKind.Tilde);
                tokens.Expect(TokenKind.Identifier, name);
                var callable = ParseCallable();
                elements.Add(new DestructorDeclaration(callable.Elements));
                continue;
            }

            if (tokens.Current.Text == "public" && tokens.Peek.Text == "partial")
            {
                elements.Add(ToNested(ParseClass(false)));
                continue;
            }

            ExpectPublic();
            if (tokens.Current.Text == "enum")
            {
                elements.Add(EnumParser.Parse(tokens, () => ParseExpression(), ScriptLanguage.CSharp));
                continue;
            }
            var isStatic = tokens.TryConsume(TokenKind.Identifier, "static");
            var isOverride = tokens.TryConsume(TokenKind.Identifier, "override");
            var isReadonly = isStatic && tokens.TryConsume(TokenKind.Identifier, "readonly");
            if (tokens.Current.Text == name && tokens.Peek.Kind == TokenKind.OpenParenthesis)
            {
                tokens.Expect(TokenKind.Identifier, name);
                elements.Add(ParseCallable());
                continue;
            }
            if (tokens.Current.Text == "const")
            {
                tokens.Expect(TokenKind.Identifier, "const");
                elements.Add(ParseScalar(true));
                continue;
            }

            var type = ParseType();
            var memberName = tokens.Expect(TokenKind.Identifier).Text;
            if (isReadonly)
            {
                var field = ParseScalarAfterName(type, memberName);
                if (field.Initializer is null)
                    throw new ParseException("A readonly constant requires a value", tokens.Current.Line, tokens.Current.Column);
                // Collection constants retain their explicit element/key/value types in GDScript.
                elements.Add(new ConstantDeclaration(memberName, type, field.Initializer,
                    field.Initializer is not (ArrayExpression or DictionaryExpression)));
                continue;
            }
            if (tokens.PeekAfterNewLines().Kind == TokenKind.OpenBrace)
            {
                elements.Add(ParseProperty(type, memberName, isStatic));
                continue;
            }
            elements.Add(tokens.Current.Kind == TokenKind.OpenParenthesis
                ? ParseMethod(type, memberName, isStatic, isOverride)
                : ParseScalarAfterName(type, memberName) with { IsStatic = isStatic });
        }

        tokens.Expect(TokenKind.CloseBrace);
        EndOfLine(optional: true);
        return new ClassDeclaration(name, baseType, isGlobal, elements);
    }

    private static NestedClassDeclaration ToNested(ClassDeclaration declaration) =>
        new(declaration.Name, declaration.BaseType, declaration.Elements);

    // C# defaults class members to private; GD cannot preserve restricted visibility.
    private void ExpectPublic()
    {
        if (!tokens.TryConsume(TokenKind.Identifier, "public"))
            throw new ParseException("Non-public C# declarations are not supported; change the access modifier to public.",
                tokens.Current.Line, tokens.Current.Column);
    }

    // Restrict the reserved names to private static methods, not fields or instance methods.
    private bool IsGeneratedHelper(int offset = 0)
    {
        if (tokens.PeekAt(offset).Text != "private" || tokens.PeekAt(offset + 1).Text != "static")
            return false;
        offset += 2;
        if (!LookAheadType(ref offset) || !generatedHelperNames.Contains(tokens.PeekAt(offset).Text))
            return false;
        return tokens.PeekAt(offset + 1).Kind is TokenKind.OpenParenthesis or TokenKind.LessThan;
    }

    // C# setter parameters are implicit. Fold only our leading alias when value is otherwise unused.
    private PropertyDeclaration ParseProperty(TypeReference type, string name, bool isStatic)
    {
        EndOfLine(optional: true);
        tokens.Expect(TokenKind.OpenBrace);
        EndOfLine();
        var elements = new List<SyntaxElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (tokens.Current.Kind != TokenKind.CloseBrace)
        {
            if (ParseTrivia(elements))
                continue;
            if (tokens.Current.Text is "private" or "protected" or "internal")
                throw new ParseException("Non-public C# property accessors are not supported; remove the access modifier so the accessor is public.",
                    tokens.Current.Line, tokens.Current.Column);
            var accessor = tokens.Expect(TokenKind.Identifier);
            if (accessor.Text is not ("get" or "set") || !seen.Add(accessor.Text))
                throw new ParseException("Expected one get and/or set accessor.", accessor.Line, accessor.Column);
            if (tokens.Current.Kind == TokenKind.Semicolon)
                throw new ParseException("Auto-properties are not supported; use explicit accessor bodies and a backing field.", accessor.Line, accessor.Column);
            var body = ParseBracedBody();
            var parameter = accessor.Text == "get" ? null : "value";
            if (parameter is not null && body.FirstOrDefault() is LocalVariableStatement local
                && local.Typing == LocalTyping.Inferred && local.Initializer is ValueExpression { Text: "value" }
                && local.Name != "value" && !PropertySemantics.UsesBareValue(body.Skip(1)))
            {
                parameter = local.Name;
                body.RemoveAt(0);
            }
            elements.Add(SourceLocations.Record(new PropertyAccessorDeclaration(accessor.Text == "get", parameter, body), accessor));
        }
        tokens.Expect(TokenKind.CloseBrace);
        EndOfLine(optional: true);
        if (seen.Count == 0)
            throw new ParseException("A property requires at least one accessor.", tokens.Current.Line, tokens.Current.Column);
        return new PropertyDeclaration(name, type, elements, isStatic);
    }

    // Godot registers the delegate name without EventHandler as the exact signal name.
    private SignalDeclaration ParseSignal()
    {
        tokens.Expect(TokenKind.OpenBracket);
        tokens.Expect(TokenKind.Identifier, "Signal");
        tokens.Expect(TokenKind.CloseBracket);
        EndOfLine(optional: true);
        ExpectPublic();
        tokens.Expect(TokenKind.Identifier, "delegate");
        tokens.Expect(TokenKind.Identifier, "void");
        var name = tokens.Expect(TokenKind.Identifier);
        const string suffix = "EventHandler";
        if (!name.Text.EndsWith(suffix, StringComparison.Ordinal) || name.Text.Length == suffix.Length)
            throw new ParseException("Signal delegate names must end with EventHandler.", name.Line, name.Column);
        var parameters = ParseParameters();
        tokens.Expect(TokenKind.Semicolon);
        EndOfLine();
        return new SignalDeclaration(name.Text[..^suffix.Length], parameters);
    }

    private MethodDeclaration ParseMethod(TypeReference returnType, string name, bool isStatic, bool isOverride)
    {
        var callable = ParseCallable();
        if (!isOverride)
            return new MethodDeclaration(name, returnType, callable.Parameters, callable.Elements, isStatic);
        if (name != "ToString" || returnType.Kind != TypeKind.String || callable.Parameters.Parameters.Count != 0)
            throw new NotSupportedException("Unsupported language dialect.");
        return new MethodDeclaration(name, returnType, callable.Parameters, callable.Elements, false, MethodKind.StringConversion);
    }

    private FieldDeclaration ParseScalarAfterName(TypeReference type, string name)
    {
        if (tokens.TryConsume(TokenKind.Semicolon))
        {
            EndOfLine();
            return new FieldDeclaration(name, type, null, false);
        }
        tokens.Expect(TokenKind.Equals);
        var value = ParseExpression();
        tokens.Expect(TokenKind.Semicolon);
        EndOfLine();
        // Initialized fields can use GDScript's inferred declaration syntax, including arrays.
        // Fold only exact empty container construction; populated or different types retain their values.
        if (IsContainerDefault(type, value))
            return new FieldDeclaration(name, type, null, false);
        return new FieldDeclaration(name, type, value, value is not DictionaryExpression);
    }

    private ConstructorDeclaration ParseCallable()
        => new(ParseParameters(), ParseBracedBody());

    // Delegates and callable declarations use the same parameter grammar and layout.
    private ParameterList ParseParameters()
    {
        tokens.Expect(TokenKind.OpenParenthesis);
        var parameters = new List<ParameterDeclaration>();
        var startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
        if (!tokens.TryConsume(TokenKind.CloseParenthesis))
        {
            while (true)
            {
                var type = ParseType(allowEventDouble: true);
                var name = tokens.Expect(TokenKind.Identifier).Text;
                var defaultValue = tokens.TryConsume(TokenKind.Equals) ? ParseExpression() : null;
                parameters.Add(new ParameterDeclaration(name, type, startsOnNewLine, defaultValue));
                if (!tokens.TryConsume(TokenKind.Comma))
                {
                    var closingOnNewLine = tokens.TryConsume(TokenKind.NewLine);
                    tokens.Expect(TokenKind.CloseParenthesis);
                    return new ParameterList(parameters, closingOnNewLine);
                }
                startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
                if (tokens.TryConsume(TokenKind.CloseParenthesis))
                    break;
            }
        }
        return new ParameterList(parameters, startsOnNewLine);
    }

    // Consumes braces while delegating their ordered contents to the shared body parser.
    private List<SyntaxElement> ParseBracedBody()
    {
        EndOfLine();
        tokens.Expect(TokenKind.OpenBrace);
        EndOfLine();
        var elements = ParseBody();
        tokens.Expect(TokenKind.CloseBrace);
        EndOfLine(optional: true);
        return elements;
    }

    // Reads recursively nested statements, comments, and blank lines up to a closing brace.
    private List<SyntaxElement> ParseBody()
    {
        var elements = new List<SyntaxElement>();
        while (tokens.Current.Kind != TokenKind.CloseBrace)
        {
            if (ParseTrivia(elements))
                continue;
            if (tokens.Current.Text == "foreach")
            {
                elements.Add(ParseForEach());
                continue;
            }
            if (tokens.Current.Text == "for")
            {
                elements.Add(ParseFor());
                continue;
            }
            if (tokens.Current.Text == "while")
            {
                elements.Add(ParseWhile());
                continue;
            }
            if (tokens.Current.Text == "if")
            {
                elements.Add(ParseConditional());
                continue;
            }
            if (tokens.TryConsume(TokenKind.Identifier, "break"))
            {
                tokens.Expect(TokenKind.Semicolon);
                EndOfLine();
                elements.Add(new BreakStatement());
                continue;
            }
            if (tokens.TryConsume(TokenKind.Identifier, "continue"))
            {
                tokens.Expect(TokenKind.Semicolon);
                EndOfLine();
                elements.Add(new ContinueStatement());
                continue;
            }
            var returnToken = tokens.Current;
            if (tokens.TryConsume(TokenKind.Identifier, "return"))
            {
                var returnValue = tokens.Current.Kind == TokenKind.Semicolon ? null : ParseExpression();
                tokens.Expect(TokenKind.Semicolon);
                EndOfLine();
                elements.Add(SourceLocations.Record(new ReturnStatement(returnValue), returnToken));
                continue;
            }
            if (IsLocalDeclaration())
            {
                var statementToken = tokens.Current;
                // C# var carries the same language-neutral inference state as GDScript :=.
                var isInferred = tokens.TryConsume(TokenKind.Identifier, "var");
                var type = isInferred ? null : ParseType();
                var name = tokens.Expect(TokenKind.Identifier).Text;
                var hasInitializer = tokens.TryConsume(TokenKind.Equals);
                if (isInferred && !hasInitializer)
                    throw new ParseException("An inferred local requires a value", tokens.Current.Line, tokens.Current.Column);
                var initializer = hasInitializer ? ParseExpression() : null;
                if (!isInferred && initializer is not null && IsGeneratedDefault(type!, initializer))
                    initializer = null;
                tokens.Expect(TokenKind.Semicolon);
                EndOfLine();
                elements.Add(SourceLocations.Record(new LocalVariableStatement(
                    name,
                    type,
                    initializer,
                    isInferred ? LocalTyping.Inferred : LocalTyping.Explicit), statementToken));
                continue;
            }
            var statementStart = tokens.Current;
            var expression = ParseExpression();
            if (tokens.TryConsumeAssignment(out var operation))
            {
                var value = ParseExpression();
                tokens.Expect(TokenKind.Semicolon);
                EndOfLine();
                elements.Add(SourceLocations.Record(new AssignmentStatement(expression, value, operation), statementStart));
                continue;
            }
            if (expression is not InvocationExpression)
                throw new ParseException("Expected a function call", tokens.Current.Line, tokens.Current.Column);
            tokens.Expect(TokenKind.Semicolon);
            EndOfLine();
            elements.Add(new ExpressionStatement(expression));
        }
        return elements;
    }

    // Counter headers share expressions and assignments with ordinary statements, but
    // increment syntax has no expression value here: it becomes an additive assignment.
    private CounterLoopStatement ParseFor()
    {
        var opening = tokens.Expect(TokenKind.Identifier, "for");
        tokens.Expect(TokenKind.OpenParenthesis);
        Statement initializer;
        string name;
        if (IsLocalDeclaration())
        {
            var inferred = tokens.TryConsume(TokenKind.Identifier, "var");
            var type = inferred ? null : ParseType();
            name = tokens.Expect(TokenKind.Identifier).Text;
            tokens.Expect(TokenKind.Equals);
            initializer = new LocalVariableStatement(name, type, ParseExpression(),
                inferred ? LocalTyping.Inferred : LocalTyping.Explicit);
        }
        else
        {
            name = tokens.Expect(TokenKind.Identifier).Text;
            tokens.Expect(TokenKind.Equals);
            initializer = new AssignmentStatement(new ValueExpression(name), ParseExpression());
        }
        tokens.Expect(TokenKind.Semicolon);
        var condition = tokens.Current.Kind == TokenKind.Semicolon ? new ValueExpression("true") : ParseExpression();
        tokens.Expect(TokenKind.Semicolon);
        var iterator = ParseForIterator(name);
        tokens.Expect(TokenKind.CloseParenthesis);
        return SourceLocations.Record(new CounterLoopStatement(initializer, condition, iterator, ParseBracedBody()), opening);
    }

    private AssignmentStatement ParseForIterator(string name)
    {
        var opening = tokens.Current;
        var prefix = tokens.TryConsume(TokenKind.PlusPlus) ? "+="
            : tokens.TryConsume(TokenKind.MinusMinus) ? "-=" : null;
        tokens.Expect(TokenKind.Identifier, name);
        var operation = prefix ?? (tokens.TryConsume(TokenKind.PlusPlus) ? "+="
            : tokens.TryConsume(TokenKind.MinusMinus) ? "-=" : null);
        if (operation is not null)
            return new AssignmentStatement(new ValueExpression(name), new ValueExpression("1"), operation);
        if (!tokens.TryConsumeAssignment(out operation))
            throw new ParseException("For loops require one counter update: ++, --, += or -=.", opening.Line, opening.Column);
        var value = ParseExpression();
        if (operation == "=" && value is BinaryExpression { Left: ValueExpression left, Operator: "+" or "-" } binary
            && left.Text == name)
        {
            operation = binary.Operator == "+" ? "+=" : "-=";
            value = binary.Right;
        }
        if (operation is not ("+=" or "-="))
            throw new ParseException("For loops require an additive counter update (++/--, +=/-=, or counter = counter +/- step).",
                opening.Line, opening.Column);
        return new AssignmentStatement(new ValueExpression(name), value, operation);
    }

    private ForEachStatement ParseForEach()
    {
        tokens.Expect(TokenKind.Identifier, "foreach");
        tokens.Expect(TokenKind.OpenParenthesis);
        var type = tokens.TryConsume(TokenKind.Identifier, "var") ? null : ParseType();
        var variable = tokens.Expect(TokenKind.Identifier).Text;
        tokens.Expect(TokenKind.Identifier, "in");
        var collection = ParseExpression();
        tokens.Expect(TokenKind.CloseParenthesis);
        return new ForEachStatement(variable, collection, ParseBracedBody(), type);
    }

    private WhileStatement ParseWhile()
    {
        tokens.Expect(TokenKind.Identifier, "while");
        tokens.Expect(TokenKind.OpenParenthesis);
        var condition = ParseExpression();
        tokens.Expect(TokenKind.CloseParenthesis);
        return new WhileStatement(condition, ParseBracedBody());
    }

    // Groups C# else-if and else blocks into one language-neutral conditional.
    private ConditionalStatement ParseConditional()
    {
        var branches = new List<ConditionalBranch>();
        tokens.Expect(TokenKind.Identifier, "if");
        branches.Add(new ConditionalBranch(ParseCondition(), ParseBracedBody()));
        while (tokens.TryConsume(TokenKind.Identifier, "else"))
        {
            if (!tokens.TryConsume(TokenKind.Identifier, "if"))
                return new ConditionalStatement(branches, ParseBracedBody());
            branches.Add(new ConditionalBranch(ParseCondition(), ParseBracedBody()));
        }
        return new ConditionalStatement(branches, null);
    }

    private Expression ParseCondition()
    {
        tokens.Expect(TokenKind.OpenParenthesis);
        var condition = ParseExpression();
        tokens.Expect(TokenKind.CloseParenthesis);
        return condition;
    }

    private MemberDeclaration ParseScalar(bool isConstant)
    {
        var type = ParseType();
        var name = tokens.Expect(TokenKind.Identifier).Text;
        tokens.Expect(TokenKind.Equals);
        var value = ParseExpression();
        tokens.Expect(TokenKind.Semicolon);
        EndOfLine();
        return isConstant
            ? new ConstantDeclaration(name, type, value, true)
            : new FieldDeclaration(name, type, value, true);
    }

    // A type followed by a variable name distinguishes declarations from member calls
    // and comparison expressions. Lookahead leaves actual parsing and diagnostics intact.
    private bool IsLocalDeclaration()
    {
        var offset = 0;
        return LookAheadType(ref offset)
            && tokens.PeekAt(offset).Kind == TokenKind.Identifier;
    }

    // Mirrors the supported type grammar, including qualified and nested generic types.
    private bool LookAheadType(ref int offset)
    {
        if (tokens.PeekAt(offset).Kind != TokenKind.Identifier)
            return false;
        offset++;
        while (tokens.PeekAt(offset).Kind == TokenKind.Dot)
        {
            offset++;
            if (tokens.PeekAt(offset).Kind != TokenKind.Identifier)
                return false;
            offset++;
        }
        if (tokens.PeekAt(offset).Kind != TokenKind.LessThan)
            return LookAheadArraySuffix(ref offset);
        offset++;
        do
        {
            if (!LookAheadType(ref offset))
                return false;
            if (tokens.PeekAt(offset).Kind != TokenKind.Comma)
                break;
            offset++;
        }
        while (true);
        if (tokens.PeekAt(offset).Kind != TokenKind.GreaterThan)
            return false;
        offset++;
        return LookAheadArraySuffix(ref offset);
    }

    private bool LookAheadArraySuffix(ref int offset)
    {
        if (tokens.PeekAt(offset).Kind != TokenKind.OpenBracket)
            return true;
        if (tokens.PeekAt(offset + 1).Kind != TokenKind.CloseBracket)
            return false;
        offset += 2;
        return true;
    }

    private TypeReference ParseType(bool allowEventDouble = false)
    {
        var typeToken = tokens.Current;
        var name = tokens.Expect(TokenKind.Identifier).Text;
        while (tokens.TryConsume(TokenKind.Dot))
        {
            name += "." + tokens.Expect(TokenKind.Identifier).Text;
        }
        if (PackedArrayMappings.TryElement(name, out _))
            throw new ParseException(PackedArrayMappings.StorageError(name), typeToken.Line, typeToken.Column);
        // Only parameter positions may temporarily contain a native event double. Semantic
        // binding must fold its generated float conversion before final parameter validation.
        if (name is "double" or "Double" or "System.Double")
        {
            if (!allowEventDouble || tokens.Current.Kind == TokenKind.OpenBracket)
                throw new ParseException("C# double is not supported; use float instead.", typeToken.Line, typeToken.Column);
            name = "float64";
        }
        if (name is "Godot.Collections.Array" or "Godot.Collections.Dictionary")
            name = name["Godot.Collections.".Length..];

        var arguments = new List<TypeReference>();
        if (tokens.TryConsume(TokenKind.LessThan))
        {
            do
            {
                arguments.Add(ParseType());
            }
            while (tokens.TryConsume(TokenKind.Comma));
            tokens.Expect(TokenKind.GreaterThan);
        }
        if (name == "Dictionary" && arguments.Count != 2)
            throw new ParseException("Untyped dictionaries are not supported; specify Dictionary[KeyType, ValueType] or use a class for mixed fields.", typeToken.Line, typeToken.Column);
        var type = SourceLocations.Record(new TypeReference(name switch { "String" => "string", "long" => "int64", _ => name }, arguments), typeToken);
        if (tokens.TryConsume(TokenKind.OpenBracket))
        {
            tokens.Expect(TokenKind.CloseBracket);
            return new TypeReference("ArrayBuffer", new List<TypeReference> { type });
        }
        return type;
    }

    // Reserved helper implementations are C#-only; their names, not signatures, identify them.
    private void ParseGeneratedHelper()
    {
        while (tokens.Current.Kind != TokenKind.OpenBrace)
        {
            if (tokens.Current.Kind is TokenKind.EndOfFile or TokenKind.Semicolon)
                throw new ParseException("Expected a generated helper body", tokens.Current.Line, tokens.Current.Column);
            tokens.Expect(tokens.Current.Kind);
        }
        tokens.Expect(TokenKind.OpenBrace);
        var depth = 1;
        while (depth > 0)
        {
            if (tokens.Current.Kind == TokenKind.EndOfFile)
                throw new ParseException("Unterminated generated helper", tokens.Current.Line, tokens.Current.Column);
            if (tokens.Current.Kind == TokenKind.OpenBrace)
                depth++;
            else if (tokens.Current.Kind == TokenKind.CloseBrace)
                depth--;
            tokens.Expect(tokens.Current.Kind);
        }
        EndOfLine(optional: true);
    }

    private ArrayExpression ParseArray()
    {
        var opening = tokens.Expect(TokenKind.Identifier, "gdArray");
        tokens.Expect(TokenKind.OpenParenthesis);
        // Apply the same empty-literal restriction when backtranslating the array helper.
        if (tokens.PeekAfterNewLines().Kind == TokenKind.CloseParenthesis)
            throw new ParseException("Empty array literals are not supported; declare an array type without an initializer instead.", opening.Line, opening.Column);
        var startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
        var elements = new List<ArrayElement>();
        while (tokens.Current.Kind != TokenKind.CloseParenthesis)
        {
            elements.Add(new ArrayElement(ParseExpression(), startsOnNewLine));
            if (tokens.TryConsume(TokenKind.Comma))
            {
                startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
                continue;
            }
            var closingOnNewLine = tokens.TryConsume(TokenKind.NewLine);
            tokens.Expect(TokenKind.CloseParenthesis);
            return new ArrayExpression(elements, closingOnNewLine);
        }
        tokens.Expect(TokenKind.CloseParenthesis);
        return new ArrayExpression(elements, startsOnNewLine);
    }

    // Collection initializer entries map directly to GDScript dictionary key/value pairs.
    private DictionaryExpression ParseDictionary(TypeReference type)
    {
        var opening = tokens.Expect(TokenKind.OpenBrace);
        var lineBreaks = tokens.ConsumeLineBreaks();
        var entries = new List<DictionaryEntry>();
        var trailingComma = false;
        while (tokens.Current.Kind != TokenKind.CloseBrace)
        {
            tokens.Expect(TokenKind.OpenBrace);
            var key = ParseExpression();
            tokens.Expect(TokenKind.Comma);
            var value = ParseExpression();
            tokens.Expect(TokenKind.CloseBrace);
            entries.Add(new DictionaryEntry(key, value, lineBreaks));
            trailingComma = tokens.TryConsume(TokenKind.Comma);
            lineBreaks = tokens.ConsumeLineBreaks();
            if (!trailingComma)
                break;
        }
        tokens.Expect(TokenKind.CloseBrace);
        return SourceLocations.Record(new DictionaryExpression(entries, lineBreaks, trailingComma, type), opening);
    }

    // Normalizes C# operators while building precedence-aware expression trees.
    private Expression ParseExpression(int minimumPrecedence = 0)
    {
        var left = ParseUnaryExpression();
        while (true)
        {
            // Consume continuation lines only when this precedence level owns the following operator.
            var beforeOperator = groupingDepth > 0 && tokens.Current.Kind == TokenKind.NewLine
                && OperatorPrecedence(tokens.PeekAfterNewLines()) >= minimumPrecedence
                    ? tokens.ConsumeLineBreaks() : 0;
            // C# places as casts at the same precedence as relational operators.
            if (minimumPrecedence <= 6 && tokens.TryConsume(TokenKind.Identifier, "as"))
            {
                left = new SafeCastExpression(ParseType(), left);
                continue;
            }
            if (minimumPrecedence <= 6 && tokens.TryConsume(TokenKind.Identifier, "is"))
            {
                var isNotNull = tokens.TryConsume(TokenKind.Identifier, "not");
                if (tokens.Current.Text != "null")
                {
                    var type = ParseType();
                    // Pattern identifiers are optional; contextual words must remain operators.
                    var patternName = tokens.Current.Kind == TokenKind.Identifier && tokens.Current.Text is not ("as" or "is")
                        ? tokens.Expect(TokenKind.Identifier).Text : null;
                    left = new TypeCheckExpression(left, type, isNotNull, patternName);
                    continue;
                }
                tokens.Expect(TokenKind.Identifier, "null");
                if (left is not MemberAccessExpression { Member: "Delegate" } member)
                    throw new ParseException("Callable null check requires Delegate", tokens.Current.Line, tokens.Current.Column);
                left = new CallableNullExpression(member.Target);
                if (isNotNull)
                    left = new UnaryExpression("not", left);
                continue;
            }
            if (OperatorPrecedence(tokens.Current) < minimumPrecedence)
                break;
            var operatorToken = tokens.Current;
            var precedence = OperatorPrecedence(operatorToken);
            tokens.Expect(operatorToken.Kind, operatorToken.Text);
            var afterOperator = groupingDepth > 0 ? tokens.ConsumeLineBreaks() : 0;
            var right = ParseExpression(precedence + 1);
            left = new BinaryExpression(left, NormalizeOperator(operatorToken.Text), right, beforeOperator, afterOperator);
        }
        if (minimumPrecedence == 0 && tokens.TryConsume(TokenKind.Question))
        {
            var whenTrue = ParseExpression();
            tokens.Expect(TokenKind.Colon);
            var whenFalse = ParseExpression();
            return new ConditionalExpression(left, whenTrue, whenFalse);
        }
        return left;
    }

    private Expression ParseUnaryExpression()
    {
        if (tokens.TryConsume(TokenKind.Bang))
            return new UnaryExpression("not", ParseUnaryExpression());
        if (tokens.TryConsume(TokenKind.Tilde))
            return new UnaryExpression("~", ParseUnaryExpression());
        if (tokens.TryConsume(TokenKind.Minus))
            return new UnaryExpression("-", ParseUnaryExpression());
        if (tokens.TryConsume(TokenKind.Plus))
            return new UnaryExpression("+", ParseUnaryExpression());
        return ParsePostfixExpression();
    }

    // Applies member access, indexing, and calls repeatedly to the same base expression.
    private Expression ParsePostfixExpression()
    {
        var opening = tokens.Current;
        var expression = ParsePrimaryExpression();
        while (true)
        {
            if (tokens.TryConsume(TokenKind.Dot))
            {
                var member = tokens.Expect(TokenKind.Identifier).Text;
                // Variant.As<T>() is a typed extraction rather than an ordinary generic call.
                if (member == "As" && tokens.TryConsume(TokenKind.LessThan))
                {
                    var type = ParseType();
                    tokens.Expect(TokenKind.GreaterThan);
                    tokens.Expect(TokenKind.OpenParenthesis);
                    tokens.Expect(TokenKind.CloseParenthesis);
                    expression = SourceLocations.Record(new VariantCastExpression(type, expression), opening);
                    continue;
                }
                expression = SourceLocations.Record(new MemberAccessExpression(expression, member), opening);
                continue;
            }
            if (tokens.TryConsume(TokenKind.OpenBracket))
            {
                var index = ParseExpression();
                tokens.Expect(TokenKind.CloseBracket);
                expression = SourceLocations.Record(new IndexExpression(expression, index), opening);
                continue;
            }
            if (tokens.Current.Kind != TokenKind.OpenParenthesis)
                return expression;
            var arguments = ParseArguments();
            expression = SourceLocations.Record(new InvocationExpression(expression, arguments), opening);
        }
    }

    private Expression ParsePrimaryExpression()
    {
        // LINQ permits a single inferred parameter without surrounding parentheses.
        if (tokens.Current.Kind == TokenKind.Identifier && tokens.Peek.Kind == TokenKind.LambdaArrow)
        {
            var name = tokens.Expect(TokenKind.Identifier).Text;
            tokens.Expect(TokenKind.LambdaArrow);
            var body = ParseExpression();
            return new LambdaExpression(new ParameterList([new ParameterDeclaration(name, new TypeReference(""))], false),
                new TypeReference("void"), [new ExpressionStatement(body)], true);
        }
        // Array helpers are expressions too, allowing chained calls such as gdArray(...).Contains(...).
        if (tokens.Current.Text == "gdArray" && tokens.Peek.Kind == TokenKind.OpenParenthesis)
            return ParseArray();
        if (tokens.Current.Text == "Callable" && tokens.Peek.Kind == TokenKind.Dot)
            return ParseCallableFactory();
        if (tokens.Current.Kind == TokenKind.InterpolatedString)
            return ParseInterpolatedString(tokens.Expect(TokenKind.InterpolatedString).Text);
        if (tokens.Current.Kind == TokenKind.OpenParenthesis && tokens.Peek.Text is "float" or "int" or "uint"
            && tokens.PeekAt(2).Kind == TokenKind.CloseParenthesis)
            return ParseConversion();
        if (IsEnumConversion())
            return ParseConversion();
        if (IsLambda())
            return ParseLambda(new List<TypeReference>());
        if (tokens.Current.Kind == TokenKind.OpenParenthesis)
        {
            var opening = tokens.Expect(TokenKind.OpenParenthesis);
            groupingDepth++;
            var openingLineBreaks = tokens.ConsumeLineBreaks();
            var grouped = ParseExpression();
            var closingLineBreaks = tokens.ConsumeLineBreaks();
            tokens.Expect(TokenKind.CloseParenthesis);
            groupingDepth--;
            return SourceLocations.Record(new ParenthesizedExpression(grouped, openingLineBreaks, closingLineBreaks), opening);
        }
        if (tokens.Current.Text == "new")
        {
            var opening = tokens.Expect(TokenKind.Identifier, "new");
            var type = ParseType();
            if (type.Kind == TypeKind.Dictionary
                && tokens.PeekAfterNewLines().Kind == TokenKind.OpenBrace)
            {
                tokens.ConsumeLineBreaks();
                return ParseDictionary(type);
            }
            var arguments = ParseArguments();
            if (type.Kind == TypeKind.Dictionary
                && arguments.Arguments.Count == 0
                && tokens.PeekAfterNewLines().Kind == TokenKind.OpenBrace)
            {
                tokens.ConsumeLineBreaks();
                return ParseDictionary(type);
            }
            return SourceLocations.Record(new ObjectCreationExpression(type, arguments), opening);
        }

        var value = ConsumeValue();
        if (value.Kind != TokenKind.Identifier)
            return SourceLocations.Record(new ValueExpression(value.Text.TrimEnd('f', 'F')), value);
        if (value.Text == "this")
            return new CurrentInstanceExpression();
        return SourceLocations.Record(new ValueExpression(value.Text), value);
    }

    // Callable.From accepts either a named method group or a lambda; both retain their signatures.
    private Expression ParseCallableFactory()
    {
        tokens.Expect(TokenKind.Identifier, "Callable");
        tokens.Expect(TokenKind.Dot);
        tokens.Expect(TokenKind.Identifier, "From");
        var types = new List<TypeReference>();
        if (tokens.TryConsume(TokenKind.LessThan))
        {
            do
                types.Add(ParseType());
            while (tokens.TryConsume(TokenKind.Comma));
            tokens.Expect(TokenKind.GreaterThan);
        }
        tokens.Expect(TokenKind.OpenParenthesis);
        if (tokens.Current.Kind != TokenKind.OpenParenthesis)
        {
            // Named method groups have no lambda parameter list or arrow after the factory opening.
            var target = ParseExpression();
            tokens.Expect(TokenKind.CloseParenthesis);
            return new CallableMethodGroupExpression(target, types);
        }
        var lambda = ParseLambda(types);
        tokens.Expect(TokenKind.CloseParenthesis);
        return lambda;
    }

    // Distinguishes a typed lambda parameter list from an ordinary grouped expression.
    private bool IsLambda()
    {
        if (tokens.Current.Kind != TokenKind.OpenParenthesis)
            return false;
        var depth = 0;
        for (var offset = 0; ; offset++)
        {
            var token = tokens.PeekAt(offset);
            if (token.Kind == TokenKind.EndOfFile)
                return false;
            if (token.Kind == TokenKind.OpenParenthesis)
                depth++;
            if (token.Kind == TokenKind.CloseParenthesis)
            {
                depth--;
                if (depth == 0)
                    return tokens.PeekAt(offset + 1).Kind == TokenKind.LambdaArrow;
            }
        }
    }

    // Shared by Callable.From lambdas and raw event-handler lambdas.
    private LambdaExpression ParseLambda(List<TypeReference> types)
    {
        tokens.Expect(TokenKind.OpenParenthesis);
        var parameters = new List<ParameterDeclaration>();
        var startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
        while (tokens.Current.Kind != TokenKind.CloseParenthesis)
        {
            // Qualified/generic element types are explicit lambda parameter types too.
            var type = tokens.Peek.Kind is TokenKind.Identifier or TokenKind.Dot or TokenKind.LessThan or TokenKind.OpenBracket
                ? ParseType(allowEventDouble: true)
                : types.ElementAtOrDefault(parameters.Count) ?? new TypeReference("");
            var name = tokens.Expect(TokenKind.Identifier).Text;
            var defaultValue = tokens.TryConsume(TokenKind.Equals) ? ParseExpression() : null;
            parameters.Add(new ParameterDeclaration(name, type, startsOnNewLine, defaultValue));
            if (!tokens.TryConsume(TokenKind.Comma))
                break;
            startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
        }
        var closingOnNewLine = tokens.TryConsume(TokenKind.NewLine);
        tokens.Expect(TokenKind.CloseParenthesis);
        tokens.Expect(TokenKind.LambdaArrow);
        var returnType = types.Count == parameters.Count + 1 ? types[^1] : new TypeReference("void");
        var isInline = tokens.Current.Kind is not (TokenKind.NewLine or TokenKind.OpenBrace);
        List<SyntaxElement> elements;
        if (isInline)
        {
            var value = ParseExpression();
            elements = new List<SyntaxElement>
            {
                returnType.Kind == TypeKind.Void ? new ExpressionStatement(value) : new ReturnStatement(value)
            };
        }
        else
        {
            tokens.TryConsume(TokenKind.NewLine);
            tokens.Expect(TokenKind.OpenBrace);
            EndOfLine();
            elements = ParseBody();
            tokens.Expect(TokenKind.CloseBrace);
        }
        return new LambdaExpression(new ParameterList(parameters, closingOnNewLine), returnType, elements, isInline);
    }

    // Converts C# cast syntax into the same node as GDScript's float(value) conversion.
    private ConversionExpression ParseConversion()
    {
        tokens.Expect(TokenKind.OpenParenthesis);
        var type = ParseType();
        tokens.Expect(TokenKind.CloseParenthesis);
        var value = ParseUnaryExpression();
        if (value is ParenthesizedExpression parenthesized)
            value = parenthesized.Expression;
        return new ConversionExpression(type, value);
    }

    // Splits C# interpolation syntax into language-neutral text and value parts.
    private InterpolatedStringExpression ParseInterpolatedString(string source)
    {
        var parts = new List<InterpolationPart>();
        var text = new StringBuilder();
        var content = source[2..^1];
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] == '{' && index + 1 < content.Length && content[index + 1] == '{')
            {
                text.Append('{');
                index++;
                continue;
            }
            if (content[index] == '}' && index + 1 < content.Length && content[index + 1] == '}')
            {
                text.Append('}');
                index++;
                continue;
            }
            if (content[index] != '{')
            {
                text.Append(content[index]);
                continue;
            }

            AddInterpolationText(parts, text);
            var end = content.IndexOf('}', index + 1);
            if (end < 0)
                throw new NotSupportedException("Unsupported language dialect.");
            parts.Add(ParseInterpolationValue(content[(index + 1)..end]));
            index = end;
        }
        AddInterpolationText(parts, text);
        return new InterpolatedStringExpression(parts);
    }

    // Temporarily tokenizes an embedded value so normal expression parsing remains shared.
    private InterpolationValue ParseInterpolationValue(string source)
    {
        var outerTokens = tokens;
        try
        {
            tokens = new TokenReader(new Lexer(source, false, cSharpIdentifiers: true));
            var value = ParseExpression();
            var format = tokens.TryConsume(TokenKind.Colon)
                ? tokens.Expect(TokenKind.Identifier).Text
                : null;
            tokens.Expect(TokenKind.EndOfFile);
            return new InterpolationValue(value, format);
        }
        finally
        {
            tokens = outerTokens;
        }
    }

    private static void AddInterpolationText(List<InterpolationPart> parts, StringBuilder text)
    {
        if (text.Length == 0)
            return;
        parts.Add(new InterpolationText(text.ToString()));
        text.Clear();
    }

    // Records each line transition independently, including the closing parenthesis.
    private ArgumentList ParseArguments()
    {
        tokens.Expect(TokenKind.OpenParenthesis);
        var arguments = new List<Argument>();
        var startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
        if (tokens.TryConsume(TokenKind.CloseParenthesis))
            return new ArgumentList(arguments, startsOnNewLine);
        while (true)
        {
            arguments.Add(new Argument(ParseExpression(), startsOnNewLine));
            if (!tokens.TryConsume(TokenKind.Comma))
            {
                var closingOnNewLine = tokens.TryConsume(TokenKind.NewLine);
                tokens.Expect(TokenKind.CloseParenthesis);
                return new ArgumentList(arguments, closingOnNewLine);
            }
            startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
            if (tokens.TryConsume(TokenKind.CloseParenthesis))
                return new ArgumentList(arguments, startsOnNewLine);
        }
    }

    private static int OperatorPrecedence(Token token) =>
        ExpressionOperators.Precedence(NormalizeOperator(token.Text), ScriptLanguage.CSharp);

    private static string NormalizeOperator(string value) => value switch
    {
        "&&" => "and",
        "||" => "or",
        _ => value
    };

    // Collapses the C# defaults emitted for uninitialized typed GDScript locals back to one model shape.
    private static bool IsGeneratedDefault(TypeReference type, Expression expression) => expression switch
    {
        ObjectCreationExpression when IsContainerDefault(type, expression) => true,
        ValueExpression { Text: "null" } when type.Kind is TypeKind.UserObject or TypeKind.GodotObject => true,
        ValueExpression { Text: "false" } when type.Kind == TypeKind.Bool => true,
        ValueExpression { Text: "0" } when type.Kind is TypeKind.Int32 or TypeKind.Int64 => true,
        ValueExpression { Text: "0.0" } when type.Kind is TypeKind.Float32 or TypeKind.Float64 => true,
        MemberAccessExpression { Target: ValueExpression { Text: "string" }, Member: "Empty" } when type.Kind == TypeKind.String => true,
        ValueExpression { Text: "default" } when type.Kind is TypeKind.StringName or TypeKind.NodePath or TypeKind.Callable => true,
        _ => false
    };

    private static bool IsContainerDefault(TypeReference type, Expression expression) =>
        type.Kind is TypeKind.Array or TypeKind.Dictionary
        && expression is ObjectCreationExpression creation
        && creation.Arguments.Arguments.Count == 0
        && SameType(type, creation.Type);

    // TypeReference contains lists, so record equality cannot compare independently parsed generic types.
    private static bool SameType(TypeReference left, TypeReference right) =>
        left.Name == right.Name && left.TypeArguments.Count == right.TypeArguments.Count
        && left.TypeArguments.Zip(right.TypeArguments).All(pair => SameType(pair.First, pair.Second));

    private Token ConsumeValue()
    {
        var value = tokens.Current;
        tokens.Expect(value.Kind);
        return value;
    }

    private bool ParseTrivia(List<SyntaxElement> elements)
    {
        if (tokens.TryConsume(TokenKind.NewLine))
        {
            elements.Add(new BlankLineElement());
            return true;
        }
        if (tokens.Current.Kind != TokenKind.Comment)
            return false;
        elements.Add(new CommentElement(tokens.Expect(TokenKind.Comment).Text));
        EndOfLine(optional: true);
        return true;
    }

    private void ParseUsing(string name)
    {
        tokens.Expect(TokenKind.Identifier, "using");
        var parts = name.Split('.');
        for (var index = 0; index < parts.Length; index++)
        {
            if (index > 0)
                tokens.Expect(TokenKind.Dot);
            tokens.Expect(TokenKind.Identifier, parts[index]);
        }
        tokens.Expect(TokenKind.Semicolon);
        EndOfLine();
    }

    private void EndOfLine(bool optional = false)
    {
        if (tokens.TryConsume(TokenKind.NewLine))
            return;
        if (!optional && tokens.Current.Kind != TokenKind.EndOfFile)
            tokens.Expect(TokenKind.NewLine);
    }
}
