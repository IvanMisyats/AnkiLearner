namespace AnkiLearner.Api;

/// <summary>
/// How the Angular SPA's files in wwwroot are served: one <see cref="StaticFileOptions"/> for
/// both the static file middleware and the deep-link fallback to index.html.
/// </summary>
public static class SpaStaticFiles
{
    /// <summary>For content-hashed bundles: the name changes whenever the content does.</summary>
    public const string Immutable = "public, max-age=31536000, immutable";

    /// <summary>Cache, but ask the server (a cheap 304 via ETag) before every use.</summary>
    public const string Revalidate = "no-cache";

    /// <summary>
    /// The Cache-Control value for a file in wwwroot. Angular builds with
    /// <c>outputHashing: "all"</c>, so only its .js and .css bundles get a new name on every
    /// change and may be cached forever. Everything else keeps its name across deploys and must
    /// be revalidated: index.html (it names the current bundles; a stale copy references deleted
    /// ones), the PWA manifest, the icons and the favicon.
    /// </summary>
    public static string CacheControlFor(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
            ? Immutable
            : Revalidate;
    }

    /// <summary>
    /// Options for <c>UseStaticFiles</c> and <c>MapFallbackToFile</c>. The fallback needs them
    /// too: without them a deep link (/translate, /study, a home-screen shortcut, a share)
    /// returns index.html with no Cache-Control, which browsers may cache heuristically.
    /// The default content types already include .webmanifest → application/manifest+json.
    /// </summary>
    public static StaticFileOptions Options() => new()
    {
        OnPrepareResponse = ctx =>
            ctx.Context.Response.Headers.CacheControl = CacheControlFor(ctx.File.Name),
    };
}
