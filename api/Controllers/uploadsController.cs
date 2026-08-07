using Microsoft.AspNetCore.Mvc;
using financesApi.models;
using financesApi.utilities;
using financesApi.services;
using Npgsql;
using System.Data;
using System.Globalization;

namespace financesApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class uploadsController : ControllerBase
    {
        [HttpPost("newAccount")]
        public async Task<IActionResult> newAccount(NewAccountRequest accountParameters)
        {
            try
            {
                int accountId = 0;
                await PostgreSqlQuerier.ExecuteTransactionAsync(async (connection, transaction) =>
                {
                    // 1. Check if this person + account combination already exists
                    var duplicateCheckQuery = @"
                        SELECT COUNT(*) 
                        FROM accounts a 
                        JOIN people p ON a.owner_id = p.id 
                        WHERE p.first_name = @first_name AND p.last_name = @last_name AND a.name = @account_name;";

                    using var duplicateCommand = new NpgsqlCommand(duplicateCheckQuery, connection, transaction);
                    duplicateCommand.Parameters.AddWithValue("@first_name", accountParameters.FirstName);
                    duplicateCommand.Parameters.AddWithValue("@last_name", accountParameters.LastName);
                    duplicateCommand.Parameters.AddWithValue("@account_name", accountParameters.AccountName);

                    var duplicateCount = Convert.ToInt32(await duplicateCommand.ExecuteScalarAsync());
                    if (duplicateCount > 0)
                    {
                        throw new InvalidOperationException($"Account '{accountParameters.AccountName}' already exists for {accountParameters.FirstName} {accountParameters.LastName}");
                    }

                    // 2. Insert or get person (check if exists first)
                    var checkPersonQuery = "SELECT id FROM people WHERE first_name = @first_name AND last_name = @last_name;";
                    using var checkPersonCommand = new NpgsqlCommand(checkPersonQuery, connection, transaction);
                    checkPersonCommand.Parameters.AddWithValue("@first_name", accountParameters.FirstName);
                    checkPersonCommand.Parameters.AddWithValue("@last_name", accountParameters.LastName);
                    var existingPersonId = await checkPersonCommand.ExecuteScalarAsync();

                    int personId;
                    if (existingPersonId != null)
                    {
                        personId = Convert.ToInt32(existingPersonId);
                    }
                    else
                    {
                        var insertPersonQuery = "INSERT INTO people (first_name, last_name) VALUES (@first_name, @last_name) RETURNING id;";
                        using var insertPersonCommand = new NpgsqlCommand(insertPersonQuery, connection, transaction);
                        insertPersonCommand.Parameters.AddWithValue("@first_name", accountParameters.FirstName);
                        insertPersonCommand.Parameters.AddWithValue("@last_name", accountParameters.LastName);
                        personId = Convert.ToInt32(await insertPersonCommand.ExecuteScalarAsync());
                    }

                    // 3. Insert account (using 'owner_id' to match your schema)
                    var accountQuery = @"
                        INSERT INTO accounts (name, owner_id) 
                        VALUES (@account_name, @owner_id) 
                        RETURNING id;";

                    using var accountCommand = new NpgsqlCommand(accountQuery, connection, transaction);
                    accountCommand.Parameters.AddWithValue("@account_name", accountParameters.AccountName);
                    accountCommand.Parameters.AddWithValue("@owner_id", personId);
                    accountId = Convert.ToInt32(await accountCommand.ExecuteScalarAsync());

                    // 4. Insert initial transaction (note: using 'memo' instead of 'description' based on your schema)
                    var transactionQuery = @"
                        INSERT INTO transactions (account_id, amount, transaction_date, memo, fitid, transaction_type) 
                        VALUES (@account_id, @amount, @transaction_date, @memo, @fitid, @transaction_type);";

                    if (!DateTime.TryParse(accountParameters.StartingDate, out DateTime startDate))
                    {
                        throw new ArgumentException("Invalid date format");
                    }

                    using var transCommand = new NpgsqlCommand(transactionQuery, connection, transaction);
                    transCommand.Parameters.AddWithValue("@account_id", accountId);
                    transCommand.Parameters.AddWithValue("@amount", accountParameters.StartingBalance);
                    transCommand.Parameters.AddWithValue("@transaction_date", startDate);
                    transCommand.Parameters.AddWithValue("@memo", "Initial Deposit");
                    transCommand.Parameters.AddWithValue("@fitid", "Initial Deposit");
                    transCommand.Parameters.AddWithValue("@transaction_type", "Initial Deposit");
                    await transCommand.ExecuteNonQueryAsync();
                });

                return Ok(new { message = "Account setup completed", account_id = accountId });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating account: {ex.Message}");
                return StatusCode(500, new { error = "An error occurred while creating the account" });
            }
        }

        [HttpGet("getAccounts")]
        public async Task<IActionResult> getAccounts()
        {
            DataTable accountsTable = await GenericDataService.ExecuteQueryAsync(queryPath: "get_accounts");
            return Ok(DataEditor.ConvertData(accountsTable));
        }
                
        [HttpPost("uploadTransactions")]
        public async Task<IActionResult> uploadTransactions([FromForm] OfxUploadRequest ofxUploadRequest)
        {
            try
            {
                Console.WriteLine($"Processing upload for AccountId: {ofxUploadRequest.AccountId}");
                Console.WriteLine($"File name: {ofxUploadRequest.OfxContent.FileName}");
                Console.WriteLine($"File size: {ofxUploadRequest.OfxContent.Length} bytes");
                
                using var stream = ofxUploadRequest.OfxContent.OpenReadStream();
                
                // Parse using unified parser that detects file type
                var parsedResults = FinancialFileParserService.Parse(
                    stream, 
                    ofxUploadRequest.OfxContent.FileName
                );
                
                if (!parsedResults.Any())
                {
                    return BadRequest(new { error = "No valid transactions found in file" });
                }
                
                // // ============ PRINT PARSED DATA ============
                // Console.WriteLine("\n========================================");
                // Console.WriteLine($"PARSED {parsedResults.Count} TRANSACTIONS");
                // Console.WriteLine("========================================\n");
                
                // int index = 1;
                // decimal totalAmount = 0;
                
                // foreach (var tx in parsedResults)
                // {
                //     Console.WriteLine($"Transaction #{index}:");
                //     Console.WriteLine($"  Date:   {tx.Date:yyyy-MM-dd}");
                //     Console.WriteLine($"  Amount: {tx.Amount}");
                //     Console.WriteLine($"  Payee:  {tx.Payee}");
                //     Console.WriteLine($"  Memo:   {tx.Memo ?? "(no memo)"}");
                    
                //     // Check for type-specific fields
                //     if (tx is OfxTransactionDto ofxTx)
                //     {
                //         Console.WriteLine($"  Type:   OFX");
                //         Console.WriteLine($"  FitId:  {ofxTx.FitId ?? "(no FitId)"}");
                //         Console.WriteLine($"  TrnType: {ofxTx.TransType ?? "(no type)"}");
                //     }
                //     else if (tx is QifTransactionDto qifTx)
                //     {
                //         Console.WriteLine($"  Type:   QIF");
                //         Console.WriteLine($"  Category: {qifTx.Category ?? "(no category)"}");
                //         Console.WriteLine($"  Check#:  {qifTx.CheckNumber ?? "(no check number)"}");
                //     }
                //     else
                //     {
                //         Console.WriteLine($"  Type:   Generic");
                //     }
                    
                //     Console.WriteLine("  ---");
                    
                //     totalAmount += tx.Amount;
                //     index++;
                // }
                
                // // Summary statistics
                // Console.WriteLine("\n========================================");
                // Console.WriteLine("SUMMARY:");
                // Console.WriteLine($"  Total Transactions: {parsedResults.Count}");
                // Console.WriteLine($"  Date Range: {parsedResults.Min(t => t.Date):yyyy-MM-dd} to {parsedResults.Max(t => t.Date):yyyy-MM-dd}");
                // Console.WriteLine($"  Total Amount: {totalAmount}");
                
                // var credits = parsedResults.Where(t => t.Amount > 0).ToList();
                // var debits = parsedResults.Where(t => t.Amount < 0).ToList();
                
                // Console.WriteLine($"  Credits: {credits.Count} transactions, Total: {credits.Sum(t => t.Amount):C}");
                // Console.WriteLine($"  Debits:  {debits.Count} transactions, Total: {debits.Sum(t => t.Amount):C}");
                // Console.WriteLine("========================================\n");
                
                await GenericDataService.FilterAndInsertTransactionsAsync(parsedResults, ofxUploadRequest.AccountId);

                return Ok(new
                {
                    success = true,
                    message = $"Successfully processed {parsedResults.Count} transactions",
                    accountId = ofxUploadRequest.AccountId,
                    transactionCount = parsedResults.Count
                });
            }
            catch (NotSupportedException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return StatusCode(500, new { error = "Error processing file", details = ex.Message });
            }
        }
    }
}