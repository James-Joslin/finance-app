import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import axios from '../api/api';
import { setFormatPreferences } from '../lib/format';
import TransactionReviewForm from './TransactionReviewForm';

vi.mock('../api/api', () => ({
    default: {
        post: vi.fn(),
    },
}));

vi.mock('../contexts/useAccounts', () => ({
    useAccounts: () => ({
        selectedAccountId: 1,
        accounts: [{ id: 1, name: 'Everyday account' }],
    }),
}));

describe('TransactionReviewForm regional formatting', () => {
    beforeEach(() => {
        setFormatPreferences({
            currencyCode: 'EUR',
            locale: 'de-DE',
            timezone: 'Pacific/Auckland',
        });
        axios.post.mockResolvedValue({
            data: {
                Headers: [
                    ['id', 'transaction_date', 'amount', 'payee', 'memo'],
                ],
                Rows: [
                    ['1', '2026-08-30T00:00:00.0000000', '12.50', 'Salary', ''],
                ],
            },
        });
    });

    afterEach(() => {
        vi.clearAllMocks();
        setFormatPreferences({
            currencyCode: 'GBP',
            locale: 'en-GB',
            timezone: 'Europe/London',
        });
    });

    it('uses household currency and locale for summaries and transaction rows', async () => {
        render(<TransactionReviewForm />);

        await waitFor(() => expect(axios.post).toHaveBeenCalledOnce());

        expect(
            screen.getByText('Total Credits').parentElement
        ).toHaveTextContent('12,50 €');
        expect(
            screen.getByText('Total Debits').parentElement
        ).toHaveTextContent('0,00 €');
        expect(screen.getByText('Net Balance').parentElement).toHaveTextContent(
            '12,50 €'
        );

        const row = screen.getByText('Salary').closest('tr');
        const cells = within(row).getAllByRole('cell');
        expect(cells[0]).toHaveTextContent('30. Aug. 2026');
        expect(cells[3]).toHaveTextContent('+12,50 €');
        expect(cells[4]).toHaveTextContent('12,50 €');
    });
});
