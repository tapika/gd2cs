namespace gd2cs.Parsing;

internal enum TokenKind
{
    Identifier,
    Number,
    String,
    InterpolatedString,
    Comment,
    NewLine,
    Dot,
    Question,
    Colon,
    Equals,
    EqualEqual,
    Bang,
    BangEqual,
    Semicolon,
    OpenBracket,
    CloseBracket,
    OpenBrace,
    CloseBrace,
    OpenParenthesis,
    CloseParenthesis,
    Comma,
    Tilde,
    Arrow,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    AmpersandAmpersand,
    PipePipe,
    EndOfFile
}

internal readonly record struct Token(TokenKind Kind, string Text, int Line, int Column);
