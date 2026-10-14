# syntax=docker/dockerfile:1
# Single-service image for Radix, which cannot select a stage of the multi-target root Dockerfile.
# Keep in sync with the matching stage there.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/AgenticLab.AiService/AgenticLab.AiService.csproj \
    --configuration Release \
    --output /out \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --chmod=644 LICENSE NOTICE ./
# The sample workspace for Workspace:Mode=ReadOnlySample, owned by root so the app user can only read it.
COPY --chmod=555 sample-workspace ./sample-workspace
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER $APP_UID
COPY --from=build /out .
ENTRYPOINT ["dotnet", "AgenticLab.AiService.dll"]
