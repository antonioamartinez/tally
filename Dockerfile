# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the package layer is cached until dependencies change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Tally.Domain/Tally.Domain.csproj src/Tally.Domain/
COPY src/Tally.Web/Tally.Web.csproj src/Tally.Web/
RUN dotnet restore src/Tally.Web/Tally.Web.csproj

COPY src/ src/
RUN dotnet publish src/Tally.Web/Tally.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Writable folder for the SQLite file when running without Postgres.
RUN mkdir /data && chown "$APP_UID" /data
VOLUME /data

COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Tally="Data Source=/data/tally.db"
EXPOSE 8080

# The .NET images ship a non-root "app" user; run as it.
USER $APP_UID
ENTRYPOINT ["dotnet", "Tally.Web.dll"]
