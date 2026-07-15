# HeadlessChromium.Puppeteer.Lambda.Dotnet
Packages everything you need to run [PuppeteerSharp](https://github.com/kblok/puppeteer-sharp) in AWS Lambda on Chromium into a Nuget Package

# Description
The chromium binary for this project has been extracted from the NPM project [Sparticuz/chromium](https://github.com/Sparticuz/chromium).  It is automatically extracted to `/tmp/chromium` at runtime.  This project requires the lambda runtime to be configured as `dotnet8` or `dotnet10` (Amazon Linux 2023 based; Amazon Linux 2 runtimes such as `dotnet6`/`dotnet7` are no longer supported since Sparticuz/chromium dropped AL2 binaries).  For [now](https://github.com/Sparticuz/chrome-aws-lambda/issues/7), the chromium binary only supports `x86_64`

# Usage
Screenshot a URL as a byte[].  

```
var browserLauncher = new HeadlessChromiumPuppeteerLauncher(logger);

using(var browser = await browserLauncher.LaunchAsync())
using(var page = await browser.NewPageAsync())
{
    await page.GoToAsync(url);
    return await page.ScreenshotDataAsync();
}
```

For more use cases see the [PuppeteerSharp documentation](http://www.puppeteersharp.com/api/index.html)

# Projects using this library
These projects are using this library and are good examples of how you might consume this nuget package
- [HtmlToPDF](https://github.com/unnijeevan/HtmlToPDF)

# Building
To build locally:
```
.\build.ps1 -Target Build
```
