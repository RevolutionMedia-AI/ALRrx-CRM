# Slice Backend — Documentación Técnica

> **Stack:** .NET 8 · ASP.NET Core Web API · EPPlus · BCrypt · JWT · Resend API  
> **Arquitectura:** Clean Architecture en 4 capas  
> **Almacenamiento:** En memoria (ConcurrentDictionary) — preparado para swap a DB

---

## Tabla de contenidos

1. [Visión general](#1-visión-general)
2. [Estructura del proyecto](#2-estructura-del-proyecto)
3. [Arquitectura por capas](#3-arquitectura-por-capas)
4. [Cómo correr el proyecto](#4-cómo-correr-el-proyecto)
5. [Configuración (appsettings)](#5-configuración-appsettings)
6. [Autenticación y autorización](#6-autenticación-y-autorización)
7. [API — Endpoints completos](#7-api--endpoints-completos)
8. [Flujo de procesamiento de archivos](#8-flujo-de-procesamiento-de-archivos)
9. [Modelos de datos](#9-modelos-de-datos)
10. [Mejoras y optimizaciones aplicadas](#10-mejoras-y-optimizaciones-aplicadas)
11. [Decisiones de diseño](#11-decisiones-de-diseño)
12. [Próximos pasos hacia producción](#12-próximos-pasos-hacia-producción)

---

## 1. Visión general

**Slice Backend** es la API REST que potencia la plataforma Slice CRM. Su función principal es:

1. Recibir archivos Excel diarios de métricas de call-center y e-commerce.
2. Parsear, fusionar y exportar esos archivos como reportes consolidados.
3. Permitir a los usuarios ver, editar y exportar sus reportes.
4. Enviar reportes por email (con el XLSX adjunto) vía Resend.

El backend no tiene base de datos persistente en esta versión — todos los datos viven en memoria y se pierden al reiniciar. La arquitectura ya está preparada para conectar una DB real simplemente cambiando las implementaciones de los repositorios.

---

## 2. Estructura del proyecto

```
slice-backend/
│
├── Slice.sln
│
├── Slice.Domain/                   ← Núcleo: entidades y contratos
│   ├── Entities/
│   │   ├── SliceUser.cs            Entidad de usuario
│   │   ├── SliceReport.cs          Reporte consolidado + filas de las 3 secciones
│   │   └── ProcessingJob.cs        Seguimiento de un batch de procesamiento
│   ├── Enums/
│   │   └── JobStatus.cs            Estados del pipeline: Pending→Processing→Completed
│   └── Interfaces/
│       ├── IUserRepository.cs
│       ├── IJobRepository.cs
│       └── IReportRepository.cs
│
├── Slice.Application/              ← Casos de uso: interfaces, DTOs, validaciones
│   ├── DTOs/
│   │   ├── AuthDtos.cs             LoginRequest, RegisterRequest, LoginResponse, etc.
│   │   ├── ReportDtos.cs           ReportSummaryDto, ChartDataDto, Patch DTOs
│   │   └── UploadDtos.cs           UploadJobResponse, JobStatusResponse
│   ├── Interfaces/
│   │   ├── IAuthService.cs         Hash de contraseña + generación de JWT
│   │   ├── IEmailService.cs        Envío de emails
│   │   ├── IExcelParserService.cs  Lectura de archivos Excel
│   │   ├── IReportMergeService.cs  Fusión y exportación de reportes
│   │   ├── IFileProcessingOrchestrator.cs  Pipeline completo
│   │   └── IZipExtractionService.cs        Extracción de ZIPs
│   ├── Validators/
│   │   ├── LoginRequestValidator.cs
│   │   └── RegisterRequestValidator.cs
│   └── DependencyInjection/
│       └── ApplicationRegistration.cs
│
├── Slice.Infrastructure/           ← Implementaciones concretas
│   ├── Auth/
│   │   └── JwtAuthService.cs       BCrypt + HS256 JWT
│   ├── Email/
│   │   └── ResendEmailService.cs   Integración con Resend API
│   ├── Excel/
│   │   ├── ExcelParserService.cs   Lectura de .xlsx/.xls/.xlsm con EPPlus
│   │   └── ReportMergeService.cs   Fusión de reportes + exportación XLSX/CSV
│   ├── Processing/
│   │   └── FileProcessingOrchestrator.cs  Pipeline async con Parallel.ForEachAsync
│   ├── Repositories/
│   │   ├── InMemoryUserRepository.cs
│   │   ├── InMemoryJobRepository.cs
│   │   └── InMemoryReportRepository.cs
│   ├── Seeding/
│   │   └── UserSeedService.cs      Carga usuarios desde appsettings al arrancar
│   ├── Zip/
│   │   └── ZipExtractionService.cs Extracción paralela con ConcurrentBag
│   └── DependencyInjection/
│       └── InfrastructureRegistration.cs
│
└── Slice.Api/                      ← Capa HTTP: controllers y middleware
    ├── Controllers/
    │   ├── AuthController.cs
    │   ├── EmailController.cs
    │   ├── FileUploadController.cs
    │   └── ReportsController.cs
    ├── Middleware/
    │   ├── ExceptionMiddleware.cs      Manejo global de errores → JSON
    │   └── SliceEmailGuardMiddleware.cs  Whitelist de emails
    ├── Program.cs
    └── appsettings.json
```

---

## 3. Arquitectura por capas

```
┌─────────────────────────────────────────┐
│              Slice.Api                  │  Controllers · Middleware · Program.cs
│         (HTTP / ASP.NET Core)           │  Depende de: Application + Infrastructure
└────────────────────┬────────────────────┘
                     │
┌────────────────────▼────────────────────┐
│          Slice.Application              │  DTOs · Interfaces · Validators
│        (Casos de uso)                   │  Depende de: Domain
└────────────────────┬────────────────────┘
                     │
┌────────────────────▼────────────────────┐
│          Slice.Infrastructure           │  Auth · Email · Excel · Repos · Zip
│       (Implementaciones externas)       │  Depende de: Application + Domain
└────────────────────┬────────────────────┘
                     │
┌────────────────────▼────────────────────┐
│             Slice.Domain                │  Entidades · Enums · Interfaces
│          (Núcleo del negocio)           │  Sin dependencias externas
└─────────────────────────────────────────┘
```

**Regla clave:** Las capas internas (Domain, Application) no conocen a las externas. Los controllers y servicios de Infrastructure se comunican a través de interfaces, lo que permite cambiar cualquier implementación (p.ej. pasar de in-memory a PostgreSQL) sin tocar la lógica de negocio.

---

## 4. Cómo correr el proyecto

### Requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2022+ o VS Code con extensión C#

### Pasos

```bash
# 1. Ir a la carpeta del proyecto API
cd slice-backend/Slice.Api

# 2. Restaurar paquetes NuGet
dotnet restore

# 3. Configurar credenciales en appsettings.Development.json
#    (ver sección 5 para los campos requeridos)

# 4. Correr el servidor
dotnet run
```

La API queda disponible en `https://localhost:PUERTO`.  
El puerto exacto se define en `Properties/launchSettings.json`.

### Swagger UI (solo en Development)

Navegar a `https://localhost:PUERTO/swagger` para explorar y probar todos los endpoints de forma interactiva.

### Health check

```
GET /health
→ { "status": "healthy", "service": "Slice API" }
```

---

## 5. Configuración (appsettings)

### appsettings.json (valores base)

```json
{
  "Jwt": {
    "Key":      "clave-secreta-minimo-32-caracteres",
    "Issuer":   "SliceApi",
    "Audience": "SliceFrontend"
  },
  "Google": {
    "ClientId": "tu-client-id.apps.googleusercontent.com"
  },
  "Resend": {
    "ApiKey":    "",
    "FromEmail": "Slice Reports <reports@tudominio.com>"
  },
  "Slice": {
    "AllowedDomains": ["revolutionmedia.ai"],
    "AllowedEmails":  ["usuario@gmail.com"],
    "Users": [
      { "Email": "admin@empresa.com", "FullName": "Nombre Admin", "Role": "Admin" },
      { "Email": "viewer@empresa.com", "FullName": "Nombre Viewer", "Role": "Viewer" }
    ]
  }
}
```

### appsettings.Development.json (overrides locales)

```json
{
  "Resend": {
    "ApiKey": "re_xxxxxxxxxxxxxxxxxxxx"
  }
}
```

### Descripción de cada sección

| Sección | Campo | Descripción |
|---|---|---|
| `Jwt` | `Key` | Clave secreta HS256. Mínimo 32 caracteres. |
| `Jwt` | `Issuer` / `Audience` | Deben coincidir con los valores que usa el frontend al validar. |
| `Resend` | `ApiKey` | API key de [resend.com](https://resend.com). |
| `Resend` | `FromEmail` | Remitente de los correos. Debe estar verificado en Resend. |
| `Slice` | `AllowedDomains` | Dominios de email que pueden acceder (p.ej. `"empresa.com"`). |
| `Slice` | `AllowedEmails` | Emails individuales que pueden acceder aunque no sean del dominio. |
| `Slice` | `Users[]` | Lista de usuarios pre-cargados al arrancar. Roles: `Admin`, `Supervisor`, `Viewer`. |

---

## 6. Autenticación y autorización

### Método 1 — Google OAuth

```
Frontend                          Backend                          Google
   │                                 │                               │
   │── access_token de Google ───────▶│                               │
   │                                 │── GET /oauth2/v3/userinfo ────▶│
   │                                 │◀── { email, name } ───────────│
   │                                 │                               │
   │                          Verifica whitelist                     │
   │                          Crea usuario si es nuevo               │
   │◀── JWT propio (8 horas) ────────│                               │
```

### Método 2 — Email y contraseña

```
POST /api/auth/login
Body: { "email": "...", "password": "..." }
→ JWT si las credenciales son válidas
```

### Usar el JWT en cada petición

```
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### Roles

| Rol | Permisos |
|---|---|
| `Admin` | Todo: ve todos los reportes, edita filas, registra usuarios |
| `Supervisor` | Ve sus propios reportes, puede subir archivos |
| `Viewer` | Ve sus propios reportes, puede subir archivos |

### Whitelist de emails

Dos capas de seguridad:
1. **Middleware `SliceEmailGuardMiddleware`** — rechaza con `403` cualquier JWT cuyo email no esté en `AllowedEmails` ni pertenezca a `AllowedDomains`.
2. **Validación en `AuthController`** — verifica la whitelist antes de emitir el JWT.

---

## 7. API — Endpoints completos

### Base URL: `https://localhost:PUERTO/api`

---

### Autenticación — `/auth`

#### `POST /auth/google`
Login con Google OAuth. No requiere JWT previo.

**Body:**
```json
{ "accessToken": "ya29.a0AfH..." }
```
**Respuesta 200:**
```json
{
  "token":     "eyJ...",
  "email":     "usuario@empresa.com",
  "fullName":  "Juan Pérez",
  "role":      "Viewer",
  "expiresAt": "2026-06-04T20:00:00Z"
}
```

---

#### `POST /auth/login`
Login con email y contraseña.

**Body:**
```json
{ "email": "admin@empresa.com", "password": "MiPassword1" }
```
**Respuesta 200:** Mismo formato que `/auth/google`.

---

#### `POST /auth/register` *(solo Admin)*
Registra un nuevo usuario con contraseña.

**Body:**
```json
{
  "email":    "nuevo@empresa.com",
  "password": "Password1",
  "fullName": "Nuevo Usuario",
  "role":     "Viewer"
}
```
**Respuesta 201:** Datos del usuario creado.

---

#### `GET /auth/me` *(requiere JWT)*
Retorna el perfil del usuario autenticado.

**Respuesta 200:**
```json
{
  "id":        "guid-del-usuario",
  "email":     "usuario@empresa.com",
  "fullName":  "Juan Pérez",
  "role":      "Viewer",
  "createdAt": "2026-06-01T10:00:00Z"
}
```

---

### Subida de archivos — `/fileupload`

#### `POST /fileupload/excel` *(requiere JWT)*
Sube hasta **12 archivos Excel** (`.xlsx`, `.xls`, `.xlsm`) en una sola petición.

- **Content-Type:** `multipart/form-data`
- **Límite por archivo:** 50 MB
- **Límite total de la petición:** 600 MB
- El procesamiento ocurre en **background** — la respuesta es inmediata.

**Respuesta 202:**
```json
{
  "jobId":     "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "fileCount": 5,
  "status":    "Processing"
}
```

---

#### `POST /fileupload/zip` *(requiere JWT)*
Sube un único archivo `.zip` que contenga archivos Excel adentro.

- **Límite:** 200 MB
- El servidor extrae los Excel y los procesa igual que el endpoint anterior.

**Respuesta 202:**
```json
{
  "jobId":     "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "fileCount": 1,
  "status":    "Extracting"
}
```

---

#### `GET /fileupload/status/{jobId}` *(requiere JWT)*
Consulta el estado actual de un job. Hacer polling hasta que `status` sea `Completed` o `Failed`.

**Respuesta 200:**
```json
{
  "jobId":          "3fa85f64...",
  "status":         "Completed",
  "totalFiles":     5,
  "processedFiles": 5,
  "errorMessage":   null,
  "reportId":       "guid-del-reporte",
  "createdAt":      "2026-06-04T12:00:00Z",
  "completedAt":    "2026-06-04T12:00:45Z"
}
```

**Valores posibles de `status`:**

| Valor | Significado |
|---|---|
| `Pending` | Job creado, aún no empezó |
| `Extracting` | Extrayendo archivos del ZIP |
| `Processing` | Parseando archivos Excel |
| `Merging` | Fusionando y exportando |
| `Completed` | Listo — usar `reportId` para ver el reporte |
| `Failed` | Error — ver `errorMessage` |

---

### Reportes — `/reports`

#### `GET /reports` *(requiere JWT)*
Lista de reportes. Admins ven todos; otros usuarios solo ven los propios.

**Respuesta 200:**
```json
[
  {
    "id":             "guid-reporte",
    "reportDate":     "2026-06-04T00:00:00Z",
    "generatedAt":    "2026-06-04T12:00:45Z",
    "podCount":       6,
    "agentCount":     42,
    "mergedCsvPath":  "/tmp/slice/exports/Slice_Report_xxx.csv",
    "mergedXlsxPath": "/tmp/slice/exports/Slice_Report_xxx.xlsx"
  }
]
```

---

#### `GET /reports/{reportId}` *(requiere JWT)*
Reporte completo con todas las filas de las 3 secciones.

**Respuesta 200:** Objeto `SliceReport` completo (ver sección 9).

---

#### `GET /reports/{reportId}/charts/global` *(requiere JWT)*
Datos del Daily Global formateados para renderizar gráficas de barras/líneas.

**Respuesta 200:**
```json
{
  "label": "Daily Global",
  "series": [
    { "name": "Queued",      "values": [120, 98, 145, 87, 110, 132] },
    { "name": "Handled",     "values": [115, 94, 140, 82, 108, 128] },
    { "name": "Missed",      "values": [5, 4, 5, 5, 2, 4] },
    { "name": "Transferred", "values": [8, 6, 9, 7, 5, 8] },
    { "name": "Conv %",      "values": [12.5, 11.2, 13.8, 10.9, 12.1, 13.0] },
    { "name": "Orders",      "values": [95, 78, 112, 67, 89, 104] }
  ]
}
```

---

#### `GET /reports/{reportId}/export/{format}` *(requiere JWT)*
Descarga el reporte exportado. El archivo se sirve en **streaming** sin cargarlo en memoria.

- `format` puede ser `xlsx` o `csv`
- El nombre del archivo descargado será: `Slice_Report_YYYYMMDD.{ext}`

---

#### `PATCH /reports/{reportId}/global/{pod}` *(solo Admin)*
Edita campos de una fila del Daily Global. Solo se actualizan los campos enviados (patch parcial).

**Body (todos los campos son opcionales):**
```json
{
  "queued":             125,
  "handled":            120,
  "missedCalls":        5,
  "transferredCalls":   8,
  "pctQueued":          92.5,
  "pctHandled":         88.3,
  "pctMissed":          4.2,
  "pctTransferred":     6.1,
  "convPct":            12.8,
  "orderCount":         98,
  "refundedOrders":     3,
  "pctOrdersWithErrors": 1.5
}
```

---

#### `PATCH /reports/{reportId}/agent/{agentEmail}` *(solo Admin)*
Edita campos de una fila del Daily Agent por email del agente.

**Body (todos opcionales):**
```json
{
  "hc":               45,
  "tc":               50,
  "numberOfHolds":    3,
  "avgHoldTime":      28.5,
  "asa":              12.3,
  "aht":              240.0,
  "acw":              45.0,
  "pctContactsOnHold": 6.7,
  "pctSLUnder15Sec":  78.5,
  "pctTransfers":     4.2,
  "shift":            "Morning",
  "supervisorName":   "María López"
}
```

---

#### `PATCH /reports/{reportId}/shop/{shopName}` *(solo Admin)*
Edita campos de una fila del Shop Daily por nombre del shop.

**Body (todos opcionales):**
```json
{
  "totalOrders":    250,
  "refundedOrders": 8,
  "errorRate":      3.2,
  "conversionRate": 14.5
}
```

---

### Email — `/email`

#### `POST /email/send-report` *(requiere JWT)*
Envía el reporte por correo electrónico con el XLSX adjunto.

**Body:**
```json
{
  "toEmail":  "jefe@empresa.com",
  "reportId": "guid-del-reporte",
  "subject":  "Reporte diario"
}
```

> **Nota:** el campo `subject` actualmente es ignorado; el asunto se genera automáticamente como `"Slice Daily Report — YYYY-MM-DD"`.

**Respuesta 200:**
```json
{ "message": "Report sent to jefe@empresa.com" }
```

---

## 8. Flujo de procesamiento de archivos

```
Usuario sube archivos
         │
         ▼
┌─────────────────────────────────────────────────────────────┐
│  FileUploadController                                        │
│  • Valida extensión y tamaño                                 │
│  • Llama a IFileProcessingOrchestrator.EnqueueAsync()       │
│  • Devuelve 202 + jobId inmediatamente                       │
└────────────────────────────┬────────────────────────────────┘
                             │ fire-and-forget background task
                             ▼
┌─────────────────────────────────────────────────────────────┐
│  FileProcessingOrchestrator                                  │
│                                                             │
│  1. Guarda streams en archivos temp (%TEMP%/slice/{jobId}/) │
│  2. Status → Processing                                     │
│                                                             │
│  3. Parallel.ForEachAsync (max 12 en paralelo):             │
│     ┌───────────────────────────────────┐                   │
│     │  ExcelParserService.ParseAsync()  │                   │
│     │  • Abre el .xlsx con EPPlus       │                   │
│     │  • Busca secciones por header     │                   │
│     │  • Extrae Daily Global            │                   │
│     │  • Extrae Daily Agent             │                   │
│     │  • Extrae Shop Daily              │                   │
│     │  • Retorna SliceReport parcial    │                   │
│     └───────────────────────────────────┘                   │
│     • Elimina archivo temp al terminar cada uno             │
│     • Actualiza job.ProcessedFiles atómicamente             │
│                                                             │
│  4. Status → Merging                                        │
│                                                             │
│  5. ReportMergeService.Merge():                             │
│     • Agrupa Daily Global por Pod → suma volúmenes,         │
│       promedia porcentajes                                  │
│     • Agrupa Daily Agents por (Email+Pod) → suma HC/TC,     │
│       promedia tiempos                                      │
│     • Agrupa Shop Daily por ShopName → suma órdenes,        │
│       promedia tasas                                        │
│                                                             │
│  6. ReportMergeService.ExportXlsxAsync()                    │
│     → %TEMP%/slice/exports/Slice_Report_{id}.xlsx           │
│                                                             │
│  7. ReportMergeService.ExportCsvAsync()                     │
│     → %TEMP%/slice/exports/Slice_Report_{id}.csv            │
│                                                             │
│  8. Guarda SliceReport en IReportRepository                 │
│  9. Status → Completed + ReportId                           │
└─────────────────────────────────────────────────────────────┘
```

### Estructura esperada del Excel

El parser detecta las secciones por palabras clave en la columna A:

```
Fila N:    "Daily Global"           ← header de sección
Fila N+1:  (encabezados de columnas)
Fila N+2+: Pod | Queued | Handled | ...
           ES-POD-01 | 120 | 115 | ...
           (termina cuando el Pod no empieza con "ES-")

Fila M:    "Daily Agent"
Fila M+1:  (encabezados)
Fila M+2+: POD | | ES-POD-01          ← fila de pod
           Agent | HC | TC | ...       ← encabezados de agente
           agente@empresa.com | 45 | 50 | ...

Fila K:    "Shop Daily"
Fila K+1:  (encabezados)
Fila K+2+: ShopName | TotalOrders | ...
```

---

## 9. Modelos de datos

### SliceUser

| Campo | Tipo | Descripción |
|---|---|---|
| `Id` | `Guid` | Identificador único |
| `Email` | `string` | Email (lowercase, único) |
| `PasswordHash` | `string` | Hash BCrypt. Vacío para usuarios Google OAuth |
| `FullName` | `string` | Nombre completo |
| `Role` | `string` | `Admin` / `Supervisor` / `Viewer` |
| `IsActive` | `bool` | Si es false, no puede autenticarse |
| `CreatedAt` | `DateTime` | Fecha de creación (UTC) |

### SliceReport

| Campo | Tipo | Descripción |
|---|---|---|
| `Id` | `string` | GUID como string, usado en las rutas de API |
| `JobId` | `string` | ID del job que lo generó |
| `ReportDate` | `DateTime` | Fecha de los datos (UTC midnight) |
| `GeneratedAt` | `DateTime` | Cuándo se generó el reporte |
| `GeneratedByEmail` | `string` | Email del usuario que subió los archivos |
| `DailyGlobal` | `List<DailyGlobalRow>` | Métricas por Pod |
| `DailyAgents` | `List<DailyAgentRow>` | Métricas por agente |
| `ShopDaily` | `List<ShopDailyRow>` | Métricas por tienda |
| `MergedXlsxPath` | `string?` | Ruta al XLSX exportado |
| `MergedCsvPath` | `string?` | Ruta al CSV exportado |

### DailyGlobalRow (por Pod)

| Campo | Tipo | Descripción |
|---|---|---|
| `Pod` | `string` | Identificador del pod (ej. `ES-POD-01`) |
| `Queued` | `int` | Contactos en cola |
| `Handled` | `int` | Contactos atendidos |
| `MissedCalls` | `int` | Llamadas perdidas |
| `TransferredCalls` | `int` | Llamadas transferidas |
| `PctQueued` | `double` | % en cola |
| `PctHandled` | `double` | % atendidos |
| `PctMissed` | `double` | % perdidos |
| `PctTransferred` | `double` | % transferidos |
| `ConvPct` | `double` | % conversión (contactos → órdenes) |
| `OrderCount` | `int` | Órdenes generadas |
| `RefundedOrders` | `int` | Órdenes reembolsadas |
| `PctOrdersWithErrors` | `double` | % órdenes con errores |

### DailyAgentRow (por agente)

| Campo | Tipo | Descripción |
|---|---|---|
| `Pod` | `string` | Pod al que pertenece |
| `SupervisorName` | `string` | Nombre del supervisor |
| `AgentEmail` | `string` | Email del agente |
| `HC` | `int` | Handled Contacts |
| `TC` | `int` | Total Contacts |
| `NumberOfHolds` | `int` | Número de retenciones |
| `AvgHoldTime` | `double` | Tiempo promedio de retención (seg) |
| `ASA` | `double` | Average Speed of Answer (seg) |
| `AHT` | `double` | Average Handle Time (seg) |
| `ACW` | `double` | After-Call Work time (seg) |
| `PctContactsOnHold` | `double` | % contactos en retención |
| `PctSLUnder15Sec` | `double` | % respondidos en menos de 15 seg (Service Level) |
| `PctTransfers` | `double` | % transferencias |
| `Shift` | `string` | Turno (Morning / Afternoon / etc.) |

### ShopDailyRow (por tienda)

| Campo | Tipo | Descripción |
|---|---|---|
| `ShopName` | `string` | Nombre de la tienda |
| `TotalOrders` | `int` | Total de órdenes |
| `RefundedOrders` | `int` | Órdenes reembolsadas |
| `ErrorRate` | `double` | Tasa de error (%) |
| `ConversionRate` | `double` | Tasa de conversión (%) |

### ProcessingJob

| Campo | Tipo | Descripción |
|---|---|---|
| `Id` | `Guid` | Identificador del job |
| `Status` | `JobStatus` | Estado actual del pipeline |
| `CreatedByEmail` | `string` | Email del usuario que inició la subida |
| `CreatedAt` | `DateTime` | Cuándo se creó (UTC) |
| `CompletedAt` | `DateTime?` | Cuándo terminó (UTC), o null si sigue en progreso |
| `TotalFiles` | `int` | Total de Excel a procesar |
| `ProcessedFiles` | `int` | Excel ya procesados (actualizado en tiempo real) |
| `ErrorMessage` | `string?` | Detalle del error si Status = Failed |
| `SourceFiles` | `List<string>` | Rutas temp de los archivos mientras se procesan |
| `ReportId` | `string?` | ID del reporte generado al completar |

---

## 10. Mejoras y optimizaciones aplicadas

### Bugs corregidos

| Problema | Impacto | Solución |
|---|---|---|
| **Race condition en `ProcessedFiles++`** | Contador incorrecto con 12 archivos en paralelo | `Interlocked.Increment(ref processedCount)` |
| **`new HttpClient()` por request en Google OAuth** | Agotamiento de sockets bajo carga | `IHttpClientFactory.CreateClient("Google")` |
| **Sin control de acceso en `GET /charts/global`** | Cualquier usuario autenticado veía gráficas ajenas | Mismo check de pertenencia que `GetById` |

### Optimizaciones de rendimiento

| Qué | Resultado |
|---|---|
| `File.ReadAllBytesAsync()` → `PhysicalFile()` para descargas | Streaming directo — no carga el XLSX/CSV en RAM del servidor |
| Channel + SemaphoreSlim → `Parallel.ForEachAsync` | Código 60% más corto, misma concurrencia máxima |
| Config leída en cada request → `HashSet<string>` cacheado en constructor | O(1) lookup para la whitelist de emails vs O(n) con LINQ cada vez |
| `lock(List<T>)` en extracción ZIP → `ConcurrentBag<string>` | Lock-free thread safety |
| Licencia EPPlus en cada constructor → una sola vez en `Program.cs` | Evita llamadas estáticas repetidas al inicializar servicios |

### Mejoras de arquitectura

| Qué | Por qué importa |
|---|---|
| Creada interfaz `IUserRepository` en Domain | `AuthController` ya no depende de una clase concreta de Infrastructure (violación del principio DIP) |
| `InMemoryUserRepository` registrada como `IUserRepository` en DI | Se puede cambiar por implementación con DB sin modificar ningún controller |
| Patch DTOs movidos a `Slice.Application/DTOs/ReportDtos.cs` | Estaban definidos al fondo del controller, fuera de la capa correcta |
| Cliente HTTP `"Google"` registrado en `InfrastructureRegistration` | Centraliza la configuración HTTP; el controller no necesita saber la URL base de Google |

---

## 11. Decisiones de diseño

### ¿Por qué almacenamiento en memoria?

El proyecto está en fase de prototipo / MVP. Los repositorios `InMemory*` son thread-safe con `ConcurrentDictionary` y la API async ya está preparada para que el día de mañana se puedan agregar repositorios con Entity Framework o Dapper **sin cambiar ni una línea de los controllers** — solo hay que registrar la nueva implementación en el DI.

### ¿Por qué fire-and-forget para el procesamiento?

Procesar 12 archivos Excel grandes puede tardar 30–60 segundos. Mantener la conexión HTTP abierta ese tiempo es frágil (timeouts de proxies, Nginx, balanceadores). El patrón `EnqueueAsync → jobId → polling` es el estándar para operaciones largas en REST APIs.

### ¿Por qué BCrypt con work factor 12?

Work factor 12 produce un hash que tarda ~250ms en generarse. Eso es completamente imperceptible para el usuario en un login, pero hace que un ataque de diccionario sea miles de veces más lento que con MD5 o SHA-256 sin salt.

### ¿Por qué ClockSkew = Zero en JWT?

Con `ClockSkew = TimeSpan.Zero` el token expira exactamente en el `ExpiresAt` que se le comunica al frontend, sin margen adicional. Esto hace que la experiencia sea predecible: si el frontend guarda el `expiresAt` del login y lo usa para saber cuándo renovar, los números coinciden exactamente.

---

## 12. Próximos pasos hacia producción

### Base de datos
Crear implementaciones de `IUserRepository`, `IJobRepository`, `IReportRepository` con Entity Framework Core + PostgreSQL (o SQL Server). Solo se cambia el registro en `InfrastructureRegistration.cs`:

```csharp
// Antes (in-memory):
services.AddSingleton<IUserRepository, InMemoryUserRepository>();

// Después (base de datos):
services.AddScoped<IUserRepository, EfUserRepository>();
```

### Almacenamiento de archivos exportados
Los archivos XLSX/CSV actualmente se guardan en `%TEMP%`. En producción usar Azure Blob Storage o AWS S3 y guardar la URL en el reporte.

### Variables de entorno en producción
Las claves sensibles (`Jwt:Key`, `Resend:ApiKey`) deben provenir de variables de entorno o Azure Key Vault — nunca hardcodeadas en archivos de configuración que se suben al repositorio.

### CORS
Cambiar la política de CORS de `AllowAnyOrigin` al dominio específico del frontend:

```csharp
p.WithOrigins("https://app.tudominio.com")
 .AllowAnyMethod()
 .AllowAnyHeader();
```

### Renovación de JWT
Implementar refresh tokens para que el usuario no tenga que volver a hacer login cada 8 horas.

### Logging estructurado
Agregar Serilog con salida a Application Insights o Elastic para tener logs en producción con correlación de requests.

---

*Documentación generada para Slice Backend — Junio 2026*
