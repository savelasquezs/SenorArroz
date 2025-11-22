# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files (excluding Tests for production build)
COPY ["SenorArroz.API/SenorArroz.API.csproj", "SenorArroz.API/"]
COPY ["SenorArroz.Application/SenorArroz.Application.csproj", "SenorArroz.Application/"]
COPY ["SenorArroz.Domain/SenorArroz.Domain.csproj", "SenorArroz.Domain/"]
COPY ["SenorArroz.Infrastructure/SenorArroz.Infrastructure.csproj", "SenorArroz.Infrastructure/"]
COPY ["SenorArroz.Shared/SenorArroz.Shared.csproj", "SenorArroz.Shared/"]

# Restore dependencies (restore API project which will restore all its dependencies)
RUN dotnet restore "SenorArroz.API/SenorArroz.API.csproj"

# Copy all source code
COPY . .

# Build the application
WORKDIR "/src/SenorArroz.API"
RUN dotnet build "SenorArroz.API.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "SenorArroz.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime (using SDK image to have dotnet-ef available)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS final
WORKDIR /app

# Install wget for health checks
RUN apt-get update && \
    apt-get install -y --no-install-recommends wget && \
    rm -rf /var/lib/apt/lists/*

# Install dotnet-ef globally
RUN dotnet tool install --global dotnet-ef --version 9.0.0

# Add dotnet tools to PATH
ENV PATH="${PATH}:/root/.dotnet/tools"

# Create a non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Copy published application
COPY --from=publish /app/publish .

# Change ownership to non-root user
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose ports
EXPOSE 8080
EXPOSE 8081

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check - verifica que el puerto esté escuchando
HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://localhost:8080/swagger/index.html || exit 1

# Start the application
ENTRYPOINT ["dotnet", "SenorArroz.API.dll"]

