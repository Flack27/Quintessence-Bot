FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["QutieBot/QutieBot.csproj", "QutieBot/"]
COPY ["QutieDAL/QutieDAL.csproj", "QutieDAL/"]
COPY ["QutieDTO/QutieDTO.csproj", "QutieDTO/"]
RUN dotnet restore "./QutieBot/QutieBot.csproj"
COPY . .
WORKDIR "/src/QutieBot"
RUN dotnet build "./QutieBot.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./QutieBot.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "QutieBot.dll"]