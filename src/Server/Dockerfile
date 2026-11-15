FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SunsetMP.Server.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# музыку монтируем в /music, настройки — переменными окружения
ENV Library__MusicPath=/music
ENV Urls=http://0.0.0.0:5080
EXPOSE 5080
ENTRYPOINT ["dotnet", "SunsetMP.Server.dll"]
