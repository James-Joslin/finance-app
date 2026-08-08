const compareOldestFirst = (a, b) =>
    a.dateTimestamp - b.dateTimestamp || a.databaseId - b.databaseId;

const compareNewestFirst = (a, b) =>
    b.dateTimestamp - a.dateTimestamp || b.databaseId - a.databaseId;

export const buildReportTransactions = (headers, rows) => {
    const parsedTransactions = rows.map((row, index) => {
        const transaction = {};
        headers.forEach((header, headerIndex) => {
            transaction[header] = row[headerIndex];
        });

        const amount = Number.parseFloat(transaction.amount) || 0;
        const parsedDatabaseId = Number(transaction.id);
        const databaseId = Number.isFinite(parsedDatabaseId)
            ? parsedDatabaseId
            : index;
        const dateTimestamp = Date.parse(transaction.transaction_date);

        if (Number.isNaN(dateTimestamp)) {
            throw new Error('Invalid transaction date returned by the API');
        }

        return {
            ...transaction,
            id: transaction.id || String(index),
            databaseId,
            amount,
            isDebit: amount < 0,
            displayAmount: Math.abs(amount),
            dateTimestamp,
            dateObj: new Date(dateTimestamp),
        };
    });

    const chronologicalTransactions = [...parsedTransactions].sort(
        compareOldestFirst
    );
    let runningBalance = 0;
    const balanceByTransactionId = new Map();

    chronologicalTransactions.forEach((transaction) => {
        runningBalance += transaction.amount;
        balanceByTransactionId.set(transaction.id, runningBalance);
    });

    return [...parsedTransactions]
        .sort(compareNewestFirst)
        .map((transaction) => ({
            ...transaction,
            runningBalance: balanceByTransactionId.get(transaction.id),
        }));
};
