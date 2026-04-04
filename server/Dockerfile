FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY server.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
RUN useradd -r -s /bin/false appuser && mkdir -p /app/data && chown -R appuser:appuser /app/data
USER appuser
VOLUME /app/data
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:5000/api/health || exit 1
ENTRYPOINT ["dotnet", "server.dll"]
