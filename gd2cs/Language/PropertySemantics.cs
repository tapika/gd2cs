using System.Collections;
using gd2cs.Model;

namespace gd2cs.Language;

internal static class PropertySemantics
{
    // A custom GD setter name becomes a leading C# alias of its implicit value parameter.
    // Ignore member spellings (self.value is safe), but reserve bare value and local declarations.
    public static bool UsesBareValue(object? node)
    {
        if (node is ValueExpression value)
            return value.Text == "value";
        if (node is MemberAccessExpression member)
            return UsesBareValue(member.Target);
        if (node is LocalVariableStatement { Name: "value" } or ParameterDeclaration { Name: "value" })
            return true;
        if (node is string)
            return false;
        if (node is IEnumerable list)
            return list.Cast<object?>().Any(UsesBareValue);
        return node is not null && node.GetType().IsClass && node.GetType().Namespace == typeof(Expression).Namespace
            && node.GetType().GetProperties().Any(property => UsesBareValue(property.GetValue(node)));
    }
}
