# Дашборд

Jobby поставляется с необязательным дашбордом для просмотра и управления фоновыми задачами в любом ASP.NET Core-хосте: Web API, MVC или Blazor.

Дашборд работает как одностраничное приложение на **Blazor WebAssembly**: хост раздаёт статические файлы, а данные и команды проходят через **JSON API без серверного состояния** (`/jobby/api/*`). Дашборд не использует SignalR circuit, не хранит состояние компонентов на сервере и не требует привязки пользователя к конкретной реплике.

## Пакеты

| Пакет | Назначение |
|---|---|
| `Jobby.Dashboard` | Интерфейс на Blazor WebAssembly (встроенные статические файлы) + маршруты JSON API; `AddJobbyDashboard()` / `MapJobbyDashboard()` |
| `Jobby.Postgres` | `AddJobbyPostgresDashboardStorage()` - PostgreSQL-хранилище для чтения и команд |
| `Jobby.Dashboard.Authorization` | Дополнительная встроенная Basic-аутентификация (`AddBasicAuth` / `AddBasicAuthScheme`) и `PasswordHasher` |

`Jobby.Dashboard` зависит от ASP.NET Core. Удаление пакета и соответствующих вызовов регистрации не затрагивает поведение движка Jobby.

## Настройка

### 1. Регистрация сервисов (до `builder.Build()`)

`AddJobbyDashboard()` возвращает `IJobbyDashboardBuilder`. Перед вызовом `MapJobbyDashboard` необходимо **ровно один раз** выбрать способ авторизации через возвращённый объект (см. раздел [Безопасность](#безопасность)).

```csharp
IJobbyDashboardBuilder dashboardBuilder = builder.Services.AddJobbyDashboard(o =>
{
    o.RefreshInterval             = TimeSpan.FromSeconds(5);
    o.StaleServerThresholdSeconds = 300;
    o.ReadOnly                    = false;
});

dashboardBuilder.AllowAnonymous();

// Схема и префикс ДОЛЖНЫ совпадать с настройками PostgreSQL движка (см. ниже).
builder.Services.AddJobbyPostgresDashboardStorage(npgsqlDataSource, o =>
{
    o.SchemaName   = "";          // пустая строка -> схема public (по умолчанию)
    o.TablesPrefix = "jobby_";
});
```

`ReadOnly = false` разрешает управляющие действия, если выбранное хранилище также регистрирует хранилище команд. Установите `ReadOnly = true`, если дашборд должен работать только в режиме просмотра.

### 2. Подключение маршрута дашборда (после `app.Build()`)

```csharp
app.MapJobbyDashboard("/jobby");
```

Базовый путь дашборда не должен быть корневым. После этого дашборд будет доступен по адресу `/jobby`.

### Полный пример

```csharp
using Jobby.AspNetCore;
using Jobby.Dashboard;
using Jobby.Postgres.ConfigurationExtensions;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Jobby")
    ?? throw new InvalidOperationException("Connection string 'Jobby' was not found.");

var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);

builder.Services.AddJobbyServerAndClient(jobby =>
{
    jobby.AddJobsFromAssemblies(typeof(Program).Assembly);
    jobby.ConfigureJobby((sp, config) =>
    {
        config.UsePostgresql(sp.GetRequiredService<NpgsqlDataSource>());
    });
});

builder.Services
    .AddJobbyDashboard()
    .AllowAnonymous();

builder.Services.AddJobbyPostgresDashboardStorage(dataSource, o =>
{
    o.SchemaName   = "";
    o.TablesPrefix = "jobby_";
});

var app = builder.Build();

app.Services.GetRequiredService<IJobbyStorageMigrator>().Migrate();

app.MapJobbyDashboard("/jobby");
app.Run();
```

## Безопасность

**Дашборд защищён по умолчанию.** `MapJobbyDashboard` выбрасывает исключение при запуске, если через `IJobbyDashboardBuilder` не был выбран ровно один способ авторизации. Если не выбрать способ авторизации или выбрать два, приложение завершит запуск с исключением.

Статические файлы Blazor WebAssembly-приложения и файлы фреймворка **всегда публичны** - они не содержат секретов и должны быть доступны браузеру до загрузки приложения. Только JSON API (`/jobby/api/*`) защищается выбранной политикой. Это поведение сохраняется даже при наличии у хоста глобального `AuthorizationOptions.FallbackPolicy`: для статических ресурсов явно указан `AllowAnonymous`, а для API - `RequireAuthorization`.

Выберите ровно один вариант:

### Вариант 1 - Анонимный доступ (только для локальной разработки)

```csharp
builder.Services.AddJobbyDashboard().AllowAnonymous();
```

### Вариант 2 - Повторное использование существующей политики хоста

```csharp
builder.Services
    .AddJobbyDashboard()
    .RequireHostPolicy("MyReadPolicy");
    // дополнительная перегрузка: .RequireHostPolicy("MyReadPolicy", "MyManagePolicy")
```

### Вариант 3 - Настройка политики в коде

```csharp
builder.Services
    .AddJobbyDashboard()
    .RequireAuthorization(
        read   => read.RequireRole("jobby-read"),
        manage => manage.RequireRole("jobby-manage")
    );
```

### Вариант 4 - Встроенная Basic-аутентификация (`Jobby.Dashboard.Authorization`)

Для сервисов без собственной системы аутентификации дополнительный пакет `Jobby.Dashboard.Authorization` предоставляет Basic-схему с одной учётной записью, где пароль хранится в виде PBKDF2-хеша.

```csharp
builder.Services
    .AddJobbyDashboard()
    .AddBasicAuth(o =>
    {
        o.Username     = "admin";
        o.PasswordHash = "JDPBKDF2$v1$210000$...$...";
    });

app.MapJobbyDashboard("/jobby");
```

`AddBasicAuth` одновременно регистрирует схему Basic-аутентификации **и** настраивает авторизацию дашборда - не сочетайте его с другими методами выбора авторизации.

**Сгенерируйте хеш пароля** (никогда не храните пароль в открытом виде):

```csharp
string hash = Jobby.Dashboard.Authorization.PasswordHasher.Hash("ваш-пароль");
```

`PasswordHasher.Hash` возвращает строку вида `JDPBKDF2$v1$...` (PBKDF2-HMAC-SHA256). `AddBasicAuth` выбрасывает исключение при запуске, если переданный `PasswordHash` не является корректной PBKDF2-кодировкой.

> **Требуется HTTPS:** Basic-аутентификация передаёт учётные данные в base64-кодировке. В продакшн-окружении дашборд всегда должен работать через HTTPS.

### Вариант 5 - Совместное использование Basic и JWT (продвинутый)

Используйте `AddBasicAuthScheme` для регистрации Basic-схемы без выбора политики авторизации, затем вызовите `RequireAuthorization` для комбинирования с Bearer:

```csharp
builder.Services
    .AddJobbyDashboard()
    .AddBasicAuthScheme(o =>
    {
        o.Username     = "admin";
        o.PasswordHash = "JDPBKDF2$v1$210000$...$...";
    })
    .RequireAuthorization(p =>
        p.AddAuthenticationSchemes(
                JobbyDashboardBasicDefaults.Scheme,
                JwtBearerDefaults.AuthenticationScheme)
         .RequireAuthenticatedUser());
```

При запросе без аутентификации JSON API отвечает `401` с заголовками `WWW-Authenticate: Basic` и `WWW-Authenticate: Bearer` одновременно - браузеры реагируют на `Basic`, программные клиенты - на `Bearer`.

## Соответствие схемы и префикса

Параметры (`SchemaName`, `TablesPrefix`), переданные в `AddJobbyPostgresDashboardStorage`, **должны совпадать** с настройками, переданными в `UsePostgresql(...)` при конфигурировании движка. Несоответствие приведёт к тому, что дашборд будет обращаться к неверным или несуществующим таблицам.

```csharp
config.UsePostgresql(pg =>
{
    pg.UseDataSource(dataSource);
    pg.UseSchemaName("jobs");
    pg.UseTablesPrefix("jobby_");
});

builder.Services.AddJobbyPostgresDashboardStorage(dataSource, o =>
{
    o.SchemaName   = "jobs";
    o.TablesPrefix = "jobby_";
});
```

## Возможности и API

Дашборд показывает:

- **Обзор** - актуальное количество задач по статусу и очереди.
- **Задачи** - список задач с пагинацией, фильтрами и сортировкой.
- **Детали задачи** - параметры, ошибка, временные метки и другие поля записи.
- **Периодические задачи** - определения задач, запускаемых по расписанию.
- **Серверы** - подключённые экземпляры Jobby-сервера и состояние heartbeat.
- **Очереди** - разбивка по статусам для каждой очереди.
- **Заблокированные группы** - группы последовательного выполнения, остановленные из-за упавших или потерянных задач.

Управляющие действия доступны, когда зарегистрирован `IJobbyDashboardCommandStorage`, а `JobbyDashboardOptions.ReadOnly` равно `false`. `AddJobbyPostgresDashboardStorage()` регистрирует и хранилище для чтения, и хранилище команд. Если управление отключено, дашборд скрывает соответствующие действия, а API-маршруты управления возвращают `404`.

Маршруты чтения доступны под базовым путём дашборда:

| Маршрут | Описание |
|---|---|
| `GET /jobby/api/config` | Конфигурация дашборда (запрашивается клиентским приложением при старте) |
| `GET /jobby/api/stats` | Агрегированная статистика по задачам |
| `GET /jobby/api/jobs` | Постраничный список задач (параметры: `status`, `statuses`, `queueName`, `jobNameSearch`, `createdFrom`, `createdTo`, `page`, `pageSize`, `sortBy`, `sortDescending`) |
| `GET /jobby/api/jobs/{id}` | Детали одной задачи |
| `GET /jobby/api/recurrent` | Постраничный список периодических задач |
| `GET /jobby/api/servers` | Зарегистрированные экземпляры Jobby-сервера |
| `GET /jobby/api/queues` | Статистика по очередям |
| `GET /jobby/api/locked-groups` | Постраничный список заблокированных групп |
| `GET /jobby/api/locked-groups/detail?groupId={id}` | Детали заблокированной группы и связанных задач |

Маршруты управления проверяют antiforgery-заголовок и токен, полученные из `/jobby/api/config`:

| Маршрут | Описание |
|---|---|
| `POST /jobby/api/jobs/{id}/trigger` | Запустить запланированную непериодическую задачу сейчас |
| `POST /jobby/api/jobs/{id}/retry` | Повторить упавшую или замороженную непериодическую задачу |
| `DELETE /jobby/api/jobs/{id}` | Удалить непериодическую задачу, которая не выполняется прямо сейчас |
| `DELETE /jobby/api/recurrent/{id}` | Удалить определение периодической задачи |
| `POST /jobby/api/locked-groups/unlock-request` | Запросить разблокировку и разморозку группы последовательного выполнения |

Неизвестные пути в рамках `/jobby/api/*` возвращают `404`.

## Продакшн-хостинг - Kubernetes / несколько реплик

Дашборд является Blazor WebAssembly-приложением **без серверного состояния** и работает через JSON API **без серверного состояния**. Так как он раздаётся как статическое WebAssembly-приложение, а не работает как интерактивное серверное Blazor-приложение:

- **Нет SignalR circuit.** Поддерживать постоянное WebSocket-соединение не требуется.
- **Нет привязки сессии к реплике.** Запросы можно свободно балансировать между репликами без sticky sessions / session affinity.
- **Нет состояния компонентов на сервере.** Дашборд не хранит UI-состояние в процессе ASP.NET Core.

Обычные продакшн-требования по-прежнему актуальны:

- **HTTPS** - обязателен при использовании Basic-аутентификации (учётные данные передаются в base64-кодировке, без шифрования).
- **Обратный прокси и хостинг на подпути** - настройте `UseForwardedHeaders` и базовый путь прокси как обычно. Дашборд динамически подставляет корректный `<base href>`, поэтому хостинг на подпути работает без дополнительной ручной настройки.
- **Согласованность данных** - все данные читаются из общей базы данных PostgreSQL, поэтому каждая реплика возвращает одинаковое актуальное состояние кластера.
- **Antiforgery / Data Protection** - управляющие запросы используют ASP.NET Core antiforgery. Если управление включено, а запросы могут попадать на разные реплики, настройте общее хранилище ключей ASP.NET Core Data Protection так же, как для любого ASP.NET Core-приложения с antiforgery.

## Что дальше

- [Установка и настройка](./install-and-config)
- [Устойчивость к сбоям](./fault-tolerance)
- [Метрики и трейсинг](./observability)
