# FunEvents — Plataforma de venta y reserva de entradas

Prueba técnica — Ingeniero Programador .NET Core.

Plataforma centralizada de venta de entradas para eventos, con tres canales: portal propio, oficinas de atención al cliente y colaboradores externos que integran el sistema en su propio portal o punto de venta.

| | |
|---|---|
| **Punto 1 — Arquitectura** | 📖 **[Documentación completa](https://eolunas.github.io/FunEvents/)** · también en [`docs/index.html`](docs/index.html) |
| **Punto 2 — Prototipo** | API REST + cliente de consola, en este repositorio |
---

## Arrancar en un comando

**Requisito único: Docker.**

```bash
docker compose up --build
```

Levanta PostgreSQL 16 y la API, crea el esquema y siembra los datos de demostración.

| | |
|---|---|
| Swagger | <http://localhost:8080/swagger> |
| Catálogo | <http://localhost:8080/api/v1/events> |
| Liveness | <http://localhost:8080/health> |
| Readiness | <http://localhost:8080/health/ready> |

Después, en otra terminal:

```bash
# Demo completa sin intervención
dotnet run --project clients/FunEvents.ConsoleClient -- --demo

# Menú interactivo
dotnet run --project clients/FunEvents.ConsoleClient
```

La demo recorre catálogo, usuarios, reserva de punta a punta, rechazo por usuario inactivo, idempotencia, **canal de colaboradores con API Key**, **límite de peticiones** y concurrencia. Termina comprobando que 15 peticiones simultáneas sobre un evento de 10 plazas producen exactamente 10 reservas.

### Si algún puerto está ocupado

Los puertos del host son configurables; no hace falta editar `docker-compose.yml`:

```bash
FUNEVENTS_API_PORT=9090 FUNEVENTS_DB_PORT=55432 docker compose up --build
dotnet run --project clients/FunEvents.ConsoleClient -- --demo --url http://localhost:9090
```

### Sin Docker para la API

```bash
# 1. PostgreSQL
docker run -d --name funevents-db \
  -e POSTGRES_DB=funevents -e POSTGRES_USER=funevents -e POSTGRES_PASSWORD=funevents \
  -p 5432:5432 postgres:16-alpine

# 2. API  (http://localhost:8080)
dotnet run --project src/FunEvents.Api

# 3. Cliente
dotnet run --project clients/FunEvents.ConsoleClient
```

---

## Tests

```bash
# Unitarios — dominio y casos de uso. No necesitan nada.
dotnet test tests/FunEvents.UnitTests

# Integración — API completa contra PostgreSQL real (Testcontainers). Requiere Docker.
dotnet test tests/FunEvents.IntegrationTests
```

Los tests de integración usan **PostgreSQL real, no el proveedor InMemory de EF Core**. Todo lo relevante de este sistema depende del motor —el UPDATE condicional que impide la sobreventa, la clave primaria como exclusión mutua, `FOR UPDATE SKIP LOCKED`, los índices parciales— y InMemory no implementa nada de eso: los tests pasarían en verde sin probar nada.

---

## Cómo está organizado

```
FunEvents/
├── src/
│   ├── FunEvents.Api/              Controllers, seguridad, filtros, manejo de errores, Swagger
│   ├── FunEvents.Application/      Casos de uso, DTOs, validadores, políticas
│   ├── FunEvents.Domain/           Entidades e invariantes  ← sin ninguna dependencia
│   └── FunEvents.Infrastructure/   EF Core, repositorios, idempotencia, workers
├── clients/
│   └── FunEvents.ConsoleClient/    Cliente de consola (solo HTTP, sin referencias al servidor)
├── tests/
│   ├── FunEvents.UnitTests/        Dominio + casos de uso con dobles de prueba
│   └── FunEvents.IntegrationTests/ API completa contra PostgreSQL real
└── docs/
    └── index.html                  Documento de arquitectura (punto 1) y guía de ejecución
```

`FunEvents.Domain.csproj` **no tiene ni una sola referencia**: ni paquetes NuGet ni otros proyectos. La regla de dependencia de Clean Architecture no es una convención documentada aquí, es algo que el fichero de proyecto hace verificable.

El cliente de consola tampoco referencia el servidor: habla solo por HTTP y JSON, igual que tendría que hacerlo el portal de un colaborador escrito en otro lenguaje. Si compartiera los DTOs, la demo no probaría que el contrato REST se sostiene solo.

**Stack:** .NET 10 · ASP.NET Core · PostgreSQL 16 (EF Core + Npgsql) · Serilog · FluentValidation · Swagger/OpenAPI · xUnit + FluentAssertions + NSubstitute + Testcontainers.

---

## API

| Método | Ruta | Descripción | Credencial |
|--------|------|-------------|------------|
| `GET` | `/api/v1/events` | Catálogo paginado (`page`, `pageSize`, `search`) | Pública |
| `GET` | `/api/v1/events/{id}` | Detalle de un evento | Pública |
| `GET` | `/api/v1/events/{id}/availability` | Disponibilidad actual | Pública |
| `GET` | `/api/v1/users` · `/api/v1/users/{id}` | Usuarios de demostración | Pública |
| `POST` | `/api/v1/reservations` | **Crear reserva** — requiere `Idempotency-Key` | `X-Api-Key` en canal Partner |
| `GET` | `/api/v1/reservations/{id}` | Consultar reserva | Filtrada por colaborador |
| `GET` | `/health` · `/health/ready` | Liveness · readiness | Pública, exenta de cuota |

`src/FunEvents.Api/FunEvents.Api.http` contiene peticiones listas para ejecutar, incluidos todos los casos de error.

### Ejemplo

```bash
curl -i -X POST http://localhost:8080/api/v1/reservations \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $(uuidgen)" \
  -d '{
        "eventId": "a0000000-0000-0000-0000-000000000001",
        "userId":  "b0000000-0000-0000-0000-000000000001",
        "ticketQuantity": 2,
        "channel": "Online"
      }'
```

Repetir el comando **con la misma key** devuelve `200 OK` y la misma reserva con `previouslyCreated: true`, en lugar de crear una segunda.

Por el canal de colaboradores, que exige credencial:

```bash
curl -i -X POST http://localhost:8080/api/v1/reservations \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "X-Api-Key: funevents-demo-partner-key" \
  -d '{
        "eventId": "a0000000-0000-0000-0000-000000000001",
        "userId":  "b0000000-0000-0000-0000-000000000001",
        "ticketQuantity": 1,
        "channel": "Partner"
      }'
```

El `partnerId` de la respuesta lo fija el servidor a partir de la clave; enviarlo en el cuerpo devuelve `400`.

### Errores

Todos los errores son `application/problem+json` (RFC 9457) con un campo `errorCode` estable:

```json
{
  "type": "https://api.funevents.com/errors/insufficient-capacity",
  "title": "Insufficient capacity",
  "status": 409,
  "detail": "Not enough capacity left for event 'Comedy Night'.",
  "errorCode": "INSUFFICIENT_CAPACITY",
  "correlationId": "9f2c1a..."
}
```

`errorCode` es lo que un integrador debe programar; `title` y `detail` son para humanos y pueden cambiar de redacción.

| Status | Cuándo |
|--------|--------|
| 400 | Petición mal formada, validación fallida, falta `Idempotency-Key`, `partnerId` en el cuerpo |
| 401 | `API_KEY_REQUIRED`, `INVALID_API_KEY` |
| 403 | `INSUFFICIENT_SCOPE` |
| 404 | `EVENT_NOT_FOUND`, `USER_NOT_FOUND`, o reserva de otro colaborador |
| 409 | `INSUFFICIENT_CAPACITY`, `REQUEST_IN_PROGRESS` |
| 422 | `EVENT_NOT_PUBLISHED`, `USER_INACTIVE`, `PER_USER_LIMIT_EXCEEDED`, `IDEMPOTENCY_KEY_REUSED` |
| 429 | `RATE_LIMIT_EXCEEDED` — con cabecera `Retry-After` |

---

## Datos de demostración

Son los "códigos de evento y de usuario ya conocidos" del enunciado.

| Eventos | Aforo | Estado |
|---------|-------|--------|
| `a0000000-0000-0000-0000-000000000001` — FunFest 2026 | 100 | Publicado |
| `a0000000-0000-0000-0000-000000000002` — TechConf 2026 | 25 | Publicado |
| `a0000000-0000-0000-0000-000000000003` — Comedy Night | 10 | Publicado *(prueba de concurrencia)* |
| `a0000000-0000-0000-0000-000000000004` — Ensayo General | 50 | **Borrador** *(prueba el 422)* |

| Usuarios | Estado |
|----------|--------|
| `b0000000-0000-0000-0000-000000000001` — Ana Martínez | Activa |
| `b0000000-0000-0000-0000-000000000002` — Carlos Rojas | Activo |
| `b0000000-0000-0000-0000-000000000003` — Usuario Inactivo | **Inactivo** *(prueba el 422)* |

| Colaboradores | API Key | Permisos | Cupo |
|---------------|---------|----------|------|
| Ticketera Aliada S.A. | `funevents-demo-partner-key` | `events:read`, `reservations:create`, `reservations:read` | 500/min |
| Portal Solo Consulta | `funevents-demo-partner-key-readonly` | `events:read`, `reservations:read` | 60/min *(prueba el 403 y el 429)* |
| Colaborador Dado de Baja | `funevents-demo-partner-key-revoked` | — | *(inactivo — prueba el 401)* |

En la base de datos se persiste **únicamente el SHA-256** de cada clave. Son credenciales de datos de ejemplo, fijas a propósito para que quien revise pueda ejercitar el canal de colaboradores sin darse de alta.

---

## Las cuatro decisiones que sostienen el diseño

Detalle completo, alternativas descartadas y los doce ADR en **[la documentación](docs/index.html)**.

### 1. No se puede sobrevender, por construcción

El aforo se comprueba y se consume en **una sola sentencia SQL**:

```sql
UPDATE "Events" SET "ReservedCount" = "ReservedCount" + @qty
WHERE "Id" = @id AND "State" = 'Published'
  AND "Capacity" - "ReservedCount" >= @qty;
```

`1 fila` → había aforo y ya está consumido. `0 filas` → no cabía, `409`. No hay una lectura previa que pueda quedar obsoleta, así que no existe ventana en la que dos compradores se cuelen a la vez. La sobreventa no se detecta ni se corrige: **no puede ocurrir**.

Se descartó la concurrencia optimista: con 15 peticiones simultáneas produciría 1 éxito y 14 excepciones a reintentar, el peor comportamiento posible justo en el momento de máxima demanda.

### 2. Los reintentos son seguros

`POST /reservations` exige `Idempotency-Key`. La **clave primaria de la tabla es el mecanismo de exclusión mutua**: PostgreSQL garantiza que solo una petición la gana, sin Redis ni lock distribuido. Se guarda además un SHA-256 del cuerpo, así que reutilizar la misma key con un payload distinto se rechaza (`422`) en lugar de devolver silenciosamente la reserva de otra compra.

Si la operación falla después de tomar la key, **la key se libera** para que el cliente pueda reintentar.

### 3. La identidad del colaborador nunca viene del cuerpo

El canal Partner exige `X-Api-Key`. La clave se guarda como SHA-256 y se resuelve con caché de 30 s, lo que permite **revocar dando de baja al colaborador**, sin esperar a que caduque nada. El `PartnerId` lo fija el servidor a partir de la credencial: aceptarlo del cliente permitiría a un colaborador atribuir sus ventas a otro. Y si un colaborador consulta la reserva de otro, la respuesta es **404, no 403** — un 403 confirmaría que esa reserva existe.

El cupo de peticiones se particiona **por colaborador**, con el límite de su contrato, para que un socio con un bucle mal programado no consuma el cupo de los demás.

### 4. La caducidad funciona con N réplicas

El worker toma el lote de reservas vencidas con `FOR UPDATE SKIP LOCKED`, de modo que cada instancia se lleva un lote disjunto. Devolver el aforo dos veces sería tan grave como la sobreventa, pero al revés.

---

## Alcance declarado

Por adelantado, para que la revisión no tenga que descubrirlo:

| | |
|---|---|
| **OAuth2/OIDC para portal y oficinas** | Diseñado, no implementado. Exige un proveedor de identidad externo que no forma parte del prototipo; simularlo demostraría que sé firmar un JWT, no que el sistema esté protegido. |
| **Rotación de API Keys** | Diseñada. `ApiKeyHasher.Generate()` ya existe; faltan el endpoint de administración y la columna `ApiKeyHash_Previous`. |
| **Limitador distribuido** | El actual mantiene el estado por instancia: con N réplicas el cupo efectivo es N veces el configurado. Se acepta a sabiendas (ADR-007). |
| **Pasarela de pago** | Fuera de alcance. El dominio soporta la transición `Confirm()`. |
| **.NET Aspire** | No usado. `docker compose` cubre este prototipo con menos piezas móviles. |
| **Migraciones EF versionadas** | Ver abajo. |

### Sobre las migraciones

Al arrancar, la API aplica las migraciones si existen y, si no hay ninguna, crea el esquema desde el modelo (`EnsureCreated`) dejando un aviso en el log. El prototipo arranca siempre, recién clonado y sin pasos previos.

Para generar la migración inicial:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate \
  --project src/FunEvents.Infrastructure \
  --startup-project src/FunEvents.Api
```

En producción esto no se hace al arrancar la API, sino como paso del pipeline de despliegue: N réplicas arrancando a la vez no deben competir por migrar, y el SQL debe poder revisarse antes de aplicarse.

---

## Documentación

Todo el punto 1 —contexto, estilo arquitectónico, capas, contrato de API, concurrencia, idempotencia, modelo de datos, seguridad, escalabilidad, observabilidad, plan de evolución y doce ADR— vive en un **único documento HTML autocontenido** con los diagramas en Mermaid:

- **Publicado:** <https://eolunas.github.io/FunEvents/>
- **En el repositorio:** [`docs/index.html`](docs/index.html) — se abre directamente en el navegador, sin servidor.
