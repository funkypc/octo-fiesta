# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

ARG VERSION=0.0.0-dev

COPY octo-fiesta.sln .
COPY octo-fiesta/octo-fiesta.csproj octo-fiesta/
COPY octo-fiesta.Tests/octo-fiesta.Tests.csproj octo-fiesta.Tests/

RUN dotnet restore

COPY octo-fiesta/ octo-fiesta/
COPY octo-fiesta.Tests/ octo-fiesta.Tests/
COPY youtube-music-bridge.py ./
COPY requirements.txt ./

RUN dotnet publish octo-fiesta/octo-fiesta.csproj -c Release -p:Version=$VERSION -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

RUN apt-get update && \
    apt-get install -y --no-install-recommends python3 python3-pip python3-venv && \
    rm -rf /var/lib/apt/lists/* && \
    python3 -m venv /opt/yt-venv && \
    /opt/yt-venv/bin/pip install --no-cache-dir -r /app/requirements.txt

COPY --from=build /app/publish .
COPY --from=build /src/youtube-music-bridge.py .

ENV YouTubeMusic__PythonPath=/opt/yt-venv/bin/python
ENV YouTubeMusic__ScriptPath=/app/youtube-music-bridge.py

RUN mkdir -p /app/downloads

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "octo-fiesta.dll"]
