using financesApi.models;
using financesApi.services;
using Microsoft.AspNetCore.Mvc;

namespace financesApi.controllers;

[ApiController]
[Route("reconciliation")]
public sealed class ReconciliationController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StatementSessionDto>>> Get([FromQuery] int? accountId = null) =>
        Ok(await StatementReconciliationService.GetSessionsAsync(accountId));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StatementSessionDetailDto>> Get(int id) =>
        Ok(await StatementReconciliationService.GetSessionDetailAsync(id));

    [HttpPost]
    public async Task<ActionResult<StatementSessionDetailDto>> Post(CreateStatementSessionRequest request)
    {
        var session = await StatementReconciliationService.CreateSessionAsync(request);
        return Created($"/api/reconciliation/{session.Session.Id}", session);
    }

    [HttpPatch("{id:int}/transactions/{transactionId:int}/cleared")]
    public async Task<ActionResult<StatementSessionDetailDto>> PatchCleared(
        int id, int transactionId, UpdateStatementTransactionClearedRequest request) =>
        Ok(await StatementReconciliationService.SetTransactionClearedAsync(id, transactionId, request.Cleared));

    [HttpPost("{id:int}/adjustment")]
    public async Task<ActionResult<StatementSessionDetailDto>> PostAdjustment(int id) =>
        Ok(await StatementReconciliationService.UpsertAdjustmentAsync(id));

    [HttpDelete("{id:int}/adjustment")]
    public async Task<ActionResult<StatementSessionDetailDto>> DeleteAdjustment(int id) =>
        Ok(await StatementReconciliationService.DeleteAdjustmentAsync(id));

    [HttpPost("{id:int}/close")]
    public async Task<ActionResult<StatementSessionDetailDto>> Close(int id) =>
        Ok(await StatementReconciliationService.CloseSessionAsync(id));
}
