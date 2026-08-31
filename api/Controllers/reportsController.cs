using Microsoft.AspNetCore.Mvc;
// using Microsoft.AspNetCore.Http.HttpResults;
using System.Data;

using financesApi.services;
using financesApi.models;
using financesApi.utilities;


namespace financesApi.controllers
{
    [ApiController]
    [Route("[controller]")]
    public class reportsController : ControllerBase
    {
        [HttpPost("getAccountTable")]
        public async Task<IActionResult> getAccountTable(TransactionQueryRequest requestParameters)
        {
            if (!requestParameters.accountId.HasValue) return BadRequest(new { error = "Account ID is required." });
            var parameters = new Dictionary<string, object>
            {
                { "@accountId", requestParameters.accountId.Value },
            };
            DataTable accountTable = await GenericDataService.ExecuteParameterisedQueryAsync(queryPath: "get_account_table", parameters);
            return Ok(DataEditor.ConvertData(accountTable));
        }
    }
}
