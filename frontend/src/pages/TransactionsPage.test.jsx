import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ImportRows, ImportSummary } from './TransactionsPage';

describe('transaction imports', () => {
    it('shows separate preview totals', () => {
        render(<ImportSummary batch={{
            accountName: 'Everyday account',
            fileName: 'statement.qif',
            status: 'preview',
            importable: 3,
            imported: 0,
            skipped: 2,
            rejected: 1,
            total: 6,
        }} />);

        expect(screen.getByText('3')).toBeInTheDocument();
        expect(screen.getByText('Ready')).toBeInTheDocument();
        expect(screen.getByText('Duplicates')).toBeInTheDocument();
        expect(screen.getByText('Rejected')).toBeInTheDocument();
    });

    it('renders a row-level rejection reason and source position', () => {
        render(<ImportRows page={1} onPage={() => {}} query={{
            isLoading: false,
            error: null,
            data: {
                items: [{
                    id: 2,
                    ordinal: 2,
                    sourceLabel: 'QIF transaction 2',
                    date: null,
                    displayDate: '31/08/2099',
                    amount: null,
                    displayAmount: 'not-money',
                    balanceAfter: 248.77,
                    payee: 'Broken row',
                    memo: null,
                    outcome: 'rejected',
                    reasonMessage: 'QIF transaction 2 has an invalid amount.',
                }],
                totalPages: 1,
                totalItems: 1,
            },
        }} />);

        expect(screen.getByText(/QIF transaction 2 has an invalid amount/)).toBeInTheDocument();
        expect(screen.getByText('QIF transaction 2 · 31/08/2099')).toBeInTheDocument();
        expect(screen.getByText('rejected')).toBeInTheDocument();
        expect(screen.getByText('Balance after')).toBeInTheDocument();
        expect(screen.getByText(/248\.77/)).toBeInTheDocument();
    });
});
