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

    // Fetches arbitrary lookahead without advancing the parser's current token.
    public Token PeekAt(int offset)
    {
        while (buffered.Count <= offset)
            buffered.Add(lexer.Next());
        return buffered[offset];
    }

    // Looks beyond blank lines to decide whether an operator continues the same expression.
    public Token PeekAfterNewLines()
    {
        var index = 0;
        while (PeekAt(index).Kind == TokenKind.NewLine)
            index++;
        return PeekAt(index);
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

    // Both languages share assignment tokens; event meaning is resolved later from the target type.
    public bool TryConsumeAssignment(out string operation)
    {
        operation = Current.Text;
        if (Current.Kind is not (TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals
            or TokenKind.StarEquals or TokenKind.SlashEquals))
            return false;
        Advance();
        return true;
    }

    // Retains tokens already fetched while looking across continuation lines.
    private void Advance()
    {
        buffered.RemoveAt(0);
        if (buffered.Count < 2)
            buffered.Add(lexer.Next());
    }
}
