using FWO.Middleware.Server;
using NUnit.Framework;

namespace FWO.Test;

/// <summary>
/// Tests the middleware API documentation page renderer.
/// </summary>
[TestFixture]
internal class ApiDocsPageTest
{
    /// <summary>
    /// Verifies the rendered page points browsers at the generated OpenAPI document.
    /// </summary>
    [Test]
    public void Render_WithOpenApiDocumentPath_ReturnsHtmlPage()
    {
        string html = ApiDocsPage.Render("/api-docs/v1.json");

        Assert.That(html, Does.Contain("<!doctype html>"));
        Assert.That(html, Does.Contain("FWO Middleware API Documentation"));
        Assert.That(html, Does.Contain("/api-docs/v1.json"));
    }

    /// <summary>
    /// Verifies invalid document paths are rejected.
    /// </summary>
    [Test]
    public void Render_WithEmptyOpenApiDocumentPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ApiDocsPage.Render(string.Empty));
    }
}
