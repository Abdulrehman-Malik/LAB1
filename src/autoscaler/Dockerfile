# Build from the repo root:  docker build -f src/autoscaler/Dockerfile -t localhost:5000/autoscaler:1 .

# Stage 1: build with the full .NET SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project file only (cached until the .csproj changes)
COPY Directory.Build.props ./
COPY src/autoscaler/Autoscaler.csproj src/autoscaler/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages dotnet restore src/autoscaler/Autoscaler.csproj

COPY src/autoscaler/ src/autoscaler/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages dotnet publish src/autoscaler/Autoscaler.csproj -c Release -o /app --no-restore

# Stage 2: plain .NET runtime (no ASP.NET, it has no web endpoints)
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Autoscaler.dll"]
