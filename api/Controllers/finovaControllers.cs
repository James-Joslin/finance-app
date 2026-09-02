using System.Globalization;
using System.Text;
using financesApi.models;
using financesApi.services;
using Microsoft.AspNetCore.Mvc;

namespace financesApi.controllers;

[ApiController]
[Route("enrollment")]
public sealed class EnrollmentController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EnrollmentStatusDto>> Get() =>
        Ok(await FinovaDataService.GetEnrollmentStatusAsync());

    [HttpPut]
    public async Task<ActionResult<EnrollmentStatusDto>> Put(SaveEnrollmentRequest request) =>
        Ok(await FinovaDataService.SaveEnrollmentAsync(request));
}

[ApiController]
[Route("settings")]
public sealed class SettingsController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HouseholdSettingsDto>> Get() => Ok(await FinovaDataService.GetSettingsAsync());

    [HttpPut]
    public async Task<ActionResult<HouseholdSettingsDto>> Put(UpdateHouseholdSettingsRequest request) =>
        Ok(await FinovaDataService.UpdateSettingsAsync(request));
}

[ApiController]
[Route("accounts")]
public sealed class AccountsController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AccountDto>>> Get([FromQuery] bool includeArchived = false) =>
        Ok(await FinovaDataService.GetAccountsAsync(includeArchived));

    [HttpPost]
    public async Task<ActionResult<AccountDto>> Post(CreateAccountRequest request)
    {
        var account = await FinovaDataService.CreateAccountAsync(request);
        return Created($"/accounts/{account.Id}", account);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AccountDto>> Put(int id, UpdateAccountRequest request)
    {
        var account = await FinovaDataService.UpdateAccountAsync(id, request);
        return account is null ? NotFound() : Ok(account);
    }
}

[ApiController]
[Route("categories")]
public sealed class CategoriesController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> Get([FromQuery] bool includeArchived = false) =>
        Ok(await FinovaDataService.GetCategoriesAsync(includeArchived));

    [HttpGet("rules")]
    public async Task<ActionResult<IReadOnlyList<TransactionRuleDto>>> GetRules() =>
        Ok(await FinovaDataService.GetTransactionRulesAsync());

    [HttpPost("rules")]
    public async Task<ActionResult<TransactionRuleDto>> PostRule(SaveTransactionRuleRequest request)
    {
        var rule = await FinovaDataService.SaveTransactionRuleAsync(null, request);
        return Created($"/categories/rules/{rule.Id}", rule);
    }

    [HttpPut("rules/{id:int}")]
    public async Task<ActionResult<TransactionRuleDto>> PutRule(int id, SaveTransactionRuleRequest request) =>
        Ok(await FinovaDataService.SaveTransactionRuleAsync(id, request));

    [HttpDelete("rules/{id:int}")]
    public async Task<IActionResult> DeleteRule(int id) =>
        await FinovaDataService.DeleteTransactionRuleAsync(id) ? NoContent() : NotFound();

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Post(CreateCategoryRequest request)
    {
        var category = await FinovaDataService.CreateCategoryAsync(request);
        return Created($"/categories/{category.Id}", category);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CategoryDto>> Put(int id, UpdateCategoryRequest request) =>
        Ok(await FinovaDataService.UpdateCategoryAsync(id, request));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        await FinovaDataService.DeleteCategoryAsync(id) ? NoContent() : NotFound();
}

[ApiController]
[Route("transactions")]
public sealed class TransactionsController : ControllerBase
{
    [HttpGet("type-codes")]
    public async Task<ActionResult<IReadOnlyList<TransactionTypeCodeDto>>> GetTypeCodes() =>
        Ok(await FinovaDataService.GetTransactionTypeCodesAsync());

    [HttpPost]
    public async Task<ActionResult<TransactionDetailDto>> Post(SaveManualTransactionRequest request)
    {
        var transaction = await FinovaDataService.CreateManualTransactionAsync(request);
        return Created($"/transactions/{transaction.Transaction.Id}", transaction);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TransactionDetailDto>> Get(int id) =>
        Ok(await FinovaDataService.GetTransactionDetailsAsync(id));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TransactionDetailDto>> Put(int id, SaveManualTransactionRequest request) =>
        Ok(await FinovaDataService.UpdateManualTransactionAsync(id, request));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await FinovaDataService.DeleteManualTransactionAsync(id);
        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<TransactionPageDto>> Get(
        [FromQuery] int? accountId, [FromQuery] int? categoryId, [FromQuery] string? search,
        [FromQuery] string type = "all", [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Ok(await FinovaDataService.GetTransactionsAsync(accountId, categoryId, search, type, startDate, endDate, page, pageSize));

    [HttpGet("{id:int}/transfer-candidates")]
    public async Task<ActionResult<IReadOnlyList<TransferCandidateDto>>> TransferCandidates(int id) =>
        Ok(await FinovaDataService.GetTransferCandidatesAsync(id));

    [HttpPost("{id:int}/transfer-pair")]
    public async Task<ActionResult<TransferPairDto>> PairTransfer(int id, TransferPairRequest request) =>
        Ok(await FinovaDataService.PairTransferAsync(id, request.PairedTransactionId));

    [HttpDelete("{id:int}/transfer-pair")]
    public async Task<IActionResult> UnpairTransfer(int id)
    {
        await FinovaDataService.UnpairTransferAsync(id);
        return NoContent();
    }

    [HttpPatch("{id:int}/category")]
    public async Task<IActionResult> PatchCategory(int id, UpdateTransactionCategoryRequest request)
    {
        try
        {
            await FinovaDataService.UpdateTransactionCategoryAsync(id, request);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id:int}/recurring")]
    public async Task<ActionResult<RecurringItemDto>> MarkRecurring(int id, MarkTransactionRecurringRequest request)
    {
        try
        {
            var item = await FinovaDataService.MarkTransactionRecurringAsync(id, request);
            return Created($"/plan/recurring/{item.Id}", item);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("import/preview")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<ImportBatchSummary>> PreviewImport([FromForm] OfxUploadRequest request) =>
        Ok(await TransactionImportService.PreviewAsync(request.OfxContent, request.AccountId));

    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Import([FromForm] OfxUploadRequest request)
    {
        var batch = await TransactionImportService.ImportImmediatelyAsync(request.OfxContent, request.AccountId);
        return Ok(new
        {
            success = true,
            batchId = batch.Id,
            imported = batch.Imported,
            skipped = batch.Skipped,
            rejected = batch.Rejected,
        });
    }

    [HttpPost("imports/{id:long}/commit")]
    public async Task<ActionResult<ImportBatchSummary>> CommitImport(long id) =>
        Ok(await TransactionImportService.CommitAsync(id));

    [HttpGet("imports")]
    public async Task<ActionResult<PagedImportBatches>> GetImports(
        [FromQuery] int? accountId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Ok(await TransactionImportService.GetHistoryAsync(accountId, page, pageSize));

    [HttpGet("imports/{id:long}/rows")]
    public async Task<ActionResult<PagedImportRows>> GetImportRows(
        long id, [FromQuery] string? outcome = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Ok(await TransactionImportService.GetRowsAsync(id, outcome, page, pageSize));

    [HttpPost("imports/{id:long}/undo")]
    public async Task<ActionResult<ImportUndoResult>> UndoImport(long id) =>
        Ok(await TransactionImportService.UndoAsync(id));

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] int? accountId, [FromQuery] int? categoryId, [FromQuery] string? search,
        [FromQuery] string type = "all", [FromQuery] DateOnly? startDate = null, [FromQuery] DateOnly? endDate = null)
    {
        var csv = new StringBuilder("Date,Description,Memo,Category,Account,Status,Amount\r\n");
        const int pageSize = 1000;
        var pageNumber = 1;
        TransactionPageDto page;
        do
        {
            page = await FinovaDataService.GetTransactionsAsync(accountId, categoryId, search, type, startDate, endDate, pageNumber, pageSize);
            foreach (var item in page.Items)
            {
                csv.Append(Csv(item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(',')
                    .Append(Csv(item.Payee)).Append(',').Append(Csv(item.Memo)).Append(',')
                    .Append(Csv(item.CategoryName)).Append(',').Append(Csv(item.AccountName)).Append(',')
                    .Append(Csv(item.Status)).Append(',').Append(item.Amount.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            }
            pageNumber++;
        } while (pageNumber <= page.TotalPages);
        var today = await FinovaDataService.GetHouseholdTodayAsync();
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"finova-transactions-{today:yyyyMMdd}.csv");
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}

[ApiController]
[Route("goals")]
public sealed class GoalsController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<GoalSummaryDto>> Get([FromQuery] bool includeArchived = false) =>
        Ok(await FinovaDataService.GetGoalsAsync(includeArchived));

    [HttpPost]
    public async Task<ActionResult<GoalDto>> Post(SaveGoalRequest request)
    {
        var goal = await FinovaDataService.SaveGoalAsync(null, request);
        return Created($"/goals/{goal.Id}", goal);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<GoalDto>> Put(int id, SaveGoalRequest request) =>
        Ok(await FinovaDataService.SaveGoalAsync(id, request));

    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder(ReorderGoalsRequest request)
    {
        await FinovaDataService.ReorderGoalsAsync(request.OrderedIds);
        return NoContent();
    }

    [HttpPost("images")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage([FromForm] IFormFile image)
    {
        var id = await FinovaDataService.SaveGoalImageAsync(image);
        return Created($"/goals/images/{id}", new { id, url = $"/api/goals/images/{id}" });
    }

    [HttpGet("images/{id:int}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> Image(int id)
    {
        var image = await FinovaDataService.GetGoalImageAsync(id);
        if (image is null) return NotFound();
        Response.Headers.ETag = $"\"{image.Value.Hash}\"";
        return File(image.Value.Content, image.Value.ContentType);
    }

    [HttpDelete("images/{id:int}")]
    public async Task<IActionResult> DeleteImage(int id) =>
        await FinovaDataService.DeleteGoalImageAsync(id)
            ? NoContent()
            : Conflict(new { error = "The image is still in use or does not exist." });
}

[ApiController]
[Route("plan")]
public sealed class PlanController : ControllerBase
{
    [HttpGet("recurring")]
    public async Task<ActionResult<IReadOnlyList<RecurringItemDto>>> GetRecurring([FromQuery] bool activeOnly = true) =>
        Ok(await FinovaDataService.GetRecurringItemsAsync(activeOnly));

    [HttpPost("recurring")]
    public async Task<ActionResult<RecurringItemDto>> PostRecurring(SaveRecurringItemRequest request)
    {
        var item = await FinovaDataService.SaveRecurringItemAsync(null, request);
        return Created($"/plan/recurring/{item.Id}", item);
    }

    [HttpPut("recurring/{id:int}")]
    public async Task<ActionResult<RecurringItemDto>> PutRecurring(int id, SaveRecurringItemRequest request) =>
        Ok(await FinovaDataService.SaveRecurringItemAsync(id, request));

    [HttpDelete("recurring/{id:int}")]
    public async Task<IActionResult> DeleteRecurring(int id) =>
        await FinovaDataService.DeleteRecurringItemAsync(id) ? NoContent() : NotFound();

    [HttpGet("occurrences")]
    public async Task<ActionResult<IReadOnlyList<RecurringOccurrenceDto>>> Occurrences(
        [FromQuery] DateOnly? start = null, [FromQuery] DateOnly? end = null, [FromQuery] int? recurringItemId = null) =>
        Ok(await FinovaDataService.GetRecurringOccurrencesAsync(start, end, recurringItemId));

    [HttpPut("occurrences/{id:int}")]
    public async Task<ActionResult<RecurringOccurrenceDto>> PutOccurrence(int id, UpdateRecurringOccurrenceRequest request)
    {
        try
        {
            return Ok(await FinovaDataService.UpdateRecurringOccurrenceAsync(id, request));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("suggestions")]
    public async Task<ActionResult<IReadOnlyList<RecurringSuggestionDto>>> Suggestions() =>
        Ok(await FinovaDataService.GetRecurringSuggestionsAsync());

    [HttpGet("budgets")]
    public async Task<ActionResult<IReadOnlyList<BudgetDto>>> Budgets([FromQuery] DateOnly? month = null) =>
        Ok(await FinovaDataService.GetBudgetsAsync(month));

    [HttpPut("budgets")]
    public async Task<ActionResult<BudgetDto>> PutBudget(SaveBudgetRequest request) =>
        Ok(await FinovaDataService.SaveBudgetAsync(request));

    [HttpGet("safety")]
    public async Task<ActionResult<IReadOnlyList<AccountSafetyDto>>> Safety() =>
        Ok(await FinovaDataService.GetAccountSafetyAsync());
}

[ApiController]
[Route("dashboard")]
public sealed class DashboardController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get() => Ok(await FinovaDataService.GetDashboardAsync());
}

[ApiController]
[Route("insights")]
public sealed class InsightsController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<InsightsDto>> Get(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] bool allTime = false)
    {
        var today = await FinovaDataService.GetHouseholdTodayAsync();
        var effectiveEnd = endDate ?? today;
        if (effectiveEnd > today) throw new ArgumentException("End date cannot be in the future.");
        var effectiveStart = allTime
            ? await FinovaDataService.GetEarliestInsightsDateAsync(effectiveEnd)
            : startDate ?? new DateOnly(effectiveEnd.Year, effectiveEnd.Month, 1);
        return Ok(await FinovaDataService.GetInsightsAsync(effectiveStart, effectiveEnd));
    }
}

[ApiController]
[Route("search")]
public sealed class SearchController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SearchResultDto>>> Get([FromQuery] string q = "") =>
        Ok(await FinovaDataService.SearchAsync(q));
}
