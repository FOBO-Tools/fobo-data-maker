using DataMaker.Expressions.Functions;
using DynamicExpresso;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataMaker.Expressions.Engine;

/// <summary>
/// Evaluates calculated-field and query expressions against a named parameter bag.
/// Wraps DynamicExpresso Interpreter, registers the <see cref="Fn"/> library,
/// and caches compiled lambdas by (expression, parameter shape) so repeated
/// record evaluation is memoized.
///
/// Data Maker binds form fields to parameters at compile time (one Interpreter
/// per form), then invokes per-record during record save or batch recompute.
/// </summary>
public sealed class ExpressionEngine
{
    private readonly ILogger<ExpressionEngine> _logger;
    private readonly Interpreter _interpreter;
    private readonly Dictionary<string, Lambda> _lambdaCache = new(StringComparer.Ordinal);

    public ExpressionEngine(ILogger<ExpressionEngine>? logger = null)
    {
        _logger = logger ?? NullLogger<ExpressionEngine>.Instance;
        _interpreter = new Interpreter(InterpreterOptions.Default);
        RegisterFunctionLibrary(_interpreter);
    }

    /// <summary>
    /// Compile an expression against a known parameter shape.
    /// The same (expression, shape) re-uses a cached Lambda across evaluations.
    /// </summary>
    public Lambda Compile(string expression, IReadOnlyList<Parameter> parameters)
    {
        var key = CacheKey(expression, parameters);
        if (_lambdaCache.TryGetValue(key, out var cached)) return cached;

        var lambda = _interpreter.Parse(expression, parameters.ToArray());
        _lambdaCache[key] = lambda;
        return lambda;
    }

    /// <summary>
    /// Evaluate a compiled expression with positional arguments.
    /// Caller is responsible for aligning <paramref name="values"/> to the parameter list used at compile time.
    /// </summary>
    public T? Evaluate<T>(Lambda lambda, params object?[] values)
    {
        try
        {
            var result = lambda.Invoke(values);
            if (result is null) return default;
            if (result is T typed) return typed;
            return (T)Convert.ChangeType(result, typeof(T));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ExpressionEngine] evaluation failed");
            return default;
        }
    }

    /// <summary>
    /// One-shot compile+evaluate against a name/value bag. Slower than
    /// pre-compiling with <see cref="Compile"/>; use for interactive previews only.
    /// </summary>
    public T? Evaluate<T>(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        var parameters = variables
            .Select(kv => new Parameter(kv.Key, kv.Value?.GetType() ?? typeof(object)))
            .ToArray();

        var values = variables.Select(kv => kv.Value).ToArray();

        try
        {
            var lambda = _interpreter.Parse(expression, parameters);
            var result = lambda.Invoke(values);
            if (result is null) return default;
            if (result is T typed) return typed;
            return (T)Convert.ChangeType(result, typeof(T));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ExpressionEngine] one-shot evaluation failed for '{Expr}'", expression);
            return default;
        }
    }

    /// <summary>
    /// Registers the <see cref="Fn"/> library on an interpreter: both the
    /// <c>Fn.</c>-prefixed reference AND every static method as a bare
    /// identifier. Shared with <see cref="JsCompiler"/> so the JS compiler
    /// parses the exact same expression surface the evaluator accepts —
    /// otherwise a bare <c>Upper(x)</c> would parse server-side but fail to
    /// compile to JS and silently degrade to server-only.
    /// </summary>
    internal static void RegisterFunctionLibrary(Interpreter interpreter)
    {
        interpreter.Reference(typeof(Fn));
        // Expose each static Fn method as a bare identifier (no "Fn." prefix).
        foreach (var method in typeof(Fn).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            interpreter.SetFunction(method.Name, Delegate.CreateDelegate(
                System.Linq.Expressions.Expression.GetDelegateType(
                    method.GetParameters().Select(p => p.ParameterType)
                        .Append(method.ReturnType).ToArray()),
                method));
        }
    }

    private static string CacheKey(string expression, IReadOnlyList<Parameter> parameters)
    {
        var shape = string.Join(",", parameters.Select(p => $"{p.Name}:{p.Type.FullName}"));
        return $"{expression}||{shape}";
    }
}
