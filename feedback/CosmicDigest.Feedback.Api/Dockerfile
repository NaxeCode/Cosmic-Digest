FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ai-newsletter.csproj ./
COPY feedback/CosmicDigest.Feedback.Api/CosmicDigest.Feedback.Api.csproj feedback/CosmicDigest.Feedback.Api/
RUN dotnet restore feedback/CosmicDigest.Feedback.Api/CosmicDigest.Feedback.Api.csproj
COPY . .
RUN dotnet publish feedback/CosmicDigest.Feedback.Api/CosmicDigest.Feedback.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CosmicDigest.Feedback.Api.dll"]
