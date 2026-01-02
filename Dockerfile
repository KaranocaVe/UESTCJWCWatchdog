# Build stage runs on linux/arm64 to avoid emulation/MSBuild issues, final image targets linux/amd64 for FunctionGraph.
FROM --platform=linux/arm64 mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG TARGETRID=linux-x64

COPY UESTCJWCWatchdog.sln ./
COPY src/Watchdog.Core/Watchdog.Core.csproj src/Watchdog.Core/
COPY src/Watchdog.Function/Watchdog.Function.csproj src/Watchdog.Function/
RUN dotnet restore src/Watchdog.Function/Watchdog.Function.csproj -r $TARGETRID

COPY src/Watchdog.Core/ src/Watchdog.Core/
COPY src/Watchdog.Function/ src/Watchdog.Function/
RUN dotnet publish src/Watchdog.Function/Watchdog.Function.csproj -c Release -o /out --no-restore -r $TARGETRID --self-contained false -p:UseAppHost=false

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8000
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

COPY --from=build /out/ ./

# Install Playwright browsers + OS deps for Chromium (Patchright/Microsoft.Playwright compatible).
RUN dotnet exec --runtimeconfig /app/Watchdog.Function.runtimeconfig.json /app/Microsoft.Playwright.dll install --with-deps chromium
RUN chmod -R a+rX /app/.playwright /ms-playwright

EXPOSE 8000
ENTRYPOINT ["dotnet", "/app/Watchdog.Function.dll"]
