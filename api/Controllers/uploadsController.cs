using financesApi.models;
using financesApi.services;
using Microsoft.AspNetCore.Mvc;

namespace financesApi.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class uploadsController : ControllerBase
{
    [HttpPost("newAccount")]
    public async Task<IActionResult> newAccount(NewAccountRequest request)
    {
        if (!DateOnly.TryParse(request.StartingDate, out var openingDate))
            return BadRequest(new { error = "Invalid date format." });

        var holder = string.Join(' ', new[] { request.FirstName, request.LastName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var account = await FinovaDataService.CreateAccountAsync(new(
            request.AccountName, request.FirstName, request.LastName, false, "current", null, null,
            request.StartingBalance, openingDate, 0, true, holder, null));
        return Ok(new { message = "Account setup completed", account_id = account.Id });
    }

    [HttpGet("getAccounts")]
    public async Task<IActionResult> getAccounts()
    {
        var accounts = await FinovaDataService.GetAccountsAsync();
        return Ok(accounts.Select(account => new { id = account.Id, name = account.Name }));
    }

    [HttpPost("uploadTransactions")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> uploadTransactions([FromForm] OfxUploadRequest request)
    {
        var batch = await TransactionImportService.ImportImmediatelyAsync(request.OfxContent, request.AccountId);
        return Ok(new
        {
            success = true,
            message = $"Imported {batch.Imported} transactions and skipped {batch.Skipped} duplicates.",
            accountId = request.AccountId,
            transactionCount = batch.Imported,
            skipped = batch.Skipped,
            rejected = batch.Rejected,
            batchId = batch.Id,
        });
    }
}
