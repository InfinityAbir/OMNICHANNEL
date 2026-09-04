# Single-image deploy: builds the Angular SPA and the .NET API, then serves both from one
# ASP.NET Core process (the API serves /api and /hubs; everything else falls back to the SPA's
# index.html — see Program.cs's app.MapFallbackToFile). One Render web service, no cross-origin
# CORS/SignalR config needed between frontend and backend. See docs/deployment.md.

# ---- Stage 1: build the Angular SPA ----
FROM node:22-alpine AS frontend-build
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npx ng build --configuration production

# ---- Stage 2: build + publish the .NET API ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ ./src/
RUN dotnet restore src/Omnichannel.Api/Omnichannel.Api.csproj
RUN dotnet publish src/Omnichannel.Api/Omnichannel.Api.csproj -c Release -o /app/publish --no-restore

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend-build /app/publish .
# Angular's build output lands under wwwroot's own root, alongside the existing wwwroot/widget
# assets (the widget embed script/CSS) — no overlap, no overwrite.
COPY --from=frontend-build /src/web/dist/web/browser/ ./wwwroot/

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Render assigns its own $PORT at container start (not build time) and expects the app to listen
# on it, so it can't be baked in as a fixed ENV — this maps Render's $PORT onto
# ASPNETCORE_HTTP_PORTS (.NET 8+'s supported port-binding var) when the container actually
# starts, falling back to 8080 for a plain local `docker run` where $PORT isn't set.
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet Omnichannel.Api.dll"]
