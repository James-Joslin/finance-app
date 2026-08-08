import test from 'node:test';
import assert from 'node:assert/strict';

import { buildReportTransactions } from './reportTransactions.js';

test('sorts newest first and keeps chronological balances on the correct rows', () => {
    // Column order is deliberately shuffled to catch position-based reads.
    const headers = ['payee', 'amount', 'transaction_date', 'id'];
    const rows = [
        ['Opening', '100.00', '2026-01-01T00:00:00.0000000', '101'],
        ['Debit', '-20.00', '2026-02-01T00:00:00.0000000', '102'],
        ['Credit', '5.00', '2026-02-01T00:00:00.0000000', '103'],
        ['Latest', '-10.00', '2026-03-01T00:00:00.0000000', '104'],
    ];

    const transactions = buildReportTransactions(headers, rows);

    assert.deepEqual(
        transactions.map((transaction) => transaction.id),
        ['104', '103', '102', '101']
    );
    assert.deepEqual(
        transactions.map((transaction) => transaction.runningBalance),
        [75, 85, 80, 100]
    );
    assert.deepEqual(
        transactions.map((transaction) => transaction.amount),
        [-10, 5, -20, 100]
    );
});

test('rejects locale-dependent or invalid dates instead of silently mis-sorting', () => {
    assert.throws(
        () =>
            buildReportTransactions(
                ['id', 'transaction_date', 'amount'],
                [['1', 'not-a-date', '10.00']]
            ),
        /Invalid transaction date/
    );
});
