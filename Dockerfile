# ─────────────────────────────────────────────────────────────────────────────
# STAGE 1 — Angular build
# ─────────────────────────────────────────────────────────────────────────────
FROM node:22-alpine AS angular-build

WORKDIR /angular

COPY src/Inkhound.Client/package*.json ./
RUN npm ci --legacy-peer-deps

COPY src/Inkhound.Client/ ./
RUN npm run build -- --configuration production

# ─────────────────────────────────────────────────────────────────────────────
# STAGE 2 — .NET build
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS dotnet-build

WORKDIR /app

COPY src/Inkhound.Core/*.csproj ./Inkhound.Core/
COPY src/Inkhound.Web/*.csproj  ./Inkhound.Web/
RUN dotnet restore ./Inkhound.Web/Inkhound.Web.csproj

COPY src/Inkhound.Core/ ./Inkhound.Core/
COPY src/Inkhound.Web/  ./Inkhound.Web/

# Copy Angular build output into wwwroot
COPY --from=angular-build /angular/dist/inkhound.client/browser/ ./Inkhound.Web/wwwroot/

RUN dotnet publish ./Inkhound.Web/Inkhound.Web.csproj -c Release -o /publish

# ─────────────────────────────────────────────────────────────────────────────
# STAGE 3 — Runtime image
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

WORKDIR /app

COPY --from=dotnet-build /publish ./

ENV APP_PORT=8080
EXPOSE ${APP_PORT}

ENTRYPOINT ["dotnet", "Inkhound.Web.dll"]
