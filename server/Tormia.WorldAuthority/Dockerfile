FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY server/Tormia.WorldAuthority/Tormia.WorldAuthority.csproj server/Tormia.WorldAuthority/
COPY server/Tormia.Ontology.Runtime/Tormia.Ontology.Runtime.csproj server/Tormia.Ontology.Runtime/
RUN dotnet restore server/Tormia.WorldAuthority/Tormia.WorldAuthority.csproj \
    --runtime linux-x64

COPY server/Tormia.WorldAuthority/ server/Tormia.WorldAuthority/
COPY server/Tormia.Ontology.Runtime/ server/Tormia.Ontology.Runtime/
COPY Assets/Scripts/Ontology/Core/ Assets/Scripts/Ontology/Core/
COPY Assets/Scripts/Ontology/Realtime/Protocol/ Assets/Scripts/Ontology/Realtime/Protocol/
RUN dotnet publish server/Tormia.WorldAuthority/Tormia.WorldAuthority.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained false \
    -p:UseAppHost=false \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
COPY --from=build /app/publish .
EXPOSE 8080/tcp
EXPOSE 5273/udp
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=5 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && grep -q '200 OK' <&3"]
USER $APP_UID
ENTRYPOINT ["dotnet", "Tormia.WorldAuthority.dll"]
