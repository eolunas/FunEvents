# syntax=docker/dockerfile:1
#
# Se construye desde la RAIZ del repositorio, no desde esta carpeta:
#   docker build -f src/FunEvents.Api/Dockerfile .
# El contexto tiene que incluir Directory.Packages.props y los demas proyectos.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Primero solo los ficheros de proyecto: mientras no cambien las dependencias,
# Docker reutiliza la capa de restore y la reconstruccion es cuestion de segundos.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/FunEvents.Domain/FunEvents.Domain.csproj                 src/FunEvents.Domain/
COPY src/FunEvents.Application/FunEvents.Application.csproj       src/FunEvents.Application/
COPY src/FunEvents.Infrastructure/FunEvents.Infrastructure.csproj src/FunEvents.Infrastructure/
COPY src/FunEvents.Api/FunEvents.Api.csproj                       src/FunEvents.Api/

RUN dotnet restore src/FunEvents.Api/FunEvents.Api.csproj

# Ahora si, el codigo.
COPY src/ src/

RUN dotnet publish src/FunEvents.Api/FunEvents.Api.csproj \
    -c Release \
    -o /app \
    --no-restore

# Permisos de lectura para todos sobre lo publicado.
#
# COPY conserva los modos de los ficheros del contexto de build, asi que un
# appsettings.json que en la maquina de quien construye tenga modo 600 llega a
# la imagen igual de cerrado, y el proceso -que corre como "app", no como root-
# revienta al arrancar con "Access to the path '/app/appsettings.json' is
# denied". Es un fallo que solo aparece en la maquina de algunos, que es la peor
# clase de fallo: normalizar aqui hace la imagen independiente del umask de
# quien la construye.
RUN chmod -R a+rX /app

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true

EXPOSE 8080

# Usuario sin privilegios: la imagen aspnet ya trae el usuario "app".
# Se cambia DESPUES del COPY para que los ficheros no se creen con permisos
# que luego el propio proceso no pueda leer.
USER app

ENTRYPOINT ["dotnet", "FunEvents.Api.dll"]
