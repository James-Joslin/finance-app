using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;

using financesApi.models;

namespace financesApi.utilities
{
    public static class FilterBuilder
    {
        public static string BuildFilter(TransactionQueryRequest queryParameters)
        {
            StringBuilder filter = new StringBuilder();
            bool hasCondition = false;

            if (queryParameters.accountName != null)
            {
                filter.Append("WHERE client_theseus_id = ").Append(queryParameters.accountName).Append(' ');
                hasCondition = true;
            }

            return filter.ToString();
        }
    }
}