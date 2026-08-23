FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Gateway.Api/Gateway.API.csproj Gateway.Api/
RUN dotnet restore Gateway.Api/Gateway.API.csproj

COPY Gateway.Api/ Gateway.Api/
RUN dotnet publish Gateway.Api/Gateway.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:80

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Gateway.API.dll"]
