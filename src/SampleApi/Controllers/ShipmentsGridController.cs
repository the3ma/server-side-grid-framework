namespace SampleApi.Controllers;

using GridKit.Contract;
using GridKit.Dialects;
using GridKit.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SampleApi.Auth;
using SampleApi.Data;
using SampleApi.Grid;

/// <summary>Thin and authorized: identity and row scope come from the principal, never from the body.</summary>
[ApiController]
[Authorize]
[Route("api/shipments/grid")]
public sealed class ShipmentsGridController(IGridExecutor executor, ShipmentConnectionFactory connections) : ControllerBase
{
    [HttpPost]
    public async Task<GridPage<ShipmentRow>> Page([FromBody] GridRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await executor.QueryPageAsync(
            connection, SqliteGridDialect.Instance, ShipmentsGrid.BaseSql, ShipmentsGrid.Map, request,
            this.Scope(), this.Options(), cancellationToken);
    }

    [HttpPost("values")]
    public async Task<GridValuesPage> Values([FromBody] GridValuesRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await executor.QueryValuesAsync(
            connection, SqliteGridDialect.Instance, ShipmentsGrid.BaseSql, ShipmentsGrid.Map, request,
            this.Scope(), this.Options(), cancellationToken);
    }

    private object Scope() => new { tenantId = this.User.TenantId(), finance = this.User.IsFinance() ? 1 : 0 };

    private GridExecutionOptions Options()
    {
        var finance = this.User.IsFinance();
        return new GridExecutionOptions { CanAccessColumn = column => column.Field == "priceTier" && finance };
    }
}
