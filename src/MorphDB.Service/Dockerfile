# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy Directory.Packages.props for Central Package Management
COPY Directory.Packages.props ./

# Copy project files for restore
COPY src/MorphDB.Core/MorphDB.Core.csproj src/MorphDB.Core/
COPY src/MorphDB.Npgsql/MorphDB.Npgsql.csproj src/MorphDB.Npgsql/
COPY src/MorphDB.Service/MorphDB.Service.csproj src/MorphDB.Service/

# Restore dependencies
RUN dotnet restore src/MorphDB.Service/MorphDB.Service.csproj

# Copy all source files
COPY src/ src/

# Build the application
WORKDIR /src/src/MorphDB.Service
RUN dotnet build -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Create non-root user for security
RUN addgroup --system --gid 1000 morphdb && \
    adduser --system --uid 1000 --ingroup morphdb morphdb

# Copy published application
COPY --from=publish /app/publish .

# Set ownership
RUN chown -R morphdb:morphdb /app

# Switch to non-root user
USER morphdb

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Entry point
ENTRYPOINT ["dotnet", "MorphDB.Service.dll"]
