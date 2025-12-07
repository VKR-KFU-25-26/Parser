FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["CourtParser.Worker/CourtParser.Worker.csproj", "CourtParser.Worker/"]
COPY ["CourtParser.Infrastructure/CourtParser.Infrastructure.csproj", "CourtParser.Infrastructure/"]
COPY ["CourtParser.Models/CourtParser.Models.csproj", "CourtParser.Models/"]
COPY ["CourtDecisions.Common/CourtDecisions.Common.csproj", "CourtDecisions.Common/"]
RUN dotnet restore "CourtParser.Worker/CourtParser.Worker.csproj"
COPY . .
WORKDIR "/src/CourtParser.Worker"
RUN dotnet build "./CourtParser.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./CourtParser.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CourtParser.Worker.dll"]
