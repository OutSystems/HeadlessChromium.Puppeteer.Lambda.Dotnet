using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HeadlessChromium.Puppeteer.Lambda.Dotnet
{
    /// <summary>
    /// Detects which platform-specific Chromium dependencies should be extracted, checking
    /// CHROMIUM_PLATFORM_OVERRIDE first, then auto-detecting via OS release files.
    /// </summary>
    internal class PlatformDetector
    {
        internal const string PlatformAl2023 = "al2023";
        internal const string PlatformWolfi = "wolfi";

        /// <summary>
        /// Chainguard images are Wolfi-based and some report <c>ID=chainguard</c> rather than
        /// <c>ID=wolfi</c>. Accepted as an alias so both spellings resolve to one platform.
        /// </summary>
        internal const string PlatformAliasChainguard = "chainguard";

        /// <summary>
        /// Carries only what a Wolfi image lacks on top of <c>al2023.tar.br</c>; built by
        /// <c>tools/build-wolfi-payload.sh</c>, which records why each library is in it.
        /// </summary>
        internal const string WolfiSupplementFileName = "wolfi.tar.br";

        private const string PlatformOverrideEnvVar = "CHROMIUM_PLATFORM_OVERRIDE";

        private static readonly string[] ValidPlatforms =
        {
            PlatformAl2023,
            PlatformWolfi
        };

        private static readonly Dictionary<string, string> PlatformAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { PlatformAliasChainguard, PlatformWolfi }
            };

        private readonly ILogger logger;
        private readonly string systemReleaseCpePath;
        private readonly string osReleasePath;

        public PlatformDetector(
            ILogger logger,
            string systemReleaseCpePath = "/etc/system-release-cpe",
            string osReleasePath = "/etc/os-release")
        {
            this.logger = logger;
            this.systemReleaseCpePath = systemReleaseCpePath;
            this.osReleasePath = osReleasePath;
        }

        /// <summary>
        /// The platform identifiers this library can extract dependencies for. Surfaced so that
        /// failure messages can list them without duplicating the set.
        /// </summary>
        internal static IReadOnlyCollection<string> SupportedPlatforms => ValidPlatforms;

        public string DetectPlatform()
        {
            var overridePlatform = GetEnvironmentVariableOverride();
            if (overridePlatform != null)
            {
                logger.LogDebug("Platform {Platform} overriden",
                    overridePlatform);
                return overridePlatform;
            }

            var detected = DetectFromSystemReleaseCpe() ?? DetectFromOsRelease();

            if (detected == null)
            {
                logger.LogWarning(
                    "Unable to detect platform via {SystemReleaseCpePath} or {OsReleasePath}",
                    systemReleaseCpePath, osReleasePath);
            }

            logger.LogDebug("Detected the platform {Platform}",
                detected);

            return detected;
        }

        internal static bool IsValidPlatform(string platform)
        {
            return NormalizePlatform(platform) != null;
        }

        /// <summary>
        /// Resolves a platform identifier - whether supplied by a caller, by the override
        /// environment variable, or by detection - to its canonical form, mapping aliases such as
        /// <c>chainguard</c> onto the platform they name. Returns null when the value names no
        /// supported platform.
        /// </summary>
        internal static string NormalizePlatform(string platform)
        {
            if (string.IsNullOrEmpty(platform))
            {
                return null;
            }

            if (PlatformAliases.TryGetValue(platform, out var aliased))
            {
                return aliased;
            }

            return ValidPlatforms.FirstOrDefault(
                p => p.Equals(platform, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Maps a detected platform id to the dependency archives that must be extracted for it,
        /// in extraction order.
        /// <para>
        /// Ubuntu 22.04 reuses the AL2023 archive: it runs unmodified on Ubuntu 22.04 (validated
        /// against a real ubuntu:22.04 container), since AL2023's bundled libs satisfy Ubuntu's
        /// glibc/NSS requirements and there is no Sparticuz-published Ubuntu-specific build.
        /// </para>
        /// <para>
        /// Wolfi reuses it too, but needs one library on top. A dependency walk of the Sparticuz
        /// binary resolves every NEEDED entry from either the image (libc, libdl, libm,
        /// libpthread, libgcc_s and the loader) or the AL2023 archive (libnspr4, libnss3,
        /// libnssutil3, libplc4, libplds4, libexpat). That walk is not the whole story: the first
        /// time a page needs TLS, NSS dlopen()s its PKCS#11 module libsoftokn3.so, which links
        /// against libsqlite3.so.0 for the SQLite-backed cert database. Amazon Linux and Ubuntu
        /// ship that library in the base image; Chainguard's distroless images do not, and NSS
        /// initialisation failing is fatal - Chromium aborts mid-navigation. wolfi.tar.br supplies
        /// it. The libraries the image lacks besides - fontconfig, freetype, glib, dbus, X11,
        /// xkbcommon, gbm, drm, uuid - are statically linked into the Sparticuz build and never
        /// appear in the NEEDED set.
        /// </para>
        /// </summary>
        internal static IReadOnlyList<string> GetDependencyFileNames(string platform)
        {
            var normalized = NormalizePlatform(platform) ?? platform;

            if (normalized == PlatformWolfi)
            {
                return new[] { $"{PlatformAl2023}.tar.br", WolfiSupplementFileName };
            }

            return new[] { $"{normalized}.tar.br" };
        }

        /// <summary>
        /// Describes the OS release files this detector inspected, for diagnostics on images that
        /// have no shell to investigate with. Performs file reads only and never throws.
        /// </summary>
        internal string DescribeDetectedOs()
        {
            try
            {
                if (File.Exists(osReleasePath))
                {
                    var fields = ReadOsReleaseFields();
                    return $"{osReleasePath} (ID={Field(fields, "ID")}, NAME={Field(fields, "NAME")}, " +
                           $"VERSION_ID={Field(fields, "VERSION_ID")})";
                }

                if (File.Exists(systemReleaseCpePath))
                {
                    var cpe = File.ReadLines(systemReleaseCpePath).FirstOrDefault();
                    return $"{systemReleaseCpePath} ({(string.IsNullOrEmpty(cpe) ? "(empty)" : cpe)})";
                }

                return $"neither {systemReleaseCpePath} nor {osReleasePath} exists";
            }
            catch (IOException e)
            {
                return $"unreadable ({e.Message})";
            }
            catch (UnauthorizedAccessException e)
            {
                return $"unreadable ({e.Message})";
            }
        }

        private static string Field(IReadOnlyDictionary<string, string> fields, string key)
        {
            return fields.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)
                ? value
                : "(absent)";
        }

        private string GetEnvironmentVariableOverride()
        {
            var envVar = Environment.GetEnvironmentVariable(PlatformOverrideEnvVar);
            if (string.IsNullOrEmpty(envVar))
            {
                return null;
            }

            var normalized = NormalizePlatform(envVar);
            if (normalized != null)
            {
                logger.LogDebug(
                    "Platform override via {PlatformOverrideEnvVar}: {Platform}",
                    PlatformOverrideEnvVar, normalized);
                return normalized;
            }

            logger.LogWarning(
                "Invalid {PlatformOverrideEnvVar} value: {Value}. Falling back to auto-detection.",
                PlatformOverrideEnvVar, envVar);
            return null;
        }

        private string DetectFromSystemReleaseCpe()
        {
            if (!File.Exists(systemReleaseCpePath))
            {
                return null;
            }

            var osDetails = File.ReadLines(systemReleaseCpePath).FirstOrDefault() ?? string.Empty;

            string platform = null;
            if (osDetails.EndsWith("amazon:amazon_linux:2023"))
            {
                platform = PlatformAl2023;
            }

            if (platform != null)
            {
                logger.LogDebug(
                    "Detected platform {Platform} via {SystemReleaseCpePath}",
                    platform, systemReleaseCpePath);
            }

            return platform;
        }

        private string DetectFromOsRelease()
        {
            if (!File.Exists(osReleasePath))
            {
                return null;
            }

            var fields = ReadOsReleaseFields();
            fields.TryGetValue("ID", out var id);
            fields.TryGetValue("NAME", out var name);
            fields.TryGetValue("VERSION_ID", out var versionId);

            logger.LogDebug(
                "Read ID={Id} NAME={Name} VERSION_ID={VersionId} from {OsReleasePath}",
                id, name, versionId, osReleasePath);

            // ID is the machine-readable identifier; NAME is a display string, used only as a
            // fallback for files that omit ID.
            var identity = string.IsNullOrEmpty(id) ? name : id;
            var platform = ResolveOsIdentity(identity, versionId);

            if (platform != null)
            {
                logger.LogDebug(
                    "Detected platform {Platform} via {OsReleasePath}",
                    platform, osReleasePath);
            }

            return platform;
        }

        /// <summary>
        /// Matches an os-release identity against the platforms this library knows, comparing the
        /// parsed value to a known identifier rather than asking whether an identifier contains
        /// the parsed value - the inverted test admitted false positives such as NAME="U".
        /// </summary>
        private static string ResolveOsIdentity(string identity, string versionId)
        {
            if (string.IsNullOrEmpty(identity))
            {
                return null;
            }

            // Wolfi is a rolling distribution whose VERSION_ID is a build date, so it is ignored.
            if (identity.Equals(PlatformWolfi, StringComparison.OrdinalIgnoreCase) ||
                identity.Equals(PlatformAliasChainguard, StringComparison.OrdinalIgnoreCase))
            {
                return PlatformWolfi;
            }

            return null;
        }

        /// <summary>
        /// Parses os-release into its KEY=VALUE fields, dropping comments and blank lines and
        /// stripping the optional quoting around values.
        /// </summary>
        private Dictionary<string, string> ReadOsReleaseFields()
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var line in File.ReadLines(osReleasePath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                var separator = trimmed.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = trimmed.Substring(0, separator).Trim();
                var value = trimmed.Substring(separator + 1).Trim().Trim('"', '\'');

                fields[key] = value;
            }

            return fields;
        }
    }
}
