FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["Examples/Shopping.Api/Shopping.Api.csproj", "Examples/Shopping.Api/"]
COPY ["DomainBlocks.EventStore.AspNet/DomainBlocks.EventStore.AspNetCore.csproj", "DomainBlocks.EventStore.AspNet/"]
COPY ["DomainBlocks/DomainBlocks.csproj", "DomainBlocks/"]
COPY ["DomainBlocks.Common/DomainBlocks.Common.csproj", "DomainBlocks.Common/"]
COPY ["DomainBlocks.Persistence.EventStore/DomainBlocks.Persistence.EventStore.csproj", "DomainBlocks.Persistence.EventStore/"]
COPY ["DomainBlocks.Persistence/DomainBlocks.Persistence.csproj", "DomainBlocks.Persistence/"]
COPY ["DomainBlocks.Serialization/DomainBlocks.Serialization.csproj", "DomainBlocks.Serialization/"]
COPY ["DomainBlocks.Serialization.Json/DomainBlocks.Serialization.Json.csproj", "DomainBlocks.Serialization.Json/"]
COPY ["Examples/Shopping.Domain/Shopping.Domain.csproj", "Examples/Shopping.Domain/"]
RUN dotnet restore "Examples/Shopping.Api/Shopping.Api.csproj"
COPY . .
WORKDIR "/src/Examples/Shopping.Api"
RUN dotnet build "Shopping.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Shopping.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Shopping.Api.dll"]
