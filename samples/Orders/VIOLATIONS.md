# Deliberate violations in `samples/Orders`

Every architectural break in this sample is intentional and listed below. The table is the expected
result set for `tangent check` against `samples/Orders/.tangent/`; `tests/Tangent.Cli.Tests` should
assert against it.

Paths are relative to `samples/Orders/`. Diagnostic codes come from `TANGENT-PLAN.md` Appendix A.

## Violations

| # | File | Type / member | Contract + rule | Code | What is wrong |
|---|---|---|---|---|---|
| 1 | `src/Orders.Api/Controllers/CustomersController.cs` | `CustomersController` | `Controller` — `sealed` | TNG0101 | The class is not `sealed`. |
| 2 | `src/Orders.Api/Controllers/CustomersController.cs` | `CustomersController.GetByIdAsync` | `Controller` — `methods where public { attributes [ProducesResponseType(*)] }` | TNG0104 | Public action carries no `[ProducesResponseType]`. |
| 3 | `src/Orders.Api/Controllers/ReportsController.cs` | `ReportsController` | `Controller` — `inherits ApiControllerBase` | TNG0102 | Inherits `ControllerBase` directly, skipping the solution's base. |
| 4 | `src/Orders.Api/Controllers/ReportsController.cs` | `ReportsController` | `Controller` — `attributes [ApiController], [Route(*)]` | TNG0104 | Has `[ApiController]` but no `[Route(*)]`. |
| 5 | `src/Orders.Api/Controllers/ReportsController.cs` | `ReportsController..ctor` | `Controller` — `ctor { injects only IMediator, ILogger<*> }` | TNG0106 | Injects `OrdersDbContext` and `IClock`. |
| 6 | `src/Orders.Api/Controllers/ReportsController.cs` | `ReportsController._clock` | `Controller` — `ctor { require each parameter assigned to private readonly field }` | TNG0301 | `clock` is parked in a mutable `private IClock _clock`. |
| 7 | `src/Orders.Api/Controllers/ReportsController.cs` | `ReportsController.GetSummaries` | `Controller` — `methods where public { returns Task<ActionResult<*>> \| Task<IActionResult> }` | TNG0105 | Returns `IEnumerable<ReportViewModel>` synchronously. |
| 8 | `src/Orders.Api/Controllers/ReportsController.cs` | `ReportsController` | `Controller` — `forbid using Orders.Infrastructure.**` | TNG0201 | Names `OrdersDbContext` from `Orders.Infrastructure.Persistence`. |
| 9 | `src/Orders.Api/Controllers/ReportsController.cs` | `ReportsController.GetSummaries` | `Controller` — `forbid using Microsoft.EntityFrameworkCore.**` | TNG0201 | Calls `AsNoTracking()`. |
| 10 | `src/Orders.Api/Controllers/ReportsController.cs` | `ReportsController` | `project.tan` — `Api -> Application, Domain` | TNG0202 | Layer `Api` reaches layer `Infrastructure`. |
| 11 | `src/Orders.Api/Controllers/LegacyOrdersController.cs` | `LegacyOrdersController` | `[Tangent("Controler")]` — the named contract must exist | TNG0001 | Misspelled contract name; the fix offers `Controller`. **Error**, not warning. |
| 12 | `src/Orders.Api/Controllers/LegacyOrdersController.cs` | `LegacyOrdersController.FetchAsync` | `Controller` — `methods where public { forbid new DbContext, new HttpClient }` | TNG0203 | `new HttpClient()` — exact type-name match. |
| 13 | `src/Orders.Api/Controllers/LegacyOrdersController.cs` | `LegacyOrdersController.FetchAsync` | `Controller` — `methods where public { forbid new DbContext, new HttpClient }` | TNG0203 | `new OrdersDbContext(...)` — matches `DbContext` only if `new` matching walks base types. |
| 14 | `src/Orders.Api/Controllers/LegacyOrdersController.cs` | `LegacyOrdersController.FetchAsync` | `Controller` — `forbid using Orders.Infrastructure.**`, `forbid using Microsoft.EntityFrameworkCore.**` | TNG0201 | Names `OrdersDbContext` and `DbContextOptionsBuilder<>`. |
| 15 | `src/Orders.Api/Controllers/LegacyOrdersController.cs` | `LegacyOrdersController.RebuildProjectionsAsync` | `Controller` — `methods where public { attributes [HttpGet \| HttpPost \| HttpPut \| HttpDelete \| HttpPatch] }` | TNG0104 | Public action with no HTTP verb attribute. |
| 16 | `src/Orders.Api/DependencyInjection/ServiceRegistration.cs` | `ServiceRegistration.AddOrdersServices` | `project.tan` — `Api -> Application, Domain` | TNG0202 | The composition root names Infrastructure types. |
| 17 | `src/Orders.Infrastructure/Repositories/StockLevelRepository.cs` | `IStockLevelRepository` | `Repository` — `interface I{Name} lives in Domain` | TNG0109 | The interface is declared in `Orders.Infrastructure.Repositories`. |
| 18 | `src/Orders.Infrastructure/Repositories/StockLevelRepository.cs` | `StockLevelRepository` | `Repository` — `sealed` | TNG0101 | The class is not `sealed`. |
| 19 | `src/Orders.Infrastructure/Repositories/StockLevelRepository.cs` | `StockLevelRepository..ctor` | `Repository` — `ctor { injects only OrdersDbContext }` | TNG0106 | Also injects `IClock`. |
| 20 | `src/Orders.Infrastructure/Repositories/StockLevelRepository.cs` | `StockLevelRepository.GetLevels` | `Repository` — `methods where public { returns Task<*> }` | TNG0105 | Returns `IReadOnlyList<int>` synchronously. |
| 21 | `src/Orders.Domain/Entities/Customer.cs` | `Customer..ctor` | `Entity` — `ctor { forbid public }` | TNG0101 | Public constructor instead of a factory. |
| 22 | `src/Orders.Domain/Entities/Customer.cs` | `Customer.Email` | `Entity` — `properties { forbid public set }` | TNG0107 | Public setter on an entity property. |
| 23 | `src/Orders.Domain/Entities/Customer.cs` | `Customer.Deactivate` | `AggregateRoot` — `methods where public { forbid returns void }` | TNG0105 | Command returns `void` instead of a result. |
| 24 | `src/Orders.Domain/Entities/ShipmentNote.cs` | `ShipmentNote` | `Entity` — `inherits Entity<*> \| AggregateRoot<*>` | TNG0102 | Lives in the entities namespace but inherits nothing. |
| 25 | `src/Orders.Domain/Entities/ShipmentNote.cs` | `ShipmentNote.Carrier` | `Entity` — `forbid public fields` | TNG0108 | Public instance field. |
| 26 | `src/Orders.Domain/Entities/ShipmentNote.cs` | `ShipmentNote.ToLegacyTable` | `Entity` — `forbid using System.Data.**, Microsoft.**` | TNG0201 | Returns a `System.Data.DataTable`. |
| 27 | `src/Orders.Domain/Services/DiscountPolicyService.cs` | `DiscountPolicyService._evaluations` | `DomainService` — `stateless` | TNG0303 | Mutable instance field. |
| 28 | `src/Orders.Domain/Services/DiscountPolicyService.cs` | `DiscountPolicyService._evaluations` | `DomainService` — `fields { require readonly }` | TNG0101 | Field is not `readonly`. |
| 29 | `src/Orders.Domain/Services/DiscountPolicyService.cs` | `DiscountPolicyService.TotalEvaluations` | `DomainService` — `fields { require private; forbid static }` | TNG0101 | Field is `public` and `static`. |
| 30 | `src/Orders.Domain/Services/OrderNotificationService.cs` | `OrderNotificationService` | `DomainService` — `implements I{Name}` | TNG0103 | Does not implement `IOrderNotificationService`, which is declared directly above it. |
| 31 | `src/Orders.Domain/Services/OrderNotificationService.cs` | `OrderNotificationService._dispatcher` | `DomainService` — `forbid using Orders.Application.**, Orders.Infrastructure.**` | TNG0201 | Names `Orders.Application.Notifications.INotificationDispatcher`. |
| 32 | `src/Orders.Domain/Services/OrderNotificationService.cs` | `OrderNotificationService` | `project.tan` — `Domain -> nothing` | TNG0202 | Layer `Domain` reaches layer `Application`. |
| 33 | `src/Orders.Application/ViewModels/DraftOrderViewModel.cs` | `DraftOrderViewModel.Id` | `ViewModel` — `properties { forbid public set }` | TNG0107 | Public setter on a view model. |
| 34 | `src/Orders.Application/Orders/ListOrdersQuery.cs` | `ListOrdersQueryHandler._orders` | `QueryHandler` — `ctor { require each parameter assigned to private readonly field }` | TNG0301 | `orders` is assigned to a mutable `private IOrderRepository _orders`. |
| 35 | `src/Orders.Application/Orders/ListOrdersQuery.cs` | `ListOrdersQueryHandler.HandleAsync` | `QueryHandler` — `forbid using Orders.Infrastructure.**` | TNG0201 | Calls `Orders.Infrastructure.Caching.CacheKeys`. |
| 36 | `src/Orders.Application/Orders/ListOrdersQuery.cs` | `ListOrdersQueryHandler` | `project.tan` — `Application -> Domain` | TNG0202 | Layer `Application` reaches layer `Infrastructure`. |

**Coverage by rule kind** — modifiers (1, 18, 21, 28, 29), `inherits` (3, 24), `implements` (30),
`attributes` (2, 4, 15), `returns` (7, 20, 23), `injects only` (5, 19), public setter on an entity
(22, 33), public field on an entity (25), public entity constructor (21), `forbid using`
(8, 9, 14, 26, 31, 35), `forbid new` (12, 13), layer dependency (10, 16, 32, 36),
`interface ... lives in` (17), `each parameter assigned to field` (6, 34), `stateless` (27),
unknown contract (11).

## Suppressed on purpose

These are genuine rule breaks that an ignore or a `bind ... except` clause removes. The expected
result is **no warning**; with `--show-ignored` they surface as TNG9000.

| File | Type / member | Mechanism | What it suppresses |
|---|---|---|---|
| `.tangent/bindings.tan` + `src/Orders.Api/Controllers/HealthController.cs` | `HealthController.GetAsync` | `bind Orders.Api.Controllers.HealthController to Controller except [ProducesResponseType]` | The missing `[ProducesResponseType]` (would be TNG0104). |
| `.tangent/bindings.tan` + `src/Orders.Infrastructure/Repositories/LegacyReportRepository.cs` | `LegacyReportRepository` | `ignore ... reason "migrating in Q4 2026"` | The whole type: not `sealed` (TNG0101), no `ILegacyReportRepository` (TNG0103 and TNG0109), injects a `string` (TNG0106), `Run` returns `DataTable` (TNG0105). |
| `.tangent/bindings.tan` + `src/Orders.Domain/Entities/AuditEntry.cs` | `AuditEntry` | `ignore ... from Entity reason "append-only log, public setters by design"` | Public constructor (TNG0101) and three public setters (TNG0107). |
| `src/Orders.Api/Controllers/CustomersController.cs` | `CustomersController.ExportAsync` | `[TangentIgnore("...", Contract = "Controller")]` on the method | `Task<string>` instead of `Task<ActionResult<*>>` (TNG0105). |

`Contract = "Controller"` is used rather than `Rule = "Controller/methods/returns#0"` because rule
ids are assigned by the binder and are not stable until Phase 2 lands. Once they are, this is the
natural place to exercise `Rule =` and the TNG0002 path.

## Conforming types

These must produce **zero** diagnostics. If any of them ever reports, the analyzer has a false
positive.

| Type | Contract |
|---|---|
| `Orders.Api.Controllers.OrdersController` | `Controller` |
| `Orders.Api.Controllers.HealthController` | `Controller` (with the `except` clause) |
| `Orders.Infrastructure.Repositories.OrderRepository` | `Repository` |
| `Orders.Infrastructure.Repositories.CustomerRepository` | `Repository` |
| `Orders.Domain.Entities.Order` | `Entity`, `AggregateRoot` |
| `Orders.Domain.Entities.OrderLine` | `Entity` |
| `Orders.Domain.Services.PricingService` | `DomainService` |
| `Orders.Domain.Services.TaxCalculator` | `DomainService` |
| `Orders.Application.Orders.GetOrderQueryHandler` | `QueryHandler` |
| `Orders.Application.Orders.CreateOrderCommandHandler` | `QueryHandler` |
| `Orders.Application.Customers.GetCustomerQueryHandler` | `QueryHandler` |
| `Orders.Application.ViewModels.OrderViewModel` | `ViewModel` |
| `Orders.Application.ViewModels.OrderLineViewModel` | `ViewModel` |
| `Orders.Application.ViewModels.CustomerViewModel` | `ViewModel` |
| `Orders.Application.ViewModels.ReportViewModel` | `ViewModel` |
