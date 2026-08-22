FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Shared/ShopFlow.Shared/ShopFlow.Shared.csproj Shared/ShopFlow.Shared/
COPY Services/Order/Order.Domain/Order.Domain.csproj Services/Order/Order.Domain/
COPY Services/Order/Order.Application/Order.Application.csproj Services/Order/Order.Application/
COPY Services/Order/Order.Infrastructure/Order.Infrastructure.csproj Services/Order/Order.Infrastructure/
COPY Services/Order/Order.Api/Order.API.csproj Services/Order/Order.Api/
RUN dotnet restore Services/Order/Order.Api/Order.API.csproj

COPY Shared/ShopFlow.Shared/ Shared/ShopFlow.Shared/
COPY Services/Order/Order.Domain/ Services/Order/Order.Domain/
COPY Services/Order/Order.Application/ Services/Order/Order.Application/
COPY Services/Order/Order.Infrastructure/ Services/Order/Order.Infrastructure/
COPY Services/Order/Order.Api/ Services/Order/Order.Api/
RUN dotnet publish Services/Order/Order.Api/Order.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:80

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Order.API.dll"]
