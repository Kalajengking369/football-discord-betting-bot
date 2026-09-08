# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies (leveraging Docker layer cache)
COPY ["Discord/Discord.csproj", "Discord/"]
RUN dotnet restore "Discord/Discord.csproj"

# Copy source code and build production release
COPY Discord/ Discord/
WORKDIR "/src/Discord"
RUN dotnet publish "Discord.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app

# Create directory for persistent SQLite database
RUN mkdir -p /app/data

# Copy compiled binaries from build stage
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Discord.dll"]
