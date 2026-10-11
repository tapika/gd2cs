namespace gd2cs.Parsing;

internal sealed class TokenReader
{
    // Buffers lookahead so inspecting continuation lines does not consume their layout.
    private readonly Lexer lexer;
    private readonly List<Token> buffered = new();

    public TokenReader(Lexer lexer)
    {
        this.lexer = lexer;
        buffered.Add(lexer.Next());
        buffered.Add(lexer.Next());
    }

    public Token Current => buffered[0];
    public Token Peek => buffered[1];

    // Looks beyond blank lines to decide whether an operator continues the same expression.
    public Token PeekAfterNewLines()
    {
        var index = 0;
        while (buffered[index].Kind == TokenKind.NewLine)
        {
            index++;
            if (index == buffered.Count)
                buffered.Add(lexer.Next());
        }
        return buffered[index];
    }

    public int ConsumeLineBreaks()
    {
        var count = 0;
        while (TryConsume(TokenKind.NewLine))
            count++;
        return count;
    }

    // Consumes the required token or reports its precise source position.
    public Token Expect(TokenKind kind, string? text = null)
    {
        if (Current.Kind != kind || (text is not null && Current.Text != text))
        {
            var expected = text is null ? kind.ToString() : $"'{text}'";
            var actual = Current.Kind == TokenKind.EndOfFile ? "end of file" : $"'{Current.Text}'";
            throw new ParseException($"Expected {expected}, found {actual}", Current.Line, Current.Column);
        }

        var result = Current;
        Advance();
        return result;
    }

    // Consumes optional grammar only when both requested kind and text match.
    public bool TryConsume(TokenKind kind, string? text = null)
    {
        if (Current.Kind != kind || text is not null && Current.Text != text)
            return false;
        Advance();
        return true;
    }

    public void Skip(TokenKind kind)
    {
        while (Current.Kind == kind)
            Advance();
    }

    // Retains tokens already fetched while looking across continuation lines.
    private void Advance()
    {
        buffered.RemoveAt(0);
        if (buffered.Count < 2)
            buffered.Add(lexer.Next());
    }
}
