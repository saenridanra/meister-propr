# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY MeisterProPR.slnx .
COPY src/ src/
COPY tests/ tests/

RUN dotnet restore

RUN dotnet publish src/MeisterProPR.Api/MeisterProPR.Api.csproj \
    -c Release -o /app --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Kerberos runtime required by Microsoft.TeamFoundationServer.Client; curl for healthcheck
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl \
    && rm -rf /var/lib/apt/lists/*

# Non-root user (rootless container)
RUN useradd --system --no-create-home appuser && chown -R appuser /app
USER appuser

COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "MeisterProPR.Api.dll"]
