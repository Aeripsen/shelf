# Build context is the repo root: docker build -f src/Shelf.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo

# Restore first, from the project files only, so this layer is cached until a dependency changes.
COPY global.json nuget.config ./
COPY src/Shelf.Contracts/Shelf.Contracts.csproj src/Shelf.Contracts/
COPY src/Shelf.Core/Shelf.Core.csproj src/Shelf.Core/
COPY src/Shelf.Api/Shelf.Api.csproj src/Shelf.Api/
RUN dotnet restore src/Shelf.Api/Shelf.Api.csproj

COPY src/Shelf.Contracts/ src/Shelf.Contracts/
COPY src/Shelf.Core/ src/Shelf.Core/
COPY src/Shelf.Api/ src/Shelf.Api/
RUN dotnet publish src/Shelf.Api/Shelf.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# .NET 8 images listen on 8080 and run as the non-root "app" user.
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Shelf.Api.dll"]
