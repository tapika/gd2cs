using System.Collections;
using gd2cs.Model;
using gd2cs.Parsing;

namespace gd2cs.Language;

// Event widths belong to the C# boundary; script parameters and their body keep their original types.
internal static class EventParameterConversions
{
    private const string Suffix = "_";

    // Event binding must remove native double parameters via the reversible float conversion.
    // Check script parameters only, not EventType/reflected metadata: ordinary double remains
    // unsupported. Run after binding so handler declaration order cannot affect acceptance.
    internal static void ValidateParameters(object? node)
    {
        if (node is ParameterDeclaration parameter && parameter.Type.Kind == TypeKind.Float64)
            throw SourceLocations.Error(parameter.Type, "C# double is not supported; use float instead.");
        if (node is IEnumerable list)
            foreach (var item in list)
                ValidateParameters(item);
        else if (node is not null && node.GetType().IsClass && node.GetType().Namespace == typeof(Expression).Namespace)
            foreach (var property in node.GetType().GetProperties())
                ValidateParameters(property.GetValue(node));
    }

    /* Generated conversion locals are a reversible dialect detail, not script statements.
       Fold only leading var/cast pairs whose incoming parameter has our generated name,
       matches the reflected event type, and is unused by the remaining body. No general
       cast elimination is attempted. This keeps hand-written locals and casts intact.
       Both named handlers and event lambdas use the same binding/emission transformation. */
    public static (ParameterList Parameters, List<SyntaxElement> Elements, bool Folded) Bind(
        ParameterList parameters, List<SyntaxElement> elements, List<TypeReference> nativeTypes, bool fold)
    {
        var result = parameters.Parameters.ToList();
        var body = elements.ToList();
        var folded = false;
        for (var index = 0; index < result.Count; index++)
        {
            var parameter = result[index];
            if (fold && SameType(parameter.Type, nativeTypes[index]) && body.Count > 0
                && body[0] is LocalVariableStatement { Typing: LocalTyping.Inferred } local
                && local.Initializer is ConversionExpression { Value: ValueExpression value } cast
                && value.Text == parameter.Name && parameter.Name.StartsWith(local.Name + Suffix, StringComparison.Ordinal)
                && !SameType(cast.Type, parameter.Type)
                && (local.Type is null || SameType(local.Type, cast.Type))
                && !Names(body.Skip(1)).Contains(parameter.Name)
                && !result.Any(other => other.Name == local.Name))
            {
                parameter = parameter with { Name = local.Name, Type = cast.Type };
                body.RemoveAt(0);
                folded = true;
            }
            result[index] = parameter with
            {
                EventType = SameType(parameter.Type, nativeTypes[index]) ? null : nativeTypes[index]
            };
        }
        return (parameters with { Parameters = result }, body, folded);
    }

    // Creates a C#-only view without mutating the neutral body or introducing delegate adapters.
    public static (ParameterList Parameters, List<SyntaxElement> Elements) ForCSharp(
        ParameterList parameters, List<SyntaxElement> elements)
    {
        var names = Names(elements);
        foreach (var parameter in parameters.Parameters)
            names.Add(parameter.Name);
        var result = new List<ParameterDeclaration>();
        var locals = new List<SyntaxElement>();
        foreach (var parameter in parameters.Parameters)
        {
            if (parameter.EventType is null)
            {
                result.Add(parameter);
                continue;
            }
            var incoming = parameter.Name + Suffix;
            for (var suffix = 2; !names.Add(incoming); suffix++)
                incoming = parameter.Name + Suffix + suffix;
            result.Add(parameter with { Name = incoming, Type = parameter.EventType, EventType = null });
            locals.Add(new LocalVariableStatement(parameter.Name, parameter.Type,
                new ConversionExpression(parameter.Type, new ValueExpression(incoming)), LocalTyping.Inferred));
        }
        locals.AddRange(elements);
        return (parameters with { Parameters = result }, locals);
    }

    // Conservatively reserve all spellings in model records, including nested lambda/local names.
    // Traversing only model data avoids source-text matching and automatically covers new node kinds.
    internal static HashSet<string> Names(object? node)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        void Visit(object? value)
        {
            if (value is string name)
                names.Add(name);
            else if (value is IEnumerable list)
                foreach (var item in list)
                    Visit(item);
            else if (value is not null && value.GetType().IsClass
                && value.GetType().Namespace == typeof(Expression).Namespace)
                foreach (var property in value.GetType().GetProperties())
                    Visit(property.GetValue(value));
        }
        Visit(node);
        return names;
    }

    private static bool SameType(TypeReference left, TypeReference right) =>
        left.Name == right.Name && left.TypeArguments.Count == right.TypeArguments.Count
        && left.TypeArguments.Zip(right.TypeArguments).All(pair => SameType(pair.First, pair.Second));
}
