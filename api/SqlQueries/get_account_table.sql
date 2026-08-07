SELECT
    id,
    transaction_date,
    amount,
    payee,
    memo,
    category,
    source_file,
    created_at,
    fitid,
    transaction_type,
    check_number,
    source_file_type
FROM transactions
WHERE account_id = @accountId
ORDER BY transaction_date DESC, id DESC;
