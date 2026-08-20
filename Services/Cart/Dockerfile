FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Shared/ShopFlow.Shared/ShopFlow.Shared.csproj Shared/ShopFlow.Shared/
COPY Services/Cart/Cart.Domain/Cart.Domain.csproj Services/Cart/Cart.Domain/
COPY Services/Cart/Cart.Application/Cart.Application.csproj Services/Cart/Cart.Application/
COPY Services/Cart/Cart.Infrastructure/Cart.Infrastructure.csproj Services/Cart/Cart.Infrastructure/
COPY Services/Cart/Cart.Api/Cart.API.csproj Services/Cart/Cart.Api/
RUN dotnet restore Services/Cart/Cart.Api/Cart.API.csproj

COPY Shared/ShopFlow.Shared/ Shared/ShopFlow.Shared/
COPY Services/Cart/Cart.Domain/ Services/Cart/Cart.Domain/
COPY Services/Cart/Cart.Application/ Services/Cart/Cart.Application/
COPY Services/Cart/Cart.Infrastructure/ Services/Cart/Cart.Infrastructure/
COPY Services/Cart/Cart.Api/ Services/Cart/Cart.Api/
RUN dotnet publish Services/Cart/Cart.Api/Cart.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:80

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Cart.API.dll"]
