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
        try
        {
            if (request.OfxContent.Length == 0) return BadRequest(new { error = "The uploaded file is empty." });
            using var stream = request.OfxContent.OpenReadStream();
            var parsed = FinancialFileParserService.Parse(stream, request.OfxContent.FileName);
            if (parsed.Count == 0) return BadRequest(new { error = "No valid transactions found in the file." });
            var inserted = await GenericDataService.FilterAndInsertTransactionsAsync(parsed, request.AccountId);
            await FinovaDataService.ReconcileRecurringTransactionsAsync(request.AccountId,
                parsed.Min(item => DateOnly.FromDateTime(item.Date)), parsed.Max(item => DateOnly.FromDateTime(item.Date)));
            return Ok(new
            {
                success = true,
                message = $"Imported {inserted.Count} transactions and skipped {parsed.Count - inserted.Count} duplicates.",
                accountId = request.AccountId,
                transactionCount = inserted.Count,
                skipped = parsed.Count - inserted.Count,
            });
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or ArgumentException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
