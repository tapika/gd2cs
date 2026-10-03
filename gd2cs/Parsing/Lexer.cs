namespace gd2cs.Parsing;

internal sealed class Lexer
{
    private readonly string source;
    private readonly bool emitNewLines;
    private int position;
    private int line = 1;
    private int column = 1;

    public Lexer(string source, bool emitNewLines)
    {
        this.source = source;
        this.emitNewLines = emitNewLines;
    }

    // Scans the next token while optionally retaining newlines for layout-aware parsing.
    public Token Next()
    {
        while (position < source.Length)
        {
            var character = source[position];
            if (character is ' ' or '\t')
            {
                Advance();
                continue;
            }

            if (character is '\r' or '\n')
            {
                var tokenLine = line;
                var tokenColumn = column;
                ConsumeNewLine();
                if (emitNewLines)
                    return new Token(TokenKind.NewLine, "\n", tokenLine, tokenColumn);
                continue;
            }

            if (character == '#' || (character == '/' && position + 1 < source.Length && source[position + 1] == '/'))
            {
                var tokenLine = line;
                var tokenColumn = column;
                var markerLength = character == '#' ? 1 : 2;
                for (var marker = 0; marker < markerLength; marker++)
                    Advance();
                var start = position;
                while (position < source.Length && source[position] is not '\r' and not '\n')
                    Advance();
                return new Token(TokenKind.Comment, source[start..position].TrimStart(), tokenLine, tokenColumn);
            }

            if (character == '$' && position + 1 < source.Length && source[position + 1] == '"')
                return ReadInterpolatedString();

            if (IsIdentifierStart(character))
                return ReadIdentifier();

            if (char.IsDigit(character))
                return ReadNumber();

            if (character == '"')
                return ReadString();

            // Match compound operators before their single-character prefixes.
            if (position + 1 < source.Length)
            {
                var pairKind = (character, source[position + 1]) switch
                {
                    ('-', '>') => TokenKind.Arrow,
                    ('=', '>') => TokenKind.LambdaArrow,
                    ('=', '=') => TokenKind.EqualEqual,
                    ('!', '=') => TokenKind.BangEqual,
                    ('<', '=') => TokenKind.LessThanOrEqual,
                    ('>', '=') => TokenKind.GreaterThanOrEqual,
                    ('&', '&') => TokenKind.AmpersandAmpersand,
                    ('|', '|') => TokenKind.PipePipe,
                    _ => (TokenKind?)null
                };
                if (pairKind is not null)
                {
                    var tokenLine = line;
                    var tokenColumn = column;
                    var text = source[position..(position + 2)];
                    Advance();
                    Advance();
                    return new Token(pairKind.Value, text, tokenLine, tokenColumn);
                }
            }

            var kind = character switch
            {
                '.' => TokenKind.Dot,
                '?' => TokenKind.Question,
                ':' => TokenKind.Colon,
                '=' => TokenKind.Equals,
                '!' => TokenKind.Bang,
                ';' => TokenKind.Semicolon,
                '[' => TokenKind.OpenBracket,
                ']' => TokenKind.CloseBracket,
                '{' => TokenKind.OpenBrace,
                '}' => TokenKind.CloseBrace,
                '(' => TokenKind.OpenParenthesis,
                ')' => TokenKind.CloseParenthesis,
                ',' => TokenKind.Comma,
                '~' => TokenKind.Tilde,
                '+' => TokenKind.Plus,
                '-' => TokenKind.Minus,
                '*' => TokenKind.Star,
                '/' => TokenKind.Slash,
                '%' => TokenKind.Percent,
                '<' => TokenKind.LessThan,
                '>' => TokenKind.GreaterThan,
                _ => throw new ParseException($"Unsupported character '{character}'", line, column)
            };
            var result = new Token(kind, character.ToString(), line, column);
            Advance();
            return result;
        }

        return new Token(TokenKind.EndOfFile, string.Empty, line, column);
    }

    private Token ReadIdentifier()
    {
        var start = position;
        var tokenLine = line;
        var tokenColumn = column;
        Advance();
        while (position < source.Length && IsIdentifierPart(source[position]))
            Advance();
        return new Token(TokenKind.Identifier, source[start..position], tokenLine, tokenColumn);
    }

    // Reads the supported decimal form and preserves an optional C# float suffix.
    private Token ReadNumber()
    {
        var start = position;
        var tokenLine = line;
        var tokenColumn = column;
        while (position < source.Length && (char.IsDigit(source[position]) || source[position] == '.'))
            Advance();
        if (position < source.Length && (source[position] is 'f' or 'F'))
            Advance();
        return new Token(TokenKind.Number, source[start..position], tokenLine, tokenColumn);
    }

    private Token ReadString()
    {
        var start = position;
        var tokenLine = line;
        var tokenColumn = column;
        Advance();
        while (position < source.Length)
        {
            if (source[position] == '\\' && position + 1 < source.Length)
            {
                Advance();
                Advance();
                continue;
            }
            if (source[position] == '"')
            {
                Advance();
                return new Token(TokenKind.String, source[start..position], tokenLine, tokenColumn);
            }
            Advance();
        }
        throw new ParseException("Unterminated string literal", tokenLine, tokenColumn);
    }

    // Keeps the full C# interpolation token while tracking braces until its closing quote.
    private Token ReadInterpolatedString()
    {
        var start = position;
        var tokenLine = line;
        var tokenColumn = column;
        var braceDepth = 0;
        Advance();
        Advance();
        while (position < source.Length)
        {
            if (source[position] == '\\' && position + 1 < source.Length)
            {
                Advance();
                Advance();
                continue;
            }
            if (source[position] == '{')
            {
                if (position + 1 < source.Length && source[position + 1] == '{')
                {
                    Advance();
                    Advance();
                    continue;
                }
                braceDepth++;
            }
            else if (source[position] == '}' && braceDepth > 0)
            {
                braceDepth--;
            }
            else if (source[position] == '"' && braceDepth == 0)
            {
                Advance();
                return new Token(TokenKind.InterpolatedString, source[start..position], tokenLine, tokenColumn);
            }
            Advance();
        }
        throw new ParseException("Unterminated interpolated string", tokenLine, tokenColumn);
    }

    // Normalizes CRLF and LF while resetting source coordinates for the next line.
    private void ConsumeNewLine()
    {
        if (source[position] == '\r')
        {
            position++;
            if (position < source.Length && source[position] == '\n')
                position++;
        }
        else
        {
            position++;
        }

        line++;
        column = 1;
    }

    private void Advance()
    {
        position++;
        column++;
    }

    private static bool IsIdentifierStart(char character) =>
        character is '_' || char.IsLetter(character);

    private static bool IsIdentifierPart(char character) =>
        character is '_' || char.IsLetterOrDigit(character);
}
