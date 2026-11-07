using gd2cs.Language;
using gd2cs.Model;

namespace gd2cs.Parsing;

// Both dialects share named-enum braces, comma-separated entries, and integer initializers.
internal static class EnumParser
{
    public static EnumDeclaration Parse(TokenReader tokens, Func<Expression> parseExpression, ScriptLanguage language)
    {
        var opening = tokens.Expect(TokenKind.Identifier, "enum");
        if (tokens.Current.Kind == TokenKind.OpenBrace)
            throw new ParseException("Anonymous enums are not supported; give the enum a name.", opening.Line, opening.Column);
        var name = tokens.Expect(TokenKind.Identifier).Text;
        if (language == ScriptLanguage.CSharp && tokens.TryConsume(TokenKind.Colon))
            tokens.Expect(TokenKind.Identifier, "int");
        tokens.ConsumeLineBreaks();
        tokens.Expect(TokenKind.OpenBrace);
        var startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
        var multiline = startsOnNewLine;
        var elements = new List<SyntaxElement>();
        var comma = false;
        while (tokens.Current.Kind != TokenKind.CloseBrace)
        {
            if (tokens.TryConsume(TokenKind.NewLine))
            {
                multiline = true;
                startsOnNewLine = true;
                elements.Add(new BlankLineElement());
                continue;
            }
            if (tokens.Current.Kind == TokenKind.Comment)
            {
                multiline = true;
                elements.Add(new CommentElement(tokens.Expect(TokenKind.Comment).Text));
                startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
                continue;
            }
            if (elements.OfType<EnumMemberDeclaration>().Any() && !comma)
                tokens.Expect(TokenKind.CloseBrace);
            var entry = tokens.Expect(TokenKind.Identifier);
            var value = tokens.TryConsume(TokenKind.Equals) ? parseExpression() : null;
            elements.Add(SourceLocations.Record(new EnumMemberDeclaration(entry.Text, value, startsOnNewLine), entry));
            comma = tokens.TryConsume(TokenKind.Comma);
            startsOnNewLine = tokens.TryConsume(TokenKind.NewLine);
            if (startsOnNewLine)
                multiline = true;
        }
        tokens.Expect(TokenKind.CloseBrace);
        tokens.TryConsume(TokenKind.NewLine);
        if (!elements.OfType<EnumMemberDeclaration>().Any())
            throw new ParseException("An enum requires at least one member.", opening.Line, opening.Column);
        return SourceLocations.Record(new EnumDeclaration(name, elements, multiline, comma, startsOnNewLine), opening);
    }
}
