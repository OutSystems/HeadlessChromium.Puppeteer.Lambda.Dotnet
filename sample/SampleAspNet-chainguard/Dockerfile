# Proves the library provisions Chromium on a hardened, distroless Chainguard (Wolfi) image.
#
# Deliberately minimal: no apk packages are added and no Chromium is installed by hand. Everything
# Chromium needs is either already in the base image or extracted at runtime from the NuGet
# package's own payloads. If this image needs a Dockerfile change to render, the library has not
# actually delivered Chainguard support.
#
# The app is published on the host beforehand, matching how sample/SampleLambda-dotnet*/ package
# themselves, because the build needs the local ./artifacts NuGet feed that lives two directories
# up and outside this build context:
#
#   dotnet publish sample/SampleAspNet-chainguard -c Release -o sample/SampleAspNet-chainguard/bin/Release/publish
#   docker build -t sample-aspnet-chainguard sample/SampleAspNet-chainguard

FROM cgr.dev/chainguard/aspnet-runtime:latest

WORKDIR /app

# Keep root ownership; only execution happens as the non-root user
COPY "bin/Release/publish" .

# The image's own default user. Declared explicitly so the non-root assumption is visible here
# rather than inherited silently: Chromium is extracted to /tmp (mode 1777), which is the only
# writable location this uid has.
USER 65532

EXPOSE 8080

ENTRYPOINT ["dotnet", "SampleAspNetChainguard.dll"]
