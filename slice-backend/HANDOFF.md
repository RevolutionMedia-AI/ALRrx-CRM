# Slice Backend — Guía de Traspaso

> Hola, soy el desarrollador anterior. Este documento es para que puedas arrancar
> sin perder tiempo. Te cuento cómo está todo, qué funciona, qué falta y dónde
> están los detalles que no son obvios a primera vista.

---

## Lo primero: cómo levantarlo en 5 minutos

```bash
cd slice-backend/Slice.Api
dotnet restore
dotnet run
```

Entra a `https://localhost:PUERTO/swagger` y ya puedes probar todos los endpoints.
El puerto lo ves en `Properties/launchSettings.json`.

Lo único que necesitas configurar antes de que funcione del todo es el **API key de Resend**
(para los emails). Ponlo en `appsettings.Development.json`:

```json
{
  "Resend": {
    "ApiKey": "re_xxxxxxxxxxxxxxxx"
  }
}
```

Para todo lo demás (JWT, Google OAuth, usuarios) ya hay valores por defecto en `appsettings.json`.

---

## Estado actual del proyecto

### ✅ Está hecho y funciona

- Autenticación con **Google OAuth** y con **email + contraseña**
- **Whitelist de emails/dominios** — solo entran los que están en `appsettings.json → Slice:AllowedEmails`
- Subida de hasta **12 archivos Excel** simultáneos (`.xlsx`, `.xls`, `.xlsm`)
- Subida de **ZIP** con Excel adentro
- Procesamiento en **background** (el usuario no espera, hace polling)
- **Parser de Excel** — detecta las secciones Daily Global, Daily Agent y Shop Daily
- **Merge de múltiples reportes** — suma volúmenes, promedia porcentajes
- **Exportación a XLSX y CSV** con estilos
- **Envío de email** con el XLSX adjunto vía Resend
- Edición de filas individuales de un reporte (solo Admin)
- Roles: `Admin`, `Supervisor`, `Viewer`
- Swagger UI con autenticación JWT

### ❌ No está hecho / pendiente

- **Base de datos real** — todo vive en memoria, se pierde al reiniciar (más abajo te explico cómo conectar una)
- **Refresh tokens** — el JWT dura 8 horas y no hay renovación automática
- **Almacenamiento de archivos en la nube** — los XLSX/CSV exportados van a `%TEMP%`, se pierden al reiniciar el servidor
- **Tests** — no hay ninguno. Sería bueno empezar con los parsers de Excel
- **CORS en producción** — actualmente está abierto a cualquier origen (`AllowAnyOrigin`). Hay que cerrarlo al dominio del frontend

---

## La cosa más importante que debes saber

**Todo el almacenamiento es en memoria.** Cada vez que reinicias el servidor, todos los usuarios, reportes y jobs desaparecen. Los usuarios se re-crean desde `appsettings.json → Slice:Users` al arrancar, pero los reportes no.

Esto es intencional para el MVP. La arquitectura ya está preparada para conectar una DB. Te explico cómo en la sección de "Próximos pasos".

---

## Cómo está organizado el código

```
Slice.Domain        ← Entidades y contratos (no depende de nada)
Slice.Application   ← DTOs, interfaces, validaciones
Slice.Infrastructure ← Implementaciones reales (Excel, auth, email, repos)
Slice.Api           ← Controllers, middleware, Program.cs
```

La regla es simple: **los controllers nunca importan nada de Infrastructure directamente**.
Todo pasa por interfaces. Eso es lo que permite cambiar el almacenamiento sin tocar los controllers.

---

## Los archivos que más vas a tocar

| Archivo | Para qué |
|---|---|
| `appsettings.json` | Cambiar usuarios, dominios permitidos, configurar JWT |
| `Slice.Api/Controllers/` | Agregar o modificar endpoints |
| `Slice.Infrastructure/Excel/ExcelParserService.cs` | Si cambia el formato de los Excel |
| `Slice.Infrastructure/Excel/ReportMergeService.cs` | Cambiar cómo se fusionan o exportan |
| `Slice.Infrastructure/Repositories/` | Aquí van los repos con DB cuando los hagas |
| `Slice.Infrastructure/DependencyInjection/InfrastructureRegistration.cs` | Registrar nuevos servicios |

---

## Cosas no obvias que debes saber

### 1. El parser de Excel es frágil con el formato

El `ExcelParserService` detecta las secciones buscando las palabras **"Daily Global"**, **"Daily Agent"** y **"Shop Daily"** en la columna A. Si el Excel del cliente cambia esas palabras o mueve las columnas, el parser no va a encontrar nada y va a retornar `null` silenciosamente.

Los Pods del Daily Global se detectan porque **empiezan con "ES-"**. Si eso cambia, falla.

Los agentes se detectan porque **tienen "@" en la columna A** (son emails).

Si el cliente te dice "no aparecen datos", lo primero que revisas es el formato del Excel.

### 2. El procesamiento es fire-and-forget

Cuando el usuario sube archivos, el controller responde **inmediatamente** con un `jobId`.
El procesamiento ocurre en background. Si el servidor se cae mientras procesa, el job queda en un estado intermedio en memoria y desaparece al reiniciar.

Con una DB real esto se resuelve sencillo: al arrancar, busca jobs con status `Processing` y los marca como `Failed`.

### 3. Los archivos exportados viven en `%TEMP%/slice/exports/`

En Windows suele ser `C:\Users\<usuario>\AppData\Local\Temp\slice\exports\`.
En Linux (para deploy) es `/tmp/slice/exports/`.

No los borres manualmente mientras el servidor está corriendo — el endpoint de descarga apunta a esas rutas. Cuando migres a producción, muévelos a Azure Blob o S3.

### 4. Los usuarios se seed desde appsettings

Al arrancar, `UserSeedService` lee `Slice:Users[]` y crea esos usuarios en memoria.
Si agregas un usuario nuevo ahí y reinicias, aparece. Si alguien se registró vía API (solo Admin puede), ese usuario **no persiste** al reiniciar.

### 5. La whitelist de emails tiene dos niveles

- `Slice:AllowedEmails` — emails exactos que pueden entrar
- `Slice:AllowedDomains` — cualquier email de ese dominio puede entrar

El `SliceEmailGuardMiddleware` rechaza con 403 cualquier JWT cuyo email no pase esta validación, aunque el token sea válido. Esto es una segunda capa de seguridad encima de la autenticación.

### 6. Hay dos registros del UserRepository en DI

```csharp
services.AddSingleton<InMemoryUserRepository>();
services.AddSingleton<IUserRepository>(sp => sp.GetRequiredService<InMemoryUserRepository>());
```

El primero es para que `UserSeedService` pueda inyectarlo por tipo concreto (necesidad técnica del hosted service). El segundo es para que los controllers lo usen por interfaz. Ambos apuntan a la **misma instancia**. No lo toques sin entender esto.

---

## Cómo agregar un endpoint nuevo

1. Agrega el método en el controller correspondiente (o crea uno nuevo en `Slice.Api/Controllers/`)
2. Si necesitas lógica de negocio nueva, crea una interfaz en `Slice.Application/Interfaces/`
3. Implementa la interfaz en `Slice.Infrastructure/`
4. Registra el servicio en `Slice.Infrastructure/DependencyInjection/InfrastructureRegistration.cs`
5. El controller inyecta la **interfaz**, nunca la implementación

---

## Cómo conectar una base de datos (el paso más importante que te falta)

El proyecto ya está diseñado para esto. Solo tienes que hacer 3 cosas:

**Paso 1** — Crea los repos con EF Core (o Dapper, lo que prefieras):

```csharp
// Slice.Infrastructure/Repositories/EfReportRepository.cs
public class EfReportRepository : IReportRepository
{
    private readonly SliceDbContext _db;
    // implementar GetByIdAsync, SaveAsync, etc.
}
```

**Paso 2** — Cambia el registro en `InfrastructureRegistration.cs`:

```csharp
// Borrar esto:
services.AddSingleton<IReportRepository, InMemoryReportRepository>();

// Poner esto:
services.AddScoped<IReportRepository, EfReportRepository>();
```

**Paso 3** — Los controllers y servicios no cambian nada. Ya funcionan con la interfaz.

Lo mismo aplica para `IJobRepository` y `IUserRepository`.

---

## Variables de entorno para producción

**Nunca** subas las claves al repositorio. En producción usa variables de entorno:

| Variable de entorno | Equivalente en appsettings |
|---|---|
| `Jwt__Key` | `Jwt:Key` |
| `Resend__ApiKey` | `Resend:ApiKey` |
| `Google__ClientId` | `Google:ClientId` |

ASP.NET Core las lee automáticamente — el doble guión bajo `__` es el separador de sección.

---

## Paquetes NuGet que se usan y para qué

| Paquete | Para qué |
|---|---|
| `EPPlus` | Leer y escribir archivos Excel |
| `BCrypt.Net-Next` | Hash de contraseñas |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Validar JWT en cada request |
| `System.IdentityModel.Tokens.Jwt` | Generar los JWT |
| `FluentValidation` | Validar los DTOs de login y registro |
| `Microsoft.Extensions.Http` | `IHttpClientFactory` para llamadas HTTP externas |

---

## Si algo no compila

```bash
dotnet restore   # restaura los paquetes
dotnet build     # muestra errores con línea exacta
```

El proyecto debe compilar siempre con **0 errores y 0 warnings**. Si ves warnings, arréglales antes de seguir.

---

## Contacto

Si tienes dudas del código, toda la lógica está documentada con `<summary>` en cada clase y método. El archivo `DOCUMENTACION.md` tiene la referencia completa de la API y la arquitectura.

Suerte. 🤝
