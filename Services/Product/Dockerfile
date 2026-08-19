FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Product.Domain/Product.Domain.csproj Product.Domain/
COPY Product.Application/Product.Application.csproj Product.Application/
COPY Product.Infrastructure/Product.Infrastructure.csproj Product.Infrastructure/
COPY Product.Api/Product.API.csproj Product.Api/
RUN dotnet restore Product.Api/Product.API.csproj

COPY Product.Domain/ Product.Domain/
COPY Product.Application/ Product.Application/
COPY Product.Infrastructure/ Product.Infrastructure/
COPY Product.Api/ Product.Api/
RUN dotnet publish Product.Api/Product.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:80

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Product.API.dll"]
