############################################################
# Build stage
############################################################
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached until the csproj changes
COPY ["src/api/CapitecAssessment/CapitecAssessment.csproj", "src/api/CapitecAssessment/"]
RUN dotnet restore "src/api/CapitecAssessment/CapitecAssessment.csproj"

COPY src/api/CapitecAssessment/ src/api/CapitecAssessment/
RUN dotnet publish "src/api/CapitecAssessment/CapitecAssessment.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false --no-restore

############################################################
# Runtime stage
############################################################
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

COPY --from=build /app/publish ./

# Built-in non-root user in the .NET 10 images
USER $APP_UID

ENTRYPOINT ["dotnet", "CapitecAssessment.dll"]
