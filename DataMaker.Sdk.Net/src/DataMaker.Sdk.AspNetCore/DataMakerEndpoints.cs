using System.Text.Json;
using DataMaker.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DataMaker.Sdk.AspNetCore;

/// <summary>Options for the server-side encrypt endpoint.</summary>
public sealed class DataMakerSubmitOptions
{
    /// <summary>Public submissions API base.</summary>
    public string ApiBaseUrl { get; set; } = DataMakerClient.DefaultApiBaseUrl;
}

public static class DataMakerEndpoints
{
    /// <summary>
    /// Maps a server-side encrypt endpoint. The browser (server-mode
    /// &lt;datamaker-form&gt;) POSTs <c>{ formId, values }</c> here; this
    /// resolves the form's .dmf via <paramref name="dmfResolver"/>, validates +
    /// seals server-side, and forwards to /submissions. Returns
    /// <c>{ submissionId, editToken }</c>, or 400 <c>{ issues }</c> on
    /// validation failure.
    /// </summary>
    /// <param name="dmfResolver">Maps a formId to its .dmf bytes (e.g. read from disk / blob store). Return null for unknown ids.</param>
    public static IEndpointConventionBuilder MapDataMakerSubmit(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<string, byte[]?> dmfResolver,
        DataMakerSubmitOptions? options = null)
    {
        var apiBase = options?.ApiBaseUrl ?? DataMakerClient.DefaultApiBaseUrl;

        return endpoints.MapPost(pattern, async (HttpContext ctx) =>
        {
            ServerSubmitRequest? req;
            try { req = await ctx.Request.ReadFromJsonAsync<ServerSubmitRequest>(); }
            catch { return Results.BadRequest(new { error = "invalid JSON body" }); }

            if (req is null || string.IsNullOrEmpty(req.FormId))
                return Results.BadRequest(new { error = "formId is required" });

            var dmf = dmfResolver(req.FormId);
            if (dmf is null)
                return Results.NotFound(new { error = "unknown formId" });

            FormDescriptor form;
            try { form = DataMakerClient.ReadForm(dmf); }
            catch (DmfException e) { return Results.BadRequest(new { error = e.Message }); }

            var http = ctx.RequestServices.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
            var client = new DataMakerClient(http, apiBase);

            var values = Normalize(req.Values);
            try
            {
                var result = await client.SubmitAsync(form, values, ct: ctx.RequestAborted);
                return Results.Ok(new { submissionId = result.SubmissionId, editToken = result.EditToken });
            }
            catch (ValidationException ve)
            {
                return Results.BadRequest(new { issues = ve.Issues.Select(i => new { field = i.Field, message = i.Message }) });
            }
            catch (SubmissionException se)
            {
                return Results.StatusCode(se.Status);
            }
        });
    }

    // Map raw JSON values to CLR primitives the SDK's coercion understands.
    private static Dictionary<string, object?> Normalize(Dictionary<string, JsonElement>? raw)
    {
        var outVals = new Dictionary<string, object?>();
        if (raw is null) return outVals;
        foreach (var (k, v) in raw) outVals[k] = ToClr(v);
        return outVals;
    }

    private static object? ToClr(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.Array => e.EnumerateArray().Select(ToClr).ToList(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => e.GetRawText(),
    };

    private sealed record ServerSubmitRequest(string FormId, Dictionary<string, JsonElement>? Values);
}
