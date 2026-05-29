using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Web;

namespace FOBO.Auth;

/// <summary>
/// One-shot HTTP listener on a loopback port. Waits for Cognito's
/// authorization-code redirect, captures the query string, and responds with
/// a friendly "you can close this tab" page. Part of the native OAuth
/// Authorization Code + PKCE flow (RFC 8252).
/// </summary>
public sealed class LoopbackListener(int port) : IDisposable
{
    // Path portion of the callback URL isn't needed here — the listener
    // binds to 127.0.0.1:port/ and accepts the first request. The caller's
    // OAuthOptions.RedirectUri stays the source of truth for what Cognito
    // will actually redirect to.
    private readonly HttpListener _listener = new();

    /// <summary>
    /// Starts the listener, waits for the redirect, returns its query params.
    /// Caller must then verify state + extract <c>code</c> for the token exchange.
    /// </summary>
    public async Task<NameValueCollection> WaitForCallbackAsync(CancellationToken ct)
    {
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        try
        {
            var ctxTask = _listener.GetContextAsync();
            var completed = await Task.WhenAny(ctxTask, Task.Delay(Timeout.Infinite, ct));
            if (completed != ctxTask)
            {
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException("Loopback listener timed out waiting for browser callback.");
            }

            var ctx = await ctxTask;
            var query = HttpUtility.ParseQueryString(ctx.Request.Url!.Query);

            var body = Encoding.UTF8.GetBytes(SuccessPage);
            ctx.Response.ContentType     = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = body.Length;
            await ctx.Response.OutputStream.WriteAsync(body, ct);
            ctx.Response.Close();

            return query;
        }
        finally
        {
            try { _listener.Stop(); } catch { /* idempotent */ }
        }
    }

    public void Dispose()
    {
        try { _listener.Close(); } catch { /* idempotent */ }
    }

    private const string SuccessPage = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>Signed in</title>
          <style>
            body { font-family: -apple-system, Segoe UI, Roboto, sans-serif;
                   display: flex; align-items: center; justify-content: center;
                   height: 100vh; margin: 0; background: #001c40; color: #eaf0ff; }
            .card { background: #0b2b5a; padding: 2rem 3rem; border-radius: 12px;
                    box-shadow: 0 8px 24px rgba(0,0,0,0.3); text-align: center; }
            h1 { margin: 0 0 0.5rem 0; font-size: 1.4rem; }
            p  { margin: 0; opacity: 0.8; }
          </style>
        </head>
        <body>
          <div class="card">
            <h1>You're signed in ✓</h1>
            <p>You can close this tab and return to the app.</p>
          </div>
        </body>
        </html>
        """;
}
