FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/MageBackend.csproj .
RUN dotnet restore

COPY src/ .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-jammy-chiseled AS runtime
WORKDIR /app

EXPOSE 8888

COPY --from=build /app/publish .
COPY entrypoint.sh /entrypoint.sh

RUN chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]
