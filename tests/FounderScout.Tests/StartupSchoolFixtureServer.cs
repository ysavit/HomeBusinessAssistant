using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FounderScout.Application;

namespace FounderScout.Tests;

internal sealed class StartupSchoolFixtureServer : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly int profileCount;
    private readonly Task loop;

    private StartupSchoolFixtureServer(int port, int profileCount)
    {
        this.profileCount = profileCount;
        BaseUrl = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add($"{BaseUrl}/");
        listener.Start();
        loop = Task.Run(() => RunAsync(lifetime.Token));
    }

    public string BaseUrl { get; }

    public string ListUrl => $"{BaseUrl}/list";

    public string LoginUrl => $"{BaseUrl}/login";

    public string ChallengeUrl => $"{BaseUrl}/challenge";

    public string LoadMoreUrl => $"{BaseUrl}/load-more";

    public string InfiniteUrl => $"{BaseUrl}/infinite";

    public string UnexpectedHostUrl => $"{BaseUrl}/unexpected-host";

    public string CurrentCandidateListUrl => $"{BaseUrl}/cofounder-matching";

    public string CurrentDiscoveryListUrl => $"{BaseUrl}/cofounder-matching/founders-you-may-know";

    public string CurrentNextCandidateUrl => $"{BaseUrl}/cofounder-matching/candidate/next";

    public static StartupSchoolFixtureServer Start(int profileCount = 25)
    {
        if (profileCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(profileCount));
        }

        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return new(port, profileCount);
    }

    public StartupSchoolSourceOptions CreateOptions(string? entryUrl = null) => new(
        StartupSchoolSourceOptions.CurrentAdapterVersion,
        entryUrl ?? ListUrl,
        ["127.0.0.1"],
        [new("css", "[data-testid='authenticated']")],
        [new("css", "[data-testid='login-form']")],
        [new("css", "a[data-profile-id]")],
        [new("css", "main[data-profile-id]")],
        [new("css", "[data-testid='profile-name']")],
        [new("css", "a[rel='next']")],
        [new("css", "button[data-load-more]")],
        [new("css", "[data-testid='challenge']")],
        [new("css", "[data-testid='throttled']")],
        [new("css", "[data-testid='access-denied']")],
        "pagination",
        10,
        60,
        10,
        0,
        1,
        null,
        HeadlessDiscovery: true,
        StoreRawHtml: false);

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        listener.Stop();
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or OperationCanceledException)
        {
        }

        listener.Close();
        lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "/";
                if (path == "/cofounder-matching/candidate/next")
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Redirect;
                    context.Response.RedirectLocation = "/cofounder-matching/candidate/founder-beta";
                    continue;
                }

                string html = Render(path);
                byte[] bytes = Encoding.UTF8.GetBytes(html);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    private string Render(string path)
    {
        if (path == "/login")
        {
            return "<html><body><main><form data-testid='login-form'><label>Password<input value='fixture-secret'></label><button>Log in</button></form></main></body></html>";
        }

        if (path == "/challenge")
        {
            return "<html><body><main data-testid='authenticated'><div data-testid='challenge'>Verify you are human</div></main></body></html>";
        }

        if (path == "/load-more")
        {
            return """
                <html><body><main data-testid='authenticated'>
                  <a data-profile-id='fixture-01' href='/profile/fixture-01'>first</a>
                  <button data-load-more onclick="this.insertAdjacentHTML('beforebegin', '&lt;a data-profile-id=&quot;fixture-02&quot; href=&quot;/profile/fixture-02&quot;&gt;second&lt;/a&gt;'); this.remove();">Load more</button>
                </main></body></html>
                """;
        }

        if (path == "/infinite")
        {
            return """
                <html><body style='min-height:3000px'><main data-testid='authenticated'>
                  <a data-profile-id='fixture-01' href='/profile/fixture-01'>first</a>
                </main><script>
                  let added = false;
                  const addNext = () => {
                    if (!added) {
                      document.querySelector('main').insertAdjacentHTML('beforeend', '<a data-profile-id="fixture-02" href="/profile/fixture-02">second</a>');
                      added = true;
                    }
                  };
                  window.scrollTo = addNext;
                  addEventListener('scroll', addNext);
                </script></body></html>
                """;
        }

        if (path == "/unexpected-host")
        {
            return "<html><body><main data-testid='authenticated'><a data-profile-id='outside' href='https://outside.example/profile/1'>outside</a></main></body></html>";
        }

        if (path is "/cofounder-matching" or "/cofounder-matching/founders-you-may-know")
        {
            return """
                <html><body>
                  <div data-testid='authenticated'>Authenticated fixture session</div>
                  <nav>
                    <a href='/cofounder-matching/inbox'>Inbox</a>
                    <a href='/cofounder-matching/profile'>My profile</a>
                    <a href='/cofounder-matching/founders-you-may-know'>Founders You May Know</a>
                    <a href='/cofounder-matching/saved-profiles'>Saved Profiles</a>
                    <a href='/cofounder-matching/candidate/next'>Next candidate</a>
                  </nav>
                  <a href='/cofounder-matching/founder-alpha'>View candidate</a>
                </body></html>
                """;
        }

        if (path.StartsWith("/cofounder-matching/founder-", StringComparison.Ordinal)
            || path.StartsWith("/cofounder-matching/candidate/founder-", StringComparison.Ordinal))
        {
            string id = WebUtility.HtmlEncode(path[(path.Contains("/candidate/", StringComparison.Ordinal)
                ? "/cofounder-matching/candidate/".Length
                : "/cofounder-matching/".Length)..]);
            return $"""
                <html><body>
                  <div data-testid="authenticated">Authenticated fixture session</div>
                  <h1>Synthetic Founder {id}</h1>
                  <div><p>Built a synthetic fixture company, interviewed customers, and validated a focused market need through repeated product experiments.</p></div>
                  <div><p>A complementary co-founder for a local fixture would help build a durable customer-development and distribution plan.</p></div>
                  <div><h2>Send some invites! You have 20 remaining this week.</h2></div>
                  <div><h2>Continue browsing other candidates.</h2></div>
                  <a href="/cofounder-matching/candidate/next">Next candidate</a>
                </body></html>
                """;
        }

        if (path.StartsWith("/profile/", StringComparison.Ordinal))
        {
            string id = WebUtility.HtmlEncode(path["/profile/".Length..]);
            return $"""
                <html><body>
                  <div data-testid="authenticated"></div>
                  <main data-profile-id="{id}">
                    <h1 data-testid="profile-name">Synthetic Founder {id}</h1>
                    <img src="/private-photo/{id}.png" alt="fixture portrait">
                    <section data-profile-section><h2>Background</h2><p>Built synthetic fixture company {id} and interviewed customers.</p><p>Age: PROTECTED_AGE_MARKER</p></section>
                    <section data-profile-section><h2>Looking for</h2><p>A technical co-founder for a local fixture.</p></section>
                    <span data-testid="last-seen">Active this week</span>
                    <input name="csrf" value="fixture-sensitive-value">
                  </main>
                </body></html>
                """;
        }

        var links = new StringBuilder();
        for (var index = 1; index <= profileCount; index++)
        {
            string id = $"fixture-{index:00}";
            links.Append(CultureInfo.InvariantCulture, $"<article data-testid='profile-card'><a data-profile-id='{id}' href='/profile/{id}?session=fixture-secret'>{id}</a></article>");
        }

        links.Append("<a data-profile-id='fixture-01' href='/profile/fixture-01'>duplicate</a>");
        return $"<html><body><main data-testid='authenticated'><h1>Founder fixtures</h1>{links}</main></body></html>";
    }
}
