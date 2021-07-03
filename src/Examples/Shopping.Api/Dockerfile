FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["Examples/Shopping.Api/Shopping.Api.csproj", "Examples/Shopping.Api/"]
COPY ["DomainLib.EventStore.AspNet/DomainLib.EventStore.AspNetCore.csproj", "DomainLib.EventStore.AspNet/"]
COPY ["DomainLib/DomainLib.csproj", "DomainLib/"]
COPY ["DomainLib.Common/DomainLib.Common.csproj", "DomainLib.Common/"]
COPY ["DomainLib.Persistence.EventStore/DomainLib.Persistence.EventStore.csproj", "DomainLib.Persistence.EventStore/"]
COPY ["DomainLib.Persistence/DomainLib.Persistence.csproj", "DomainLib.Persistence/"]
COPY ["DomainLib.Serialization/DomainLib.Serialization.csproj", "DomainLib.Serialization/"]
COPY ["DomainLib.Serialization.Json/DomainLib.Serialization.Json.csproj", "DomainLib.Serialization.Json/"]
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
