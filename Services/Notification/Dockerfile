FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Shared/ShopFlow.Shared/ShopFlow.Shared.csproj Shared/ShopFlow.Shared/
COPY Services/Notification/Notification.Application/Notification.Application.csproj Services/Notification/Notification.Application/
COPY Services/Notification/Notification.Infrastructure/Notification.Infrastructure.csproj Services/Notification/Notification.Infrastructure/
COPY Services/Notification/Notification.Api/Notification.API.csproj Services/Notification/Notification.Api/
RUN dotnet restore Services/Notification/Notification.Api/Notification.API.csproj

COPY Shared/ShopFlow.Shared/ Shared/ShopFlow.Shared/
COPY Services/Notification/Notification.Application/ Services/Notification/Notification.Application/
COPY Services/Notification/Notification.Infrastructure/ Services/Notification/Notification.Infrastructure/
COPY Services/Notification/Notification.Api/ Services/Notification/Notification.Api/
RUN dotnet publish Services/Notification/Notification.Api/Notification.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:80

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Notification.API.dll"]
