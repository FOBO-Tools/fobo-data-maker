using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using DataMaker.Expressions.Functions;
using DynamicExpresso;

namespace DataMaker.Expressions.Engine;

/// <summary>
/// Translates a DataMaker DSL expression (DynamicExpresso / C# syntax) into
/// an equivalent JavaScript function string for client-side evaluation.
/// Used by the web renderer to ship a compiled-once form bundle that the
/// browser can evaluate locally on every value change — without the browser
/// needing the C# expression engine, and without us maintaining a parallel
/// JS port of the DSL.
///
/// <para>
/// Output shape is always:
/// <c>function(v) { return &lt;js-expr&gt;; }</c>
/// where <c>v</c> is a plain object whose properties are the form's field
/// values keyed by field name. The browser does
/// <c>new Function('v', 'return ' + body)</c> once and caches the result.
/// </para>
///
/// <para>
/// Supported constructs: literals (string, number, bool, null), field
/// references (parameters), comparisons (==, !=, &lt;, &gt;, &lt;=, &gt;=),
/// logical ops (&amp;&amp;, ||, !), arithmetic (+, -, *, /, %), null-coalesce
/// (??), ternary (?:), implicit casts, and <see cref="Fn"/>-library method
/// calls (<c>Fn.Split</c>, <c>Fn.Concat</c>, …). Method calls translate to
/// <c>Dm.&lt;Name&gt;(args)</c> — the browser-side <c>fn.js</c> runtime
/// provides the implementations with semantics matching the C# library.
/// Unrecognised method calls, type tests, or other unhandled nodes cause
/// <see cref="TryCompile"/> to return null so the bundle can mark the
/// expression "server-only".
/// </para>
/// </summary>
public sealed class JsCompiler
{
    private readonly Interpreter _interpreter;

    public JsCompiler()
    {
        _interpreter = new Interpreter(InterpreterOptions.Default);

        // Register the exact same Fn surface ExpressionEngine uses — Fn.* AND
        // every method as a bare identifier — so any expression that parses for
        // server-side evaluation also parses here. Bare Fn calls (Upper(x)) are
        // emitted to JS by EmitMethodCall/EmitInvocation below.
        ExpressionEngine.RegisterFunctionLibrary(_interpreter);
    }

    /// <summary>
    /// Compile <paramref name="expression"/> to a JS function string. Returns
    /// <c>null</c> if the expression uses constructs not yet portable
    /// (method calls, type tests, conditional ternary, etc.) — caller marks
    /// the expression as <c>serverOnly</c> in the form bundle and falls back
    /// to a server round-trip (or "show all" in Preview).
    /// </summary>
    public string? TryCompile(string expression, params Parameter[] parameters)
    {
        try
        {
            var lambda = _interpreter.Parse(expression, parameters);
            // Lambda.Expression is the body of the parsed lambda
            // (System.Linq.Expressions.Expression). Walk it directly.
            var body = Emit(lambda.Expression);
            return $"function(v){{return {body};}}";
        }
        catch (NotPortableException)
        {
            return null;
        }
        catch
        {
            // Parse failure or other DynamicExpresso error — surface as
            // unportable rather than crashing form compilation. The C# side
            // will hit the same parse failure when it tries to evaluate
            // server-side and will report it through the normal error path.
            return null;
        }
    }

    // ── Tree walker ──────────────────────────────────────────────────

    private string Emit(Expression expr) => expr switch
    {
        ConstantExpression c        => EmitConstant(c),
        ParameterExpression p       => EmitParameter(p),
        UnaryExpression u           => EmitUnary(u),
        BinaryExpression b          => EmitBinary(b),
        ConditionalExpression cond  => EmitConditional(cond),
        MethodCallExpression call   => EmitMethodCall(call),
        InvocationExpression inv    => EmitInvocation(inv),
        _ => throw new NotPortableException($"Unsupported expression node: {expr.NodeType}")
    };

    private string EmitConditional(ConditionalExpression c) =>
        $"(({Emit(c.Test)}) ? ({Emit(c.IfTrue)}) : ({Emit(c.IfFalse)}))";

    /// <summary>
    /// Only translates calls to the <see cref="Fn"/> library — the function
    /// name maps 1-to-1 onto a member of the browser-side <c>Dm</c> runtime
    /// (see <c>wwwroot/fn.js</c>). Other method calls (instance methods on
    /// strings/arrays/etc.) bail out as not-portable so the expression falls
    /// back to server evaluation; users should call into <see cref="Fn"/>
    /// for anything that needs to run in the browser.
    /// </summary>
    private string EmitMethodCall(MethodCallExpression call)
    {
        // A bare Fn call (Upper(x)) can surface as a delegate invocation:
        // delegate.Invoke(args) where call.Object is a constant delegate over
        // an Fn method. Unwrap to the underlying Fn method name.
        if (call.Method.Name == "Invoke" && call.Object is not null && AsFnMethod(call.Object) is { } invoked)
            return $"Dm.{invoked.Name}({string.Join(",", call.Arguments.Select(Emit))})";

        if (call.Method.DeclaringType != typeof(Fn))
            throw new NotPortableException($"Method call outside Fn library: {call.Method.DeclaringType?.Name}.{call.Method.Name}");

        var args = string.Join(",", call.Arguments.Select(Emit));
        return $"Dm.{call.Method.Name}({args})";
    }

    // Bare Fn calls can also parse as an InvocationExpression over a constant
    // delegate. Both forms resolve to the same Dm.<Name>(args) JS.
    private string EmitInvocation(InvocationExpression inv)
    {
        if (AsFnMethod(inv.Expression) is not { } fn)
            throw new NotPortableException("Unsupported invocation target");
        return $"Dm.{fn.Name}({string.Join(",", inv.Arguments.Select(Emit))})";
    }

    // Resolve an expression that carries a delegate over a static Fn method to
    // that MethodInfo, stripping any implicit conversion DynamicExpresso wraps
    // it in. Returns null when the expression isn't an Fn-backed delegate.
    private static System.Reflection.MethodInfo? AsFnMethod(Expression e)
    {
        while (e is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u)
            e = u.Operand;
        return e is ConstantExpression { Value: Delegate d } && d.Method.DeclaringType == typeof(Fn)
            ? d.Method
            : null;
    }

    private string EmitBinary(BinaryExpression b)
    {
        var op = b.NodeType switch
        {
            ExpressionType.Equal              => "===",
            ExpressionType.NotEqual           => "!==",
            ExpressionType.AndAlso            => "&&",
            ExpressionType.OrElse             => "||",
            ExpressionType.LessThan           => "<",
            ExpressionType.LessThanOrEqual    => "<=",
            ExpressionType.GreaterThan        => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.Add                => "+",
            ExpressionType.Subtract           => "-",
            ExpressionType.Multiply           => "*",
            ExpressionType.Divide             => "/",
            ExpressionType.Modulo             => "%",
            ExpressionType.Coalesce           => "??",
            _ => throw new NotPortableException($"Unsupported binary: {b.NodeType}")
        };
        return $"({Emit(b.Left)} {op} {Emit(b.Right)})";
    }

    private string EmitUnary(UnaryExpression u) => u.NodeType switch
    {
        ExpressionType.Not             => $"(!{Emit(u.Operand)})",
        ExpressionType.Negate           => $"(-{Emit(u.Operand)})",
        ExpressionType.UnaryPlus        => $"(+{Emit(u.Operand)})",
        // Implicit conversions (e.g. int → double, parameter → object) carry
        // no JS semantic — JS is dynamically typed, so we strip them.
        ExpressionType.Convert          => Emit(u.Operand),
        ExpressionType.ConvertChecked   => Emit(u.Operand),
        ExpressionType.TypeAs           => Emit(u.Operand),
        _ => throw new NotPortableException($"Unsupported unary: {u.NodeType}")
    };

    private static string EmitParameter(ParameterExpression p) => $"v.{p.Name}";

    private static string EmitConstant(ConstantExpression c)
    {
        if (c.Value is null) return "null";
        return c.Value switch
        {
            string s  => JsonSerializer.Serialize(s),
            char ch   => JsonSerializer.Serialize(ch.ToString()),
            bool b    => b ? "true" : "false",
            double d  => d.ToString("R",  CultureInfo.InvariantCulture),
            float  f  => f.ToString("R",  CultureInfo.InvariantCulture),
            decimal m => m.ToString(      CultureInfo.InvariantCulture),
            long   l  => l.ToString(      CultureInfo.InvariantCulture),
            int    i  => i.ToString(      CultureInfo.InvariantCulture),
            short  sh => sh.ToString(     CultureInfo.InvariantCulture),
            byte   by => by.ToString(     CultureInfo.InvariantCulture),
            _ => throw new NotPortableException($"Unsupported constant type: {c.Value.GetType().FullName}")
        };
    }

    private sealed class NotPortableException(string message) : Exception(message);
}
