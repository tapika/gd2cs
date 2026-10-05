using System.Text;
using gd2cs.Language;
using gd2cs.Model;

namespace gd2cs.Parsing;

public sealed class CSharpParser : IScriptParser
{
    private TokenReader tokens = null!;

    public ScriptModel Parse(string source)
    {
        tokens = new TokenReader(new Lexer(source, true));
        ParseUsing("Godot");
        ParseUsing("System");
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
        tokens.Expect(TokenKind.Identifier, "public");
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
            if (tokens.Current.Kind == TokenKind.NewLine && tokens.Peek.Text == "private")
            {
                tokens.TryConsume(TokenKind.NewLine);
                continue;
            }
            if (tokens.Current.Text == "private")
            {
                ParseGdArrayHelper();
                continue;
            }
            if (ParseTrivia(elements))
                continue;
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

            tokens.TryConsume(TokenKind.Identifier, "public");
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
                elements.Add(new ConstantDeclaration(memberName, type, field.Initializer, false));
                continue;
            }
            elements.Add(tokens.Current.Kind == TokenKind.OpenParenthesis
                ? ParseMethod(type, memberName, isStatic, isOverride)
                : ParseScalarAfterName(type, memberName));
        }

        tokens.Expect(TokenKind.CloseBrace);
        EndOfLine(optional: true);
        return new ClassDeclaration(name, baseType, isGlobal, elements);
    }

    private static NestedClassDeclaration ToNested(ClassDeclaration declaration) =>
        new(declaration.Name, declaration.BaseType, declaration.Elements);

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
        Expression value = tokens.Current.Text == "gdArray"
            ? ParseArray()
            : ParseExpression();
        tokens.Expect(TokenKind.Semicolon);
        EndOfLine();
        // Initialized fields can use GDScript's inferred declaration syntax, including arrays.
        return new FieldDeclaration(name, type, value, true);
    }

    private ConstructorDeclaration ParseCallable()
    {
        tokens.Expect(TokenKind.OpenParenthesis);
        var parameters = new List<ParameterDeclaration>();
        var startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
        if (!tokens.TryConsume(TokenKind.CloseParenthesis))
        {
            while (true)
            {
                var type = ParseType();
                var name = tokens.Expect(TokenKind.Identifier).Text;
                var defaultValue = tokens.TryConsume(TokenKind.Equals) ? ParseExpression() : null;
                parameters.Add(new ParameterDeclaration(name, type, startsOnNewLine, defaultValue));
                if (!tokens.TryConsume(TokenKind.Comma))
                {
                    var closingOnNewLine = tokens.TryConsume(TokenKind.NewLine);
                    tokens.Expect(TokenKind.CloseParenthesis);
                    return new ConstructorDeclaration(new ParameterList(parameters, closingOnNewLine), ParseBracedBody());
                }
                startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
                if (tokens.TryConsume(TokenKind.CloseParenthesis))
                    break;
            }
        }
        var elements = ParseBracedBody();
        return new ConstructorDeclaration(new ParameterList(parameters, startsOnNewLine), elements);
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
            if (tokens.TryConsume(TokenKind.Identifier, "return"))
            {
                var returnValue = tokens.Current.Kind == TokenKind.Semicolon ? null : ParseExpression();
                tokens.Expect(TokenKind.Semicolon);
                EndOfLine();
                elements.Add(new ReturnStatement(returnValue));
                continue;
            }
            if (tokens.Current.Kind == TokenKind.Identifier && tokens.Peek.Kind == TokenKind.Identifier)
            {
                // C# var carries the same language-neutral inference state as GDScript :=.
                var isInferred = tokens.TryConsume(TokenKind.Identifier, "var");
                var type = isInferred ? null : ParseType();
                var name = tokens.Expect(TokenKind.Identifier).Text;
                var hasInitializer = tokens.TryConsume(TokenKind.Equals);
                if (isInferred && !hasInitializer)
                    throw new ParseException("An inferred local requires a value", tokens.Current.Line, tokens.Current.Column);
                var initializer = hasInitializer
                    ? tokens.Current.Text == "gdArray" ? ParseArray() : ParseExpression()
                    : null;
                if (!isInferred && initializer is not null && IsGeneratedDefault(type!, initializer))
                    initializer = null;
                tokens.Expect(TokenKind.Semicolon);
                EndOfLine();
                elements.Add(new LocalVariableStatement(
                    name,
                    type,
                    initializer,
                    isInferred ? LocalTyping.Inferred : LocalTyping.Explicit));
                continue;
            }
            var expression = ParseExpression();
            if (tokens.TryConsume(TokenKind.Equals))
            {
                var value = ParseExpression();
                tokens.Expect(TokenKind.Semicolon);
                EndOfLine();
                elements.Add(new AssignmentStatement(expression, value));
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

    private ForEachStatement ParseForEach()
    {
        tokens.Expect(TokenKind.Identifier, "foreach");
        tokens.Expect(TokenKind.OpenParenthesis);
        tokens.Expect(TokenKind.Identifier, "var");
        var variable = tokens.Expect(TokenKind.Identifier).Text;
        tokens.Expect(TokenKind.Identifier, "in");
        var collection = ParseExpression();
        tokens.Expect(TokenKind.CloseParenthesis);
        return new ForEachStatement(variable, collection, ParseBracedBody());
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
        var valueToken = tokens.Current;
        tokens.Expect(valueToken.Kind);
        tokens.Expect(TokenKind.Semicolon);
        EndOfLine();
        var value = valueToken.Text.TrimEnd('f', 'F');
        return isConstant
            ? new ConstantDeclaration(name, type, new ValueExpression(value), true)
            : new FieldDeclaration(name, type, new ValueExpression(value), true);
    }

    private TypeReference ParseType()
    {
        var name = tokens.Expect(TokenKind.Identifier).Text;
        while (tokens.TryConsume(TokenKind.Dot))
        {
            name += "." + tokens.Expect(TokenKind.Identifier).Text;
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
        return new TypeReference(name == "String" ? "string" : name, arguments);
    }

    // Consumes the generated gdArray helper without adding it to the script model.
    private void ParseGdArrayHelper()
    {
        tokens.Expect(TokenKind.Identifier, "private");
        tokens.Expect(TokenKind.Identifier, "static");
        _ = ParseType();
        tokens.Expect(TokenKind.Identifier, "gdArray");
        tokens.Expect(TokenKind.LessThan);
        tokens.Expect(TokenKind.OpenBracket);
        tokens.Expect(TokenKind.Identifier, "MustBeVariant");
        tokens.Expect(TokenKind.CloseBracket);
        tokens.Expect(TokenKind.Identifier, "T");
        tokens.Expect(TokenKind.GreaterThan);
        tokens.Expect(TokenKind.OpenParenthesis);
        tokens.Expect(TokenKind.Identifier, "params");
        tokens.Expect(TokenKind.Identifier, "T");
        tokens.Expect(TokenKind.OpenBracket);
        tokens.Expect(TokenKind.CloseBracket);
        tokens.Expect(TokenKind.Identifier, "values");
        tokens.Expect(TokenKind.CloseParenthesis);
        EndOfLine();
        tokens.Expect(TokenKind.OpenBrace);
        var depth = 1;
        while (depth > 0)
        {
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
        tokens.Expect(TokenKind.Identifier, "gdArray");
        tokens.Expect(TokenKind.OpenParenthesis);
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

    // Normalizes C# operators while building precedence-aware expression trees.
    private Expression ParseExpression(int minimumPrecedence = 0)
    {
        var left = ParseUnaryExpression();
        while (true)
        {
            // C# places as casts at the same precedence as relational operators.
            if (minimumPrecedence <= 3 && tokens.TryConsume(TokenKind.Identifier, "as"))
            {
                left = new SafeCastExpression(ParseType(), left);
                continue;
            }
            if (minimumPrecedence <= 2 && tokens.TryConsume(TokenKind.Identifier, "is"))
            {
                var isNotNull = tokens.TryConsume(TokenKind.Identifier, "not");
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
            var right = ParseExpression(precedence + 1);
            left = new BinaryExpression(left, NormalizeOperator(operatorToken.Text), right);
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
        if (tokens.TryConsume(TokenKind.Minus))
            return new UnaryExpression("-", ParseUnaryExpression());
        if (tokens.TryConsume(TokenKind.Plus))
            return new UnaryExpression("+", ParseUnaryExpression());
        return ParsePostfixExpression();
    }

    // Applies member access, indexing, and calls repeatedly to the same base expression.
    private Expression ParsePostfixExpression()
    {
        var expression = ParsePrimaryExpression();
        while (true)
        {
            if (tokens.TryConsume(TokenKind.Dot))
            {
                var member = tokens.Expect(TokenKind.Identifier).Text;
                expression = new MemberAccessExpression(expression, member);
                continue;
            }
            if (tokens.TryConsume(TokenKind.OpenBracket))
            {
                var index = ParseExpression();
                tokens.Expect(TokenKind.CloseBracket);
                expression = new IndexExpression(expression, index);
                continue;
            }
            if (tokens.Current.Kind != TokenKind.OpenParenthesis)
                return expression;
            var arguments = ParseArguments();
            expression = new InvocationExpression(NormalizeCallableTarget(expression, arguments), arguments);
        }
    }

    private Expression ParsePrimaryExpression()
    {
        if (tokens.Current.Text == "Callable" && tokens.Peek.Kind == TokenKind.Dot)
            return ParseLambda();
        if (tokens.Current.Kind == TokenKind.InterpolatedString)
            return ParseInterpolatedString(tokens.Expect(TokenKind.InterpolatedString).Text);
        if (tokens.Current.Kind == TokenKind.OpenParenthesis && tokens.Peek.Text == "float")
            return ParseConversion();
        if (tokens.TryConsume(TokenKind.OpenParenthesis))
        {
            var grouped = ParseExpression();
            tokens.Expect(TokenKind.CloseParenthesis);
            return new ParenthesizedExpression(grouped);
        }
        if (tokens.Current.Text == "new")
        {
            tokens.Expect(TokenKind.Identifier, "new");
            var type = ParseType();
            var arguments = ParseArguments();
            return new ObjectCreationExpression(type, arguments);
        }

        var value = ConsumeValue();
        if (value.Kind != TokenKind.Identifier)
            return new ValueExpression(value.Text.TrimEnd('f', 'F'));
        if (value.Text == "this")
            return new CurrentInstanceExpression();
        return new ValueExpression(value.Text);
    }

    // Callable.From generic arguments supply parameter/result types for an untyped C# lambda.
    private LambdaExpression ParseLambda()
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
        tokens.Expect(TokenKind.OpenParenthesis);
        var parameters = new List<ParameterDeclaration>();
        var startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
        while (tokens.Current.Kind != TokenKind.CloseParenthesis)
        {
            var type = tokens.Peek.Kind == TokenKind.Identifier
                ? ParseType()
                : types.ElementAtOrDefault(parameters.Count)
                    ?? throw new ParseException("Lambda parameter requires a type", tokens.Current.Line, tokens.Current.Column);
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
        tokens.Expect(TokenKind.CloseParenthesis);
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
            tokens = new TokenReader(new Lexer(source, false));
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

    // Math functions use one canonical GDScript name while all other callable targets stay structural.
    private static Expression NormalizeCallableTarget(Expression expression, ArgumentList arguments)
    {
        if (expression is not (ValueExpression or MemberAccessExpression { Target: ValueExpression }))
            return expression;
        var name = ExpressionName(expression);
        var normalized = MathFunctionNames.ToGdScript(name, arguments.Arguments);
        return normalized == name ? expression : new ValueExpression(normalized);
    }

    // Flattens a target only for lookup in the centralized global-function mapping.
    private static string ExpressionName(Expression expression) => expression switch
    {
        ValueExpression value => value.Text,
        CurrentInstanceExpression => "this",
        MemberAccessExpression member => ExpressionName(member.Target) + "." + member.Member,
        _ => throw new NotSupportedException("Unsupported language dialect.")
    };

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

    private static int OperatorPrecedence(Token token) => token.Kind switch
    {
        TokenKind.PipePipe => 0,
        TokenKind.AmpersandAmpersand => 1,
        TokenKind.EqualEqual or TokenKind.BangEqual => 2,
        TokenKind.LessThan or TokenKind.LessThanOrEqual or TokenKind.GreaterThan or TokenKind.GreaterThanOrEqual => 3,
        TokenKind.Plus or TokenKind.Minus => 4,
        TokenKind.Star or TokenKind.Slash or TokenKind.Percent => 5,
        _ => -1
    };

    private static string NormalizeOperator(string value) => value switch
    {
        "&&" => "and",
        "||" => "or",
        _ => value
    };

    // Collapses the C# defaults emitted for uninitialized typed GDScript locals back to one model shape.
    private static bool IsGeneratedDefault(TypeReference type, Expression expression) => expression switch
    {
        ValueExpression { Text: "null" } when type.Kind is TypeKind.UserObject or TypeKind.GodotObject or TypeKind.Array or TypeKind.Dictionary => true,
        ValueExpression { Text: "false" } when type.Kind == TypeKind.Bool => true,
        ValueExpression { Text: "0" } when type.Kind is TypeKind.Int32 or TypeKind.Int64 => true,
        ValueExpression { Text: "0.0" } when type.Kind is TypeKind.Float32 or TypeKind.Float64 => true,
        MemberAccessExpression { Target: ValueExpression { Text: "string" }, Member: "Empty" } when type.Kind == TypeKind.String => true,
        ValueExpression { Text: "default" } when type.Kind is TypeKind.StringName or TypeKind.NodePath or TypeKind.Callable => true,
        _ => false
    };

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
        tokens.Expect(TokenKind.Identifier, name);
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
