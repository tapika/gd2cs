using System.Text;
using gd2cs.Language;
using gd2cs.Model;

namespace gd2cs.Emission;

// Enum entry syntax is shared; only visibility, brace placement, comments, and indentation differ.
internal static class EnumEmitter
{
    public static void Emit(StringBuilder output, EnumDeclaration declaration, string indent,
        ScriptLanguage language, Action<Expression, string> emitExpression)
    {
        var csharp = language == ScriptLanguage.CSharp;
        output.Append(indent);
        output.Append(csharp ? "public enum " : "enum ");
        output.Append(declaration.Name);
        if (declaration.IsMultiline && csharp)
        {
            output.AppendLine();
            output.Append(indent);
            output.Append('{');
        }
        else
            output.Append(" {");
        // Delay entry line breaks until their own flags, retaining partially wrapped declarations.
        var atLineStart = false;
        var entryIndent = indent + (csharp ? "    " : "\t");
        var remaining = declaration.Elements.OfType<EnumMemberDeclaration>().Count();
        foreach (var element in declaration.Elements)
        {
            if (element is BlankLineElement)
            {
                if (!atLineStart)
                    output.AppendLine();
                output.AppendLine();
                atLineStart = true;
                continue;
            }
            if (element is CommentElement comment)
            {
                if (!atLineStart)
                    output.AppendLine();
                output.Append(entryIndent);
                output.Append(csharp ? "// " : "# ");
                output.AppendLine(comment.Text);
                atLineStart = true;
                continue;
            }
            if (element is not EnumMemberDeclaration member)
                throw new NotSupportedException("Unsupported language dialect.");
            if (member.StartsOnNewLine && !atLineStart)
            {
                output.AppendLine();
                atLineStart = true;
            }
            output.Append(atLineStart ? entryIndent : " ");
            output.Append(member.Name);
            if (member.Value is not null)
            {
                output.Append(" = ");
                emitExpression(member.Value, entryIndent);
            }
            if (--remaining > 0 || declaration.HasTrailingComma)
                output.Append(',');
            atLineStart = false;
        }
        if (declaration.ClosingBraceStartsOnNewLine && !atLineStart)
        {
            output.AppendLine();
            atLineStart = true;
        }
        output.Append(atLineStart ? indent : " ");
        output.AppendLine("}");
    }
}
