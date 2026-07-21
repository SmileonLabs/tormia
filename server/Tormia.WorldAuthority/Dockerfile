FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY server/Tormia.WorldAuthority/Tormia.WorldAuthority.csproj server/Tormia.WorldAuthority/
COPY server/Tormia.Ontology.Runtime/Tormia.Ontology.Runtime.csproj server/Tormia.Ontology.Runtime/
RUN dotnet restore server/Tormia.WorldAuthority/Tormia.WorldAuthority.csproj

COPY server/Tormia.WorldAuthority/ server/Tormia.WorldAuthority/
COPY server/Tormia.Ontology.Runtime/ server/Tormia.Ontology.Runtime/
COPY Assets/Scripts/Ontology/Core/ Assets/Scripts/Ontology/Core/
RUN dotnet publish server/Tormia.WorldAuthority/Tormia.WorldAuthority.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tormia.WorldAuthority.dll"]
