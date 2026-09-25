namespace gd2cs.Language;

using gd2cs.Model;

internal static class MathFunctionNames
{
    private const string CSharpPrefix = "Mathf.";

    private static readonly Dictionary<string, string> gdToMathf = new(StringComparer.Ordinal)
    {
        ["absf"] = "Abs",
        ["acos"] = "Acos",
        ["acosh"] = "Acosh",
        ["angle_difference"] = "AngleDifference",
        ["asin"] = "Asin",
        ["asinh"] = "Asinh",
        ["atan"] = "Atan",
        ["atan2"] = "Atan2",
        ["atanh"] = "Atanh",
        ["bezier_derivative"] = "BezierDerivative",
        ["bezier_interpolate"] = "BezierInterpolate",
        ["ceilf"] = "Ceil",
        ["ceili"] = "CeilToInt",
        ["clamp"] = "Clamp",
        ["cos"] = "Cos",
        ["cosh"] = "Cosh",
        ["cubic_interpolate"] = "CubicInterpolate",
        ["cubic_interpolate_angle"] = "CubicInterpolateAngle",
        ["db_to_linear"] = "DbToLinear",
        ["deg_to_rad"] = "DegToRad",
        ["ease"] = "Ease",
        ["exp"] = "Exp",
        ["floorf"] = "Floor",
        ["floori"] = "FloorToInt",
        ["fposmod"] = "PosMod",
        ["inverse_lerp"] = "InverseLerp",
        ["is_equal_approx"] = "IsEqualApprox",
        ["is_finite"] = "IsFinite",
        ["is_inf"] = "IsInf",
        ["is_nan"] = "IsNaN",
        ["is_zero_approx"] = "IsZeroApprox",
        ["lerpf"] = "Lerp",
        ["lerp_angle"] = "LerpAngle",
        ["linear_to_db"] = "LinearToDb",
        ["log"] = "Log",
        ["maxf"] = "Max",
        ["maxi"] = "Max",
        ["minf"] = "Min",
        ["mini"] = "Min",
        ["move_toward"] = "MoveToward",
        ["nearest_po2"] = "NearestPo2",
        ["pingpong"] = "PingPong",
        ["pow"] = "Pow",
        ["rad_to_deg"] = "RadToDeg",
        ["remap"] = "Remap",
        ["rotate_toward"] = "RotateToward",
        ["roundf"] = "Round",
        ["roundi"] = "RoundToInt",
        ["signf"] = "Sign",
        ["sin"] = "Sin",
        ["sinh"] = "Sinh",
        ["smoothstep"] = "SmoothStep",
        ["snappedf"] = "Snapped",
        ["sqrt"] = "Sqrt",
        ["step_decimals"] = "StepDecimals",
        ["tan"] = "Tan",
        ["tanh"] = "Tanh",
        ["wrapf"] = "Wrap"
    };

    private static readonly Dictionary<string, string> mathfToGd =
        gdToMathf
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

    public static string ToCSharp(string name) =>
        gdToMathf.TryGetValue(name, out var mapped) ? CSharpPrefix + mapped : name;

    public static string ToGdScript(string name)
    {
        if (!name.StartsWith(CSharpPrefix, StringComparison.Ordinal))
            return name;
        var methodName = name[CSharpPrefix.Length..];
        return mathfToGd.TryGetValue(methodName, out var mapped) ? mapped : name;
    }

    // Resolves Mathf overloads whose GDScript names encode the argument value type.
    public static string ToGdScript(string name, List<Argument> arguments)
    {
        var mapped = ToGdScript(name);
        if (mapped is "maxf" or "minf" && arguments.All(argument => IsIntegerExpression(argument.Value)))
            return mapped == "maxf" ? "maxi" : "mini";
        return mapped;
    }

    private static bool IsIntegerExpression(Expression expression) => expression switch
    {
        ValueExpression value => !value.Text.Contains('.') && !value.Text.Contains('f'),
        InvocationExpression { Target: ValueExpression { Text: "ceili" or "floori" or "roundi" } } => true,
        BinaryExpression binary => IsIntegerExpression(binary.Left) && IsIntegerExpression(binary.Right),
        _ => false
    };
}
