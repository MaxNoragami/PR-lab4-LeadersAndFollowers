FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY LeadersAndFollowers/LeadersAndFollowers.csproj LeadersAndFollowers/

RUN dotnet restore LeadersAndFollowers/LeadersAndFollowers.csproj

COPY . .

RUN dotnet publish LeadersAndFollowers/LeadersAndFollowers.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "LeadersAndFollowers.dll"]
