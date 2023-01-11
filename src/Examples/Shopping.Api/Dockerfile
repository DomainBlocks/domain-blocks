FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Examples/Shopping.Api/Shopping.Api.csproj", "Examples/Shopping.Api/"]
RUN dotnet restore "Examples/Shopping.Api/Shopping.Api.csproj" -p:TargetFramework=net6.0
COPY . .
WORKDIR "/src/Examples/Shopping.Api"
RUN dotnet build "Shopping.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Shopping.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Shopping.Api.dll"]
