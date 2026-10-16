# E2E runner: the official Playwright .NET image (browsers + .NET SDK) at the SAME version as the
# Microsoft.Playwright package. scripts/test-e2e.sh reads the version from Directory.Packages.props
# and passes it in, so the two cannot drift.
ARG PLAYWRIGHT_VERSION
FROM mcr.microsoft.com/playwright/dotnet:v${PLAYWRIGHT_VERSION}-noble
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY tests/Signage.E2E/Signage.E2E.csproj tests/Signage.E2E/
RUN dotnet restore tests/Signage.E2E/Signage.E2E.csproj

COPY tests/Signage.E2E/ tests/Signage.E2E/
RUN dotnet build tests/Signage.E2E/Signage.E2E.csproj -c Release --no-restore \
    && chmod -R a+rwX /src

# Run as the invoking host user (set by the script) so artefacts aren't root-owned.
ENV HOME=/tmp DOTNET_CLI_HOME=/tmp DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
# xUnit v3 test projects are executables (Microsoft.Testing.Platform); run the assembly directly.
ENTRYPOINT ["dotnet", "tests/Signage.E2E/bin/Release/net10.0/Signage.E2E.dll", "-noColor", "-result-xml", "/artifacts/results.xml"]
