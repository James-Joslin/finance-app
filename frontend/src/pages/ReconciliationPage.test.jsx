import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup } from '@testing-library/react';
import ReconciliationPage from './ReconciliationPage';

const query = (data) => ({
    data,
    isLoading: false,
    isFetching: false,
    error: null,
    refetch: vi.fn(),
});
const mutate = vi.fn();
const mutation = () => ({ mutate, isPending: false, error: null });

const account = { id: 1, name: 'Everyday account', balance: 100 };
const session = {
    id: 7,
    accountId: 1,
    accountName: 'Everyday account',
    periodStart: '2026-08-01',
    periodEnd: '2026-08-31',
    statementOpeningBalance: 100,
    statementClosingBalance: 75,
    expectedOpeningBalance: 100,
    openingDiscrepancy: 0,
    clearedBalance: 80,
    closingDiscrepancy: -5,
    clearedTransactionCount: 1,
    transactionCount: 2,
    status: 'open',
    canClose: false,
};

vi.mock('../lib/queries', () => ({
    useAccounts: () => query([account]),
}));

vi.mock('../lib/reconciliationQueries', () => ({
    reconciliationMutations: {
        create: vi.fn(),
        setCleared: vi.fn(),
        adjustment: vi.fn(),
        deleteAdjustment: vi.fn(),
        close: vi.fn(),
    },
    useReconciliationSessions: () => query([session]),
    useStatementSession: () =>
        query({
            session,
            transactions: [
                {
                    id: 10,
                    date: '2026-08-10',
                    amount: -20,
                    payee: 'Coffee shop',
                    memo: null,
                    categoryName: 'Food & Groceries',
                    status: 'completed',
                    isCleared: true,
                    isReconciliationAdjustment: false,
                },
                {
                    id: 11,
                    date: '2026-08-20',
                    amount: -5,
                    payee: 'Book shop',
                    memo: null,
                    categoryName: 'Shopping',
                    status: 'completed',
                    isCleared: false,
                    isReconciliationAdjustment: false,
                },
            ],
        }),
    useReconciliationMutation: () => mutation(),
}));

describe('statement reconciliation', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });
    afterEach(cleanup);

    it('shows balance discrepancies and prevents closing until resolved', () => {
        render(<ReconciliationPage />);

        expect(screen.getByText('Opening balance check')).toBeInTheDocument();
        expect(screen.getByText(/£5\.00 difference/)).toBeInTheDocument();
        expect(
            screen.getByRole('button', { name: 'Close session' })
        ).toBeDisabled();
        expect(
            screen.getByRole('button', { name: /create adjustment/i })
        ).toBeInTheDocument();
    });

    it('allows statement transactions to be cleared individually', () => {
        render(<ReconciliationPage />);

        const checkbox = screen.getByRole('checkbox', {
            name: 'Clear Book shop',
        });
        expect(checkbox).not.toBeChecked();
        fireEvent.click(checkbox);
        expect(mutate).toHaveBeenCalledWith({
            sessionId: 7,
            transactionId: 11,
            cleared: true,
        });
    });
});
