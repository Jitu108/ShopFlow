FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Identity.Domain/Identity.Domain.csproj Identity.Domain/
COPY Identity.Application/Identity.Application.csproj Identity.Application/
COPY Identity.Infrastructure/Identity.Infrastructure.csproj Identity.Infrastructure/
COPY Identity.Api/Identity.API.csproj Identity.Api/
RUN dotnet restore Identity.Api/Identity.API.csproj

COPY Identity.Domain/ Identity.Domain/
COPY Identity.Application/ Identity.Application/
COPY Identity.Infrastructure/ Identity.Infrastructure/
COPY Identity.Api/ Identity.Api/
RUN dotnet publish Identity.Api/Identity.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:80

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Identity.API.dll"]
