using System.Text;
using gd2cs.Language;
using gd2cs.Model;

namespace gd2cs.Parsing;

public sealed class GdScriptParser : IScriptParser
{
    private TokenReader tokens = null!;
    private int groupingDepth;
    private int expressionOwnerColumn = 1;

    public ScriptModel Parse(string source)
    {
        tokens = new TokenReader(new Lexer(source, true));
        groupingDepth = 0;
        expressionOwnerColumn = 1;
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

    // Uses one grammar path for the global class and indentation-bounded nested classes.
    private ClassDeclaration ParseClass(bool isGlobal)
    {
        tokens.Expect(TokenKind.Identifier, isGlobal ? "class_name" : "class");
        var name = tokens.Expect(TokenKind.Identifier).Text;
        var baseType = "RefCounted";
        if (isGlobal)
        {
            EndOfLine();
            tokens.Expect(TokenKind.Identifier, "extends");
            baseType = ParseType().Name;
        }
        else
            tokens.Expect(TokenKind.Colon);
        EndOfLine();

        if (isGlobal)
            tokens.TryConsume(TokenKind.NewLine);

        var elements = new List<SyntaxElement>();
        while (tokens.Current.Kind != TokenKind.EndOfFile)
        {
            if (!isGlobal && tokens.Current.Kind != TokenKind.NewLine && tokens.Current.Column == 1)
                break;
            if (!isGlobal && tokens.Current.Kind == TokenKind.NewLine && tokens.Peek.Kind != TokenKind.NewLine && tokens.Peek.Column == 1)
                break;
            if (ParseTrivia(elements))
                continue;
            if (tokens.Current.Text == "class")
            {
                elements.Add(ToNested(ParseClass(false)));
                continue;
            }

            var column = tokens.Current.Column;
            expressionOwnerColumn = column;
            var isStatic = tokens.TryConsume(TokenKind.Identifier, "static");
            var kind = tokens.Expect(TokenKind.Identifier).Text;
            if (kind == "signal")
            {
                if (isStatic)
                    throw new ParseException("Signals cannot be static.", tokens.Current.Line, tokens.Current.Column);
                elements.Add(ParseSignal());
                continue;
            }
            if (kind == "func")
            {
                elements.Add(ParseCallable(column, isStatic));
                continue;
            }
            if (kind is not ("const" or "var"))
                throw new ParseException("Expected a class element", tokens.Current.Line, tokens.Current.Column);
            var member = ParseScalar(kind == "const");
            elements.Add(member is FieldDeclaration field
                ? field with { IsStatic = isStatic }
                : member);
            EndOfLine(optional: true);
        }
        return new ClassDeclaration(name, new TypeReference(baseType), isGlobal, elements);
    }

    private static NestedClassDeclaration ToNested(ClassDeclaration declaration) =>
        new(declaration.Name, declaration.BaseType, declaration.Elements);

    // Argumentless signals omit parentheses; typed signals reuse callable parameter parsing.
    private SignalDeclaration ParseSignal()
    {
        var name = tokens.Expect(TokenKind.Identifier).Text;
        var parameters = tokens.TryConsume(TokenKind.OpenParenthesis)
            ? ParseParameters()
            : new ParameterList(new List<ParameterDeclaration>(), false);
        EndOfLine();
        return new SignalDeclaration(name, parameters);
    }

    private CallableDeclaration ParseCallable(int declarationColumn, bool isStatic = false)
    {
        var name = tokens.Expect(TokenKind.Identifier).Text;
        tokens.Expect(TokenKind.OpenParenthesis);
        var parameters = ParseParameters();
        var returnType = new TypeReference("void");
        if (tokens.TryConsume(TokenKind.Arrow))
        {
            returnType = ParseType();
        }
        tokens.Expect(TokenKind.Colon);
        EndOfLine();

        var elements = ParseBody(declarationColumn);
        if (name == "_init")
            return new ConstructorDeclaration(parameters, elements);
        if (name == "_exit_tree")
            return new DestructorDeclaration(elements);
        var kind = name == "_to_string" ? MethodKind.StringConversion : MethodKind.Ordinary;
        var neutralName = kind == MethodKind.StringConversion ? "ToString" : name;
        return new MethodDeclaration(neutralName, returnType, parameters, elements, isStatic, kind);
    }

    // Reads ordered body elements until indentation returns to the owning declaration.
    private List<SyntaxElement> ParseBody(int parentColumn)
    {
        var elements = new List<SyntaxElement>();
        while (tokens.Current.Kind != TokenKind.EndOfFile)
        {
            if (tokens.Current.Kind == TokenKind.NewLine)
            {
                if (tokens.Peek.Kind != TokenKind.NewLine && tokens.Peek.Column <= parentColumn)
                    break;
                tokens.TryConsume(TokenKind.NewLine);
                elements.Add(new BlankLineElement());
                continue;
            }
            if (tokens.Current.Column <= parentColumn)
                break;
            if (tokens.Current.Kind == TokenKind.Comment)
            {
                elements.Add(new CommentElement(tokens.Expect(TokenKind.Comment).Text));
                EndOfLine(optional: true);
                continue;
            }
            var statementToken = tokens.Current;
            var statementColumn = statementToken.Column;
            expressionOwnerColumn = statementColumn;
            if (tokens.Current.Text == "for")
            {
                elements.Add(ParseForEach(statementColumn));
                continue;
            }
            if (tokens.Current.Text == "while")
            {
                elements.Add(ParseWhile(statementColumn));
                continue;
            }
            if (tokens.Current.Text == "if")
            {
                elements.Add(ParseConditional(statementColumn));
                continue;
            }
            if (tokens.TryConsume(TokenKind.Identifier, "break"))
            {
                elements.Add(new BreakStatement());
                EndOfLine(optional: true);
                continue;
            }
            if (tokens.TryConsume(TokenKind.Identifier, "continue"))
            {
                elements.Add(new ContinueStatement());
                EndOfLine(optional: true);
                continue;
            }
            if (tokens.TryConsume(TokenKind.Identifier, "return"))
            {
                var returnValue = tokens.Current.Kind is TokenKind.NewLine or TokenKind.EndOfFile ? null : ParseExpression();
                elements.Add(new ReturnStatement(returnValue));
                EndOfLine(optional: true);
                continue;
            }
            if (tokens.TryConsume(TokenKind.Identifier, "var"))
            {
                var localName = tokens.Expect(TokenKind.Identifier).Text;
                var hasColon = tokens.TryConsume(TokenKind.Colon);
                // := requests inference, while = deliberately leaves the local dynamically typed.
                var typing = LocalTyping.Dynamic;
                if (hasColon)
                    typing = tokens.TryConsume(TokenKind.Equals)
                        ? LocalTyping.Inferred
                        : LocalTyping.Explicit;
                var type = typing == LocalTyping.Explicit ? ParseHintedType() : null;
                var hasInitializer = typing == LocalTyping.Inferred || tokens.TryConsume(TokenKind.Equals);
                var initializer = hasInitializer
                    ? tokens.Current.Text == "func" ? ParseLambda(statementColumn) : ParseExpression()
                    : null;
                elements.Add(SourceLocations.Record(
                    new LocalVariableStatement(localName, type, initializer, typing), statementToken));
                EndOfLine(optional: true);
                continue;
            }
            var assignmentToken = tokens.Current;
            var expression = ParseExpression();
            if (tokens.TryConsumeAssignment(out var operation))
            {
                var value = tokens.Current.Text == "func" ? ParseLambda(statementColumn) : ParseExpression();
                elements.Add(SourceLocations.Record(new AssignmentStatement(expression, value, operation), assignmentToken));
                EndOfLine(optional: true);
                continue;
            }
            if (expression is not InvocationExpression)
                throw new ParseException("Expected a function call", tokens.Current.Line, tokens.Current.Column);
            elements.Add(new ExpressionStatement(expression));
            EndOfLine(optional: true);
        }
        return elements;
    }

    private ForEachStatement ParseForEach(int statementColumn)
    {
        tokens.Expect(TokenKind.Identifier, "for");
        var variable = tokens.Expect(TokenKind.Identifier).Text;
        var type = tokens.TryConsume(TokenKind.Colon) ? ParseType() : null;
        tokens.Expect(TokenKind.Identifier, "in");
        var collection = ParseExpression();
        tokens.Expect(TokenKind.Colon);
        EndOfLine();
        return new ForEachStatement(variable, collection, ParseBody(statementColumn), type);
    }

    private WhileStatement ParseWhile(int statementColumn)
    {
        tokens.Expect(TokenKind.Identifier, "while");
        var condition = ParseExpression();
        tokens.Expect(TokenKind.Colon);
        EndOfLine();
        return new WhileStatement(condition, ParseBody(statementColumn));
    }

    // Groups adjacent elif and else blocks with their originating if statement.
    private ConditionalStatement ParseConditional(int statementColumn)
    {
        var branches = new List<ConditionalBranch>();
        tokens.Expect(TokenKind.Identifier, "if");
        branches.Add(ParseConditionalBranch(statementColumn));
        while (tokens.Current.Column == statementColumn && tokens.TryConsume(TokenKind.Identifier, "elif"))
        {
            branches.Add(ParseConditionalBranch(statementColumn));
        }

        List<SyntaxElement>? elseElements = null;
        if (tokens.Current.Column == statementColumn && tokens.TryConsume(TokenKind.Identifier, "else"))
        {
            tokens.Expect(TokenKind.Colon);
            EndOfLine();
            elseElements = ParseBody(statementColumn);
        }
        return new ConditionalStatement(branches, elseElements);
    }

    private ConditionalBranch ParseConditionalBranch(int statementColumn)
    {
        var condition = ParseExpression();
        tokens.Expect(TokenKind.Colon);
        EndOfLine();
        return new ConditionalBranch(condition, ParseBody(statementColumn));
    }

    private ParameterList ParseParameters()
    {
        var result = new List<ParameterDeclaration>();
        var startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
        if (tokens.TryConsume(TokenKind.CloseParenthesis))
            return new ParameterList(result, startsOnNewLine);
        while (true)
        {
            var name = tokens.Expect(TokenKind.Identifier).Text;
            tokens.Expect(TokenKind.Colon);
            var type = ParseType();
            var defaultValue = tokens.TryConsume(TokenKind.Equals) ? ParseExpression() : null;
            result.Add(new ParameterDeclaration(name, type, startsOnNewLine, defaultValue));
            if (!tokens.TryConsume(TokenKind.Comma))
            {
                var closingOnNewLine = tokens.TryConsume(TokenKind.NewLine);
                tokens.Expect(TokenKind.CloseParenthesis);
                return new ParameterList(result, closingOnNewLine);
            }
            startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
            if (tokens.TryConsume(TokenKind.CloseParenthesis))
                return new ParameterList(result, startsOnNewLine);
        }
    }

    // Field initializers share the expression grammar used in callable bodies, including construction.
    private MemberDeclaration ParseScalar(bool isConstant)
    {
        var name = tokens.Expect(TokenKind.Identifier).Text;
        tokens.Expect(TokenKind.Colon);
        var inferred = tokens.TryConsume(TokenKind.Equals);
        var type = inferred ? new TypeReference("Variant") : ParseHintedType();
        if (!inferred && !tokens.TryConsume(TokenKind.Equals))
        {
            if (isConstant)
                throw new ParseException("A constant requires a value", tokens.Current.Line, tokens.Current.Column);
            return new FieldDeclaration(name, type, null, false);
        }
        var value = ParseExpression();
        if (inferred)
            type = InferExpressionType(value);
        return isConstant
            ? new ConstantDeclaration(name, type, value, inferred)
            : new FieldDeclaration(name, type, value, inferred);
    }

    // Infers homogeneous array declarations so indexed access can be bound to the element type.
    private static TypeReference InferArrayType(ArrayExpression array)
    {
        var elementTypes = array.Elements
            .Select(element => InferExpressionType(element.Value))
            .DistinctBy(type => type.Name)
            .ToList();
        if (elementTypes.Count != 1)
            return new TypeReference("Array", new List<TypeReference> { new("Variant") });
        return new TypeReference("Array", new List<TypeReference> { elementTypes[0] });
    }

    private static TypeReference InferExpressionType(Expression expression) => expression switch
    {
        ValueExpression value when value.Text is "true" or "false" => new TypeReference("bool"),
        ValueExpression value when value.Text.StartsWith('"') => new TypeReference("string"),
        ValueExpression value when value.Text.Contains('.') => new TypeReference("float"),
        ValueExpression => new TypeReference("int"),
        UnaryExpression unary => InferExpressionType(unary.Operand),
        ParenthesizedExpression grouped => InferExpressionType(grouped.Expression),
        ObjectCreationExpression creation => creation.Type,
        ArrayExpression array => InferArrayType(array),
        DictionaryExpression => new TypeReference("Dictionary"),
        _ => new TypeReference("Variant")
    };

    // Trailing hints on declarations and casts retain schemas Godot cannot nest in Array[].
    private TypeReference ParseHintedType()
    {
        var opening = tokens.Current;
        var type = ParseType(true);
        if (type.Kind == TypeKind.Dictionary && type.TypeArguments.Count != 2)
            throw new ParseException("Untyped dictionaries are not supported; specify Dictionary[KeyType, ValueType] or use a class for mixed fields.", opening.Line, opening.Column);
        return type;
    }

    private TypeReference ParseType(bool allowDictionaryHint = false)
    {
        var typeToken = tokens.Current;
        var name = tokens.Expect(TokenKind.Identifier).Text;
        // Keep nested-class qualification before parsing container type arguments.
        while (tokens.TryConsume(TokenKind.Dot))
            name += "." + tokens.Expect(TokenKind.Identifier).Text;
        if (PackedArrayMappings.TryElement(name, out _))
            throw new ParseException(PackedArrayMappings.StorageError(name), typeToken.Line, typeToken.Column);
        var arguments = new List<TypeReference>();
        if (tokens.TryConsume(TokenKind.OpenBracket))
        {
            do
            {
                arguments.Add(ParseType(allowDictionaryHint && name == "Array"));
            }
            while (tokens.TryConsume(TokenKind.Comma));
            tokens.Expect(TokenKind.CloseBracket);
        }
        if (name == "Array" && arguments.Count == 1
            && arguments[0].Kind == TypeKind.Dictionary && arguments[0].TypeArguments.Count == 0)
        {
            if (!allowDictionaryHint || tokens.Current.Kind != TokenKind.Comment
                || !tokens.Current.Text.StartsWith('['))
                throw new ParseException("Untyped dictionaries are not supported; specify Dictionary[KeyType, ValueType] or use a class for mixed fields.", typeToken.Line, typeToken.Column);
            arguments[0] = ParseDictionaryHint();
        }
        if (name == "Dictionary" && arguments.Count != 2 && !(allowDictionaryHint && arguments.Count == 0))
            throw new ParseException("Untyped dictionaries are not supported; specify Dictionary[KeyType, ValueType] or use a class for mixed fields.", typeToken.Line, typeToken.Column);
        return new TypeReference(name == "String" ? "string" : name, arguments);
    }

    // Reuses type grammar rather than splitting comment text, so qualified names remain structural.
    private TypeReference ParseDictionaryHint()
    {
        var comment = tokens.Expect(TokenKind.Comment);
        var parser = new GdScriptParser
        {
            tokens = new TokenReader(new Lexer("Dictionary" + comment.Text, true))
        };
        try
        {
            var type = parser.ParseType();
            parser.tokens.Expect(TokenKind.EndOfFile);
            return type;
        }
        catch (ParseException error)
        {
            // Remove the synthetic Dictionary prefix when reporting the original comment position.
            throw new ParseException("Invalid dictionary type hint: " + error.Message,
                comment.Line, comment.Column + 1 + Math.Max(0, error.Column - 11));
        }
    }

    private ArrayExpression ParseArray()
    {
        var opening = tokens.Expect(TokenKind.OpenBracket);
        // Empty literals carry no element type; require a typed declaration without an initializer.
        if (tokens.PeekAfterNewLines().Kind == TokenKind.CloseBracket)
            throw new ParseException("Empty array literals are not supported; declare an array type without an initializer instead.", opening.Line, opening.Column);
        var startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
        var elements = new List<ArrayElement>();
        while (tokens.Current.Kind != TokenKind.CloseBracket)
        {
            elements.Add(new ArrayElement(ParseExpression(), startsOnNewLine));
            if (tokens.TryConsume(TokenKind.Comma))
            {
                startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
                continue;
            }
            var closingOnNewLine = tokens.TryConsume(TokenKind.NewLine);
            tokens.Expect(TokenKind.CloseBracket);
            return new ArrayExpression(elements, closingOnNewLine);
        }
        tokens.Expect(TokenKind.CloseBracket);
        return new ArrayExpression(elements, startsOnNewLine);
    }

    // Reads dictionary entries without conflating entry separators with call arguments.
    private DictionaryExpression ParseDictionary()
    {
        var opening = tokens.Expect(TokenKind.OpenBrace);
        var lineBreaks = tokens.ConsumeLineBreaks();
        var entries = new List<DictionaryEntry>();
        var trailingComma = false;
        while (tokens.Current.Kind != TokenKind.CloseBrace)
        {
            var key = ParseExpression();
            tokens.Expect(TokenKind.Colon);
            var value = ParseExpression();
            entries.Add(new DictionaryEntry(key, value, lineBreaks));
            trailingComma = tokens.TryConsume(TokenKind.Comma);
            lineBreaks = tokens.ConsumeLineBreaks();
            if (!trailingComma)
                break;
        }
        tokens.Expect(TokenKind.CloseBrace);
        return SourceLocations.Record(new DictionaryExpression(entries, lineBreaks, trailingComma), opening);
    }

    // Builds precedence-aware binary and conditional expressions from the token stream.
    private Expression ParseExpression(int minimumPrecedence = 0)
    {
        var left = ParseUnaryExpression();
        while (true)
        {
            // Leave delimiter/argument newlines for their owner and lower-precedence operators for the outer call.
            var beforeOperator = groupingDepth > 0 && tokens.Current.Kind == TokenKind.NewLine
                && OperatorPrecedence(tokens.PeekAfterNewLines()) >= minimumPrecedence
                    ? tokens.ConsumeLineBreaks() : 0;
            // GDScript as casts bind below binary operators.
            if (minimumPrecedence <= 9 && tokens.TryConsume(TokenKind.Identifier, "is"))
            {
                var isNegated = tokens.TryConsume(TokenKind.Identifier, "not");
                left = new TypeCheckExpression(left, ParseType(), isNegated);
                continue;
            }
            if (minimumPrecedence == 0 && tokens.TryConsume(TokenKind.Identifier, "as"))
            {
                left = new SafeCastExpression(ParseHintedType(), left);
                continue;
            }
            if (OperatorPrecedence(tokens.Current) < minimumPrecedence)
                break;
            var operatorToken = tokens.Current;
            var precedence = OperatorPrecedence(operatorToken);
            tokens.Expect(operatorToken.Kind, operatorToken.Text);
            var afterOperator = groupingDepth > 0 ? tokens.ConsumeLineBreaks() : 0;
            var right = ParseExpression(precedence + 1);
            if (operatorToken.Kind == TokenKind.Percent && left is ValueExpression literal && literal.Text.StartsWith('"'))
            {
                // Scalar and argument-list formatting share one model. Numeric % remains remainder.
                var values = right is ArrayExpression arguments
                    ? arguments.Elements.Select(element => element.Value).ToList()
                    : new List<Expression> { right };
                left = ParseInterpolatedString(literal.Text, values, operatorToken);
            }
            else
                left = new BinaryExpression(left, operatorToken.Text, right, beforeOperator, afterOperator);
        }
        if (minimumPrecedence == 0 && tokens.TryConsume(TokenKind.Identifier, "if"))
        {
            var condition = ParseExpression();
            tokens.Expect(TokenKind.Identifier, "else");
            var whenFalse = ParseExpression();
            return new ConditionalExpression(condition, left, whenFalse);
        }
        return left;
    }

    private Expression ParseUnaryExpression()
    {
        if (tokens.TryConsume(TokenKind.Identifier, "not"))
            return new UnaryExpression("not", ParseExpression(2));
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
            var isScalarConversion = expression is ValueExpression valueExpression
                && valueExpression.Text is "float" or "int"
                && arguments.Arguments.Count == 1;
            if (isScalarConversion)
            {
                expression = new ConversionExpression(
                    new TypeReference(((ValueExpression)expression).Text),
                    arguments.Arguments[0].Value);
            }
            else if (expression is MemberAccessExpression { Member: "new" } creation)
                expression = new ObjectCreationExpression(new TypeReference(ExpressionName(creation.Target)), arguments);
            else
                expression = SourceLocations.Record(new InvocationExpression(expression, arguments), opening);
        }
    }

    private Expression ParsePrimaryExpression()
    {
        // The owning statement's indentation, not the inline func column, bounds a lambda body.
        if (tokens.Current.Text == "func")
        {
            var ownerColumn = expressionOwnerColumn;
            var lambda = ParseLambda(ownerColumn);
            expressionOwnerColumn = ownerColumn;
            return lambda;
        }
        if (tokens.Current.Kind == TokenKind.OpenBrace)
            return ParseDictionary();
        if (tokens.Current.Text == "func")
            return ParseLambda(tokens.Current.Column);
        if (tokens.Current.Kind == TokenKind.OpenBracket)
            return ParseArray();
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
        var value = tokens.Current;
        tokens.Expect(value.Kind);
        if (value.Kind != TokenKind.Identifier)
            return SourceLocations.Record(new ValueExpression(value.Text), value);
        if (value.Text == "self")
            return new CurrentInstanceExpression();
        return SourceLocations.Record(new ValueExpression(value.Text), value);
    }

    // Multiline lambda bodies end at the owning declaration's indentation, not the func column.
    private LambdaExpression ParseLambda(int declarationColumn)
    {
        tokens.Expect(TokenKind.Identifier, "func");
        tokens.Expect(TokenKind.OpenParenthesis);
        var parameters = ParseParameters();
        var returnType = tokens.TryConsume(TokenKind.Arrow) ? ParseType() : new TypeReference("void");
        tokens.Expect(TokenKind.Colon);
        var isInline = !tokens.TryConsume(TokenKind.NewLine);
        List<SyntaxElement> elements;
        if (isInline)
        {
            var isReturn = tokens.TryConsume(TokenKind.Identifier, "return");
            var value = ParseExpression();
            elements = new List<SyntaxElement>
            {
                isReturn ? new ReturnStatement(value) : new ExpressionStatement(value)
            };
        }
        else
            elements = ParseBody(declarationColumn);
        return new LambdaExpression(parameters, returnType, elements, isInline);
    }

    // Splits GDScript percent formatting into language-neutral text and value parts.
    private InterpolatedStringExpression ParseInterpolatedString(string literal, List<Expression> values, Token formattingToken)
    {
        var parts = new List<InterpolationPart>();
        var text = new StringBuilder();
        var valueIndex = 0;
        var content = literal[1..^1];
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] != '%')
            {
                text.Append(content[index]);
                continue;
            }
            if (index + 1 < content.Length && content[index + 1] == '%')
            {
                text.Append('%');
                index++;
                continue;
            }

            AddInterpolationText(parts, text);
            index++;
            string? format = null;
            if (index < content.Length && content[index] == '.')
            {
                var precisionStart = ++index;
                while (index < content.Length && char.IsDigit(content[index]))
                    index++;
                if (precisionStart == index || index >= content.Length || content[index] != 'f')
                    throw new ParseException("Unsupported string format specifier; use %s, %d, %i, %f or %.Nf.",
                        formattingToken.Line, formattingToken.Column);
                format = "F" + content[precisionStart..index];
            }
            else if (index >= content.Length || content[index] is not ('s' or 'd' or 'i' or 'f'))
            {
                throw new ParseException("Unsupported string format specifier; use %s, %d, %i, %f or %.Nf.",
                    formattingToken.Line, formattingToken.Column);
            }
            else if (content[index] == 'f')
            {
                format = "F6";
            }

            if (valueIndex >= values.Count)
                throw new ParseException("String format has more placeholders than arguments.",
                    formattingToken.Line, formattingToken.Column);
            // Integer placeholders normalize to %s because C# interpolation does not retain d versus i.
            parts.Add(new InterpolationValue(values[valueIndex++], format));
        }
        AddInterpolationText(parts, text);
        if (valueIndex != values.Count)
            throw new ParseException("String format has more arguments than placeholders.",
                formattingToken.Line, formattingToken.Column);
        return SourceLocations.Record(new InterpolatedStringExpression(parts), formattingToken);
    }

    private static void AddInterpolationText(List<InterpolationPart> parts, StringBuilder text)
    {
        if (text.Length == 0)
            return;
        parts.Add(new InterpolationText(text.ToString()));
        text.Clear();
    }

    // Object construction needs a type name; ordinary invocation retains the expression tree.
    private static string ExpressionName(Expression expression) => expression switch
    {
        ValueExpression value => value.Text,
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
            var value = ParseExpression();
            arguments.Add(new Argument(value, startsOnNewLine));
            if (!tokens.TryConsume(TokenKind.Comma))
            {
                // A multiline lambda body consumes its final newline before returning here.
                var closingOnNewLine = tokens.TryConsume(TokenKind.NewLine) || value is LambdaExpression { IsInline: false };
                tokens.Expect(TokenKind.CloseParenthesis);
                return new ArgumentList(arguments, closingOnNewLine);
            }
            startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
            if (tokens.TryConsume(TokenKind.CloseParenthesis))
                return new ArgumentList(arguments, startsOnNewLine);
        }
    }

    private static int OperatorPrecedence(Token token) =>
        ExpressionOperators.Precedence(token.Text, ScriptLanguage.GdScript);

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

    private void EndOfLine(bool optional = false)
    {
        if (tokens.TryConsume(TokenKind.NewLine))
            return;
        if (!optional && tokens.Current.Kind != TokenKind.EndOfFile)
            tokens.Expect(TokenKind.NewLine);
    }
}
