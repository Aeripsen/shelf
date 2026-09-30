# Build context is the repo root: docker build -f src/Shelf.Fulfilment/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo

COPY global.json nuget.config ./
COPY src/Shelf.Contracts/Shelf.Contracts.csproj src/Shelf.Contracts/
COPY src/Shelf.Core/Shelf.Core.csproj src/Shelf.Core/
COPY src/Shelf.Fulfilment/Shelf.Fulfilment.csproj src/Shelf.Fulfilment/
RUN dotnet restore src/Shelf.Fulfilment/Shelf.Fulfilment.csproj

COPY src/Shelf.Contracts/ src/Shelf.Contracts/
COPY src/Shelf.Core/ src/Shelf.Core/
COPY src/Shelf.Fulfilment/ src/Shelf.Fulfilment/
RUN dotnet publish src/Shelf.Fulfilment/Shelf.Fulfilment.csproj -c Release -o /app --no-restore

# A worker has no HTTP endpoints, so it runs on the smaller runtime image, not aspnet.
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "Shelf.Fulfilment.dll"]
