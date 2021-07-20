FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["Examples/Shopping.ReadModel/Shopping.ReadModel.csproj", "Examples/Shopping.ReadModel/"]
RUN dotnet restore "Examples/Shopping.ReadModel/Shopping.ReadModel.csproj"
COPY . .
WORKDIR "/src/Examples/Shopping.ReadModel"
RUN dotnet build "Shopping.ReadModel.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Shopping.ReadModel.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Shopping.ReadModel.dll"]