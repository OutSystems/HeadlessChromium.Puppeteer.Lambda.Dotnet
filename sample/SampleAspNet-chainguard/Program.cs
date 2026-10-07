using HeadlessChromium.Puppeteer.Lambda.Dotnet;
using PuppeteerSharp;

var builder = WebApplication.CreateBuilder(args);

// DEBUG logging so the platform-detection source and the extracted archives are visible in
// container logs. The Chainguard runtime image is distroless, so log output and exception
// messages are the only diagnostics available (NFR3).
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.WebHost.UseUrls("http://0.0.0.0:8080");

var app = builder.Build();

app.MapGet("/healthz", () => Results.Text("ok"));

app.MapGet("/screenshot", async (ILoggerFactory loggerFactory, ILogger<Program> logger) =>
{
    var launcher = new HeadlessChromiumPuppeteerLauncher(loggerFactory);

    await using var browser = await launcher.LaunchAsync();
    await using var page = await browser.NewPageAsync();

    // Rendered from a local document rather than a live site: this endpoint exists to prove that
    // Chromium launches and that the extracted fonts are used, and a network dependency would
    // only make that check flaky.
    await page.SetContentAsync(SampleDocument);

    // Reported so a failure can be told apart from a font failure: if the extracted fonts were
    // not picked up, the measured width collapses towards the fallback metrics.
    var metrics = await page.EvaluateExpressionAsync<string>(
        "JSON.stringify({" +
        "  fonts: document.fonts.size," +
        "  width: Math.round(document.getElementById('probe').getBoundingClientRect().width)," +
        "  height: Math.round(document.getElementById('probe').getBoundingClientRect().height)" +
        "})");

    logger.LogInformation("Rendered probe metrics: {Metrics}", metrics);

    var png = await page.ScreenshotDataAsync();

    logger.LogInformation("Screenshot produced {ByteCount} bytes", png.Length);

    return Results.File(png, "image/png");
});

// Renders a real URL over HTTPS, which /screenshot deliberately does not. The distinction matters:
// Chromium only initialises NSS when a page first needs TLS, and a missing NSS dependency aborts
// the browser process there rather than at launch. A local-document render therefore passes on an
// image where every network render crashes - which is exactly how the missing libsqlite3.so.0 got
// through review. Defaults to a URL that is cheap to fetch and stable.
app.MapGet("/pdf", async (ILoggerFactory loggerFactory, ILogger<Program> logger, string? url) =>
{
    var target = string.IsNullOrWhiteSpace(url) ? "https://example.com/" : url;

    var launcher = new HeadlessChromiumPuppeteerLauncher(loggerFactory);

    await using var browser = await launcher.LaunchAsync();
    await using var page = await browser.NewPageAsync();

    var response = await page.GoToAsync(target, new NavigationOptions
    {
        WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
        Timeout = 30_000,
    });

    logger.LogInformation(
        "Navigated to {Url} with status {Status}", target, (int?)response?.Status);

    var pdf = await page.PdfDataAsync();

    logger.LogInformation("PDF produced {ByteCount} bytes", pdf.Length);

    return Results.File(pdf, "application/pdf");
});

app.Run();

public partial class Program
{
    // A gradient background keeps the PNG comfortably above a trivial size, and the glyph rows
    // make a tofu-box failure obvious to the eye when the image is inspected.
    private const string SampleDocument = """
        <!doctype html>
        <html>
          <head>
            <meta charset="utf-8">
            <style>
              body {
                margin: 0;
                width: 800px;
                height: 600px;
                background: linear-gradient(135deg, #1b2a4a 0%, #3f7fbf 50%, #8fd3c7 100%);
                color: #ffffff;
                font-family: sans-serif;
                display: flex;
                flex-direction: column;
                justify-content: center;
                align-items: center;
              }
              h1 { font-size: 44px; margin: 0 0 16px; }
              p  { font-size: 26px; margin: 6px 0; }
            </style>
          </head>
          <body>
            <h1 id="probe">Chainguard Wolfi render check</h1>
            <p>abcdefghijklmnopqrstuvwxyz</p>
            <p>ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789</p>
          </body>
        </html>
        """;
}
