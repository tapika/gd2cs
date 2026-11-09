using System.Collections;
using gd2cs.Model;
using gd2cs.Parsing;

namespace gd2cs.Language;

/* C# for conditions are live: range(end) would instead snapshot end before the first
   iteration. GD therefore uses `if true: initializer; while condition: body; update`.
   The outer block retains the initializer's scope. A continue must execute the update,
   while break and return must skip it. Inject updates only into branches belonging to
   this loop, stopping at nested loops and callable boundaries.

   The reverse transformation recognizes that exact scoped while shape and checks every
   relevant continue before folding it into CounterLoopStatement. Missing/different
   updates leave ordinary GD control flow unchanged. No source-text matching or reserved
   variable names are needed; equivalent hand-written GD follows the same semantics. */
internal static class ForLoopLowering
{
    public static string CounterName(Statement initializer) => initializer switch
    {
        LocalVariableStatement local => local.Name,
        AssignmentStatement { Target: ValueExpression value, Operator: "=" } => value.Text,
        _ => throw SourceLocations.Error(initializer, "For loops require one counter initializer.")
    };

    public static List<SyntaxElement> ForGdScript(CounterLoopStatement loop)
    {
        var body = RewriteContinues(loop.Elements, loop.Iterator, fold: false)!;
        body.Add(loop.Iterator);
        return [new ConditionalStatement([new ConditionalBranch(new ValueExpression("true"),
            [loop.Initializer, new WhileStatement(loop.Condition, body)])], null)];
    }

    public static bool TryFold(ConditionalStatement statement, out CounterLoopStatement loop)
    {
        loop = null!;
        if (statement.ElseElements is not null || statement.Branches.Count != 1
            || statement.Branches[0] is not { Condition: ValueExpression { Text: "true" }, Elements.Count: 2 } branch
            || branch.Elements[0] is not Statement initializer
            || initializer is not (LocalVariableStatement or AssignmentStatement { Target: ValueExpression, Operator: "=" })
            || branch.Elements[1] is not WhileStatement { Elements.Count: > 0 } repeated
            || repeated.Elements[^1] is not AssignmentStatement { Target: ValueExpression counter, Operator: "+=" or "-=" } iterator
            || counter.Text != CounterName(initializer))
            return false;
        var body = RewriteContinues(repeated.Elements.Take(repeated.Elements.Count - 1).ToList(), iterator, fold: true);
        if (body is null)
            return false;
        loop = SourceLocations.Copy(statement, new CounterLoopStatement(initializer, repeated.Condition, iterator, body));
        if (HasUpdateShadowing(loop))
            return false;
        return true;
    }

    // Moving an update into the body must not bind an implicit field/method reference to
    // a body local. Explicit self/this qualification remains safe; nested-loop locals do not escape.
    public static bool HasUpdateShadowing(CounterLoopStatement loop)
    {
        var locals = new HashSet<string>(StringComparer.Ordinal);
        void Collect(List<SyntaxElement> elements)
        {
            foreach (var element in elements)
            {
                if (element is LocalVariableStatement local)
                    locals.Add(local.Name);
                if (element is ConditionalStatement branch)
                {
                    foreach (var arm in branch.Branches)
                        Collect(arm.Elements);
                    if (branch.ElseElements is not null)
                        Collect(branch.ElseElements);
                }
                if (element is SwitchStatement selection)
                    foreach (var section in selection.Elements.OfType<SwitchSection>())
                        Collect(section.Elements);
            }
        }
        Collect(loop.Elements);
        return BareNames(loop.Iterator).Any(locals.Contains);
    }

    // Only unqualified value roots can be captured; names after a dot retain their receiver.
    private static IEnumerable<string> BareNames(object? node)
    {
        if (node is ValueExpression value)
            yield return value.Text;
        else if (node is IEnumerable elements)
        {
            foreach (var element in elements)
                foreach (var name in BareNames(element))
                    yield return name;
        }
        else if (node is not null && node.GetType().Namespace == typeof(Expression).Namespace)
            foreach (var property in node.GetType().GetProperties())
                foreach (var name in BareNames(property.GetValue(node)))
                    yield return name;
    }

    // Recurse through branches only: a nested loop's continue belongs to that nested loop.
    private static List<SyntaxElement>? RewriteContinues(List<SyntaxElement> elements, AssignmentStatement iterator, bool fold)
    {
        var result = new List<SyntaxElement>();
        foreach (var element in elements)
        {
            if (element is ContinueStatement)
            {
                if (fold)
                {
                    if (result.Count == 0 || !SameSyntax(result[^1], iterator))
                        return null;
                    result.RemoveAt(result.Count - 1);
                }
                else
                    result.Add(iterator);
            }
            // A match does not own continue: it still advances the enclosing counter loop.
            if (element is SwitchStatement selection)
            {
                var sections = new List<SyntaxElement>();
                foreach (var child in selection.Elements)
                {
                    if (child is not SwitchSection section)
                    {
                        sections.Add(child);
                        continue;
                    }
                    var body = RewriteContinues(section.Elements, iterator, fold);
                    if (body is null)
                        return null;
                    sections.Add(section with { Elements = body });
                }
                result.Add(selection with { Elements = sections });
            }
            else if (element is ConditionalStatement conditional)
            {
                var branches = new List<ConditionalBranch>();
                foreach (var branch in conditional.Branches)
                {
                    var body = RewriteContinues(branch.Elements, iterator, fold);
                    if (body is null)
                        return null;
                    branches.Add(branch with { Elements = body });
                }
                var otherwise = conditional.ElseElements is null ? null
                    : RewriteContinues(conditional.ElseElements, iterator, fold);
                if (conditional.ElseElements is not null && otherwise is null)
                    return null;
                result.Add(conditional with { Branches = branches, ElseElements = otherwise });
            }
            else
                result.Add(element);
        }
        return result;
    }

    // Records compare List properties by reference. Fold duplicated update expressions by
    // their full model structure instead, including call arguments and layout metadata.
    private static bool SameSyntax(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.GetType() != right.GetType())
            return false;
        if (left is string || left.GetType().IsValueType)
            return left.Equals(right);
        if (left is IEnumerable first && right is IEnumerable second)
        {
            var a = first.Cast<object?>().ToList();
            var b = second.Cast<object?>().ToList();
            return a.Count == b.Count && a.Zip(b).All(pair => SameSyntax(pair.First, pair.Second));
        }
        return left.GetType().GetProperties().All(property => SameSyntax(property.GetValue(left), property.GetValue(right)));
    }
}
