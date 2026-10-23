# ---------- Etapa 1: build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY api/*.csproj ./api/
RUN dotnet restore api/FerkAutoparts.Api.csproj
COPY api/ ./api/
RUN dotnet publish api/FerkAutoparts.Api.csproj -c Release -o /app

# ---------- Etapa 2: runtime (imagem leve) ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FerkAutoparts.Api.dll"]
