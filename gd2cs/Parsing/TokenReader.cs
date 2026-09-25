namespace gd2cs.Parsing;

internal sealed class TokenReader
{
    // Keeps one-token lookahead available without exposing lexer state to parsers.
    private readonly Lexer lexer;
    private Token current;
    private Token next;

    public TokenReader(Lexer lexer)
    {
        this.lexer = lexer;
        current = lexer.Next();
        next = lexer.Next();
    }

    public Token Current => current;
    public Token Peek => next;

    // Consumes the required token or reports its precise source position.
    public Token Expect(TokenKind kind, string? text = null)
    {
        if (current.Kind != kind || (text is not null && current.Text != text))
        {
            var expected = text is null ? kind.ToString() : $"'{text}'";
            var actual = current.Kind == TokenKind.EndOfFile ? "end of file" : $"'{current.Text}'";
            throw new ParseException($"Expected {expected}, found {actual}", current.Line, current.Column);
        }

        var result = current;
        Advance();
        return result;
    }

    // Consumes optional grammar only when both requested kind and text match.
    public bool TryConsume(TokenKind kind, string? text = null)
    {
        if (current.Kind != kind || text is not null && current.Text != text)
            return false;
        Advance();
        return true;
    }

    public void Skip(TokenKind kind)
    {
        while (current.Kind == kind)
            Advance();
    }

    // Rotates lookahead and requests exactly one replacement token.
    private void Advance()
    {
        current = next;
        next = lexer.Next();
    }
}
