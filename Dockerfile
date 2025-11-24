FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY LeadersAndFollowers.API/LeadersAndFollowers.API.csproj LeadersAndFollowers.API/
COPY LeadersAndFollowers.Core/LeadersAndFollowers.Core.csproj LeadersAndFollowers.Core/

RUN dotnet restore LeadersAndFollowers.API/LeadersAndFollowers.API.csproj

COPY . .

RUN dotnet publish LeadersAndFollowers.API/LeadersAndFollowers.API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "LeadersAndFollowers.API.dll"]
