FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/ src/
COPY tests/ tests/

RUN dotnet build \
    tests/DomainBlocks.EventStore.MongoDB.Client.Tests.Integration/DomainBlocks.EventStore.MongoDB.Client.Tests.Integration.csproj \
    -c Release --nologo

ENTRYPOINT ["dotnet", "test", \
    "tests/DomainBlocks.EventStore.MongoDB.Client.Tests.Integration/DomainBlocks.EventStore.MongoDB.Client.Tests.Integration.csproj", \
    "--no-build", "-c", "Release", "--nologo", \
    "--filter", "Name~MeasureLatency|Name~MeasureThroughputCeiling", \
    "--logger", "console;verbosity=normal"]
