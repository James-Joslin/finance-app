using System;

// Models/TransactionKeys.cs
namespace financesApi.models
{
    public class TransactionKey : IEquatable<TransactionKey>
    {
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string Payee { get; set; } = string.Empty;
        public string? Memo { get; set; }
        public string? FitId { get; set; }
        public string? TransType { get; set; }

        public override bool Equals(object? obj) => Equals(obj as TransactionKey);

        public bool Equals(TransactionKey? other)
        {
            if (other == null) return false;

            // For OFX: Use FitId if available
            if (!string.IsNullOrEmpty(FitId) && !string.IsNullOrEmpty(other.FitId))
                return FitId == other.FitId;

            // For QIF or fallback: Use date, amount, and payee
            return Date == other.Date &&
                   Amount == other.Amount &&
                   Payee == other.Payee;
        }

        public override int GetHashCode()
        {
            // If FitId exists, use it for hash
            if (!string.IsNullOrEmpty(FitId))
                return FitId.GetHashCode();

            // Otherwise use combination of date, amount, and payee
            return HashCode.Combine(Date, Amount, Payee);
        }
    }
}
