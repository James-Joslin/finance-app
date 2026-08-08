FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY financesApi.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish --configuration Release --output /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER app

ENTRYPOINT ["dotnet", "financesApi.dll"]
