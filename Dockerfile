# syntax=docker/dockerfile:1
# Multi-arch: the build stage always runs natively ($BUILDPLATFORM) and cross-publishes for
# $TARGETARCH, so `docker buildx build --platform linux/amd64,linux/arm64 .` needs no emulation.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG TARGETARCH
WORKDIR /src

# Restore first, from project files only, so the layer is cached until dependencies change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Signage.Web/Signage.Web.csproj src/Signage.Web/
RUN dotnet restore src/Signage.Web/Signage.Web.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/Signage.Web/Signage.Web.csproj -c Release -a $TARGETARCH --no-restore \
    -o /app /p:UseAppHost=false

# Chiseled = no shell or package manager; non-root ("app", uid 1654) by default.
# The "-extra" variant adds ICU and tzdata, which the IANA time-zone setting needs.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
# The base image already listens on port 8080 (ASPNETCORE_HTTP_PORTS=8080).
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Signage.Web.dll"]
