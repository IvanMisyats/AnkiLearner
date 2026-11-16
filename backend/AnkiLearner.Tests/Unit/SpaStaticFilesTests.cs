using AnkiLearner.Api;
using Microsoft.AspNetCore.StaticFiles;

namespace AnkiLearner.Tests.Unit;

public class SpaStaticFilesTests
{
    [Theory]
    [InlineData("main-MI7D5MLV.js")]
    [InlineData("chunk-CkDQ0ueU.js")]
    [InlineData("polyfills-B6TNHZQ6.js")]
    [InlineData("styles-OPUTW5UJ.css")]
    [InlineData("MAIN-ABC.JS")]
    public void CacheControlFor_HashedBundles_AreImmutable(string fileName) =>
        Assert.Equal("public, max-age=31536000, immutable", SpaStaticFiles.CacheControlFor(fileName));

    [Theory]
    [InlineData("index.html")]
    [InlineData("manifest.webmanifest")]
    [InlineData("favicon.ico")]
    [InlineData("icon-192.png")]
    [InlineData("icon.svg")]
    [InlineData("main.js.map")]
    [InlineData("noextension")]
    public void CacheControlFor_EverythingElse_IsRevalidated(string fileName) =>
        Assert.Equal("no-cache", SpaStaticFiles.CacheControlFor(fileName));

    /// <summary>The app configures no mapping of its own; it relies on this default.</summary>
    [Fact]
    public void DefaultContentTypes_ServeTheWebManifestAsManifestJson()
    {
        Assert.True(new FileExtensionContentTypeProvider()
            .TryGetContentType("manifest.webmanifest", out var contentType));
        Assert.Equal("application/manifest+json", contentType);
    }
}
