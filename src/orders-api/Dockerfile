# Build from the repo root:  docker build -f src/orders-api/Dockerfile -t localhost:5000/orders-api:1 .

# Stage 1: build with the full .NET SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files only: this layer is reused until a .csproj changes,
# and the cache mount keeps downloaded NuGet packages between builds
COPY Directory.Build.props ./
COPY src/shared/Lab.Shared/Lab.Shared.csproj src/shared/Lab.Shared/
COPY src/orders-api/OrdersApi.csproj src/orders-api/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages dotnet restore src/orders-api/OrdersApi.csproj

# Then the source code, which changes often
COPY src/shared-protos/ src/shared-protos/
COPY src/shared/Lab.Shared/ src/shared/Lab.Shared/
COPY src/orders-api/ src/orders-api/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages dotnet publish src/orders-api/OrdersApi.csproj -c Release -o /app --no-restore

# Stage 2: small runtime-only image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# The base image sets ASPNETCORE_HTTP_PORTS=8080, but our code picks its own ports (Lab.Shared AddLabDefaults).
# Clearing it avoids Kestrel's "Overriding address(es)" warning at every start.
ENV ASPNETCORE_HTTP_PORTS=
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrdersApi.dll"]
