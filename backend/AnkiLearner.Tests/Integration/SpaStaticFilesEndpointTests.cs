using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace AnkiLearner.Tests.Integration;

/// <summary>
/// An <see cref="ApiFactory"/> whose wwwroot holds a stand-in Angular build. Elsewhere wwwroot
/// is empty: the SPA is only copied in by the Docker image.
/// </summary>
public class SpaFactory : ApiFactory
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("ankilearner-wwwroot-").FullName;

    public SpaFactory()
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>SPA</title>");
        File.WriteAllText(Path.Combine(_webRoot, "main-AbC123xY.js"), "console.log('app');");
        File.WriteAllText(Path.Combine(_webRoot, "styles-AbC123xY.css"), "body {}");
        File.WriteAllText(Path.Combine(_webRoot, "manifest.webmanifest"), "{}");
        Directory.CreateDirectory(Path.Combine(_webRoot, "icons"));
        File.WriteAllBytes(Path.Combine(_webRoot, "icons", "icon-192.png"), [0x89, 0x50, 0x4E, 0x47]);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseWebRoot(_webRoot);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true);
    }
}

/// <summary>The real pipeline's Cache-Control and Content-Type for the SPA's files.</summary>
public class SpaStaticFilesEndpointTests(SpaFactory factory) : IClassFixture<SpaFactory>
{
    private const string Immutable = "public, max-age=31536000, immutable";

    [Theory]
    [InlineData("/", "text/html", "no-cache")]
    [InlineData("/index.html", "text/html", "no-cache")]
    // Deep links (home-screen shortcuts, the share target) are answered by the fallback.
    [InlineData("/translate?q=hund", "text/html", "no-cache")]
    [InlineData("/study", "text/html", "no-cache")]
    // A bundle deleted by a deploy also gets index.html: the policy follows the file served,
    // not the requested path, so the fallback page is never pinned under a .js URL.
    [InlineData("/chunk-GONE1234.js", "text/html", "no-cache")]
    [InlineData("/main-AbC123xY.js", "text/javascript", Immutable)]
    [InlineData("/styles-AbC123xY.css", "text/css", Immutable)]
    [InlineData("/manifest.webmanifest", "application/manifest+json", "no-cache")]
    [InlineData("/icons/icon-192.png", "image/png", "no-cache")]
    public async Task ServesEachFileWithItsCacheControl(string path, string contentType, string cacheControl)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(cacheControl, response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task UnknownApiPath_Is404_NotTheSpa()
    {
        var response = await factory.CreateClient().GetAsync("/api/no-such-endpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
