import {
    cleanup,
    fireEvent,
    render,
    screen,
    waitFor,
} from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import PortabilityCard from './PortabilityCard';

const mocks = vi.hoisted(() => ({
    mutateAsync: vi.fn(),
    confirm: vi.fn(),
}));

vi.mock('../lib/queries', () => ({
    mutations: { importPortableArchive: vi.fn() },
    queryKeys: {
        enrollment: ['enrollment'],
        settings: ['settings'],
        accounts: ['accounts'],
        categories: ['categories'],
        rules: ['rules'],
        dashboard: ['dashboard'],
        goals: ['goals'],
        recurring: ['recurring'],
        occurrences: ['occurrences'],
        budgets: ['budgets'],
        safety: ['safety'],
        transactionsRoot: ['transactions'],
        importsRoot: ['imports'],
        insightsRoot: ['insights'],
    },
    useFinovaMutation: () => ({
        mutateAsync: mocks.mutateAsync,
        isPending: false,
        error: null,
    }),
}));

describe('PortabilityCard', () => {
    beforeEach(() => {
        mocks.mutateAsync.mockReset();
        mocks.mutateAsync.mockResolvedValue({
            records: { accounts: 2, transactions: 7 },
            images: 1,
        });
        mocks.confirm.mockReset().mockReturnValue(true);
        vi.stubGlobal('confirm', mocks.confirm);
    });

    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
    });

    it('exposes the full and individual lossless downloads', () => {
        render(<PortabilityCard />);

        expect(
            screen.getByRole('link', { name: /export full archive/i })
        ).toHaveAttribute('href', '/api/portability/export/archive');
        expect(screen.getByRole('link', { name: 'Accounts' })).toHaveAttribute(
            'href',
            '/api/portability/export/accounts'
        );
        expect(screen.getByRole('link', { name: 'Images' })).toHaveAttribute(
            'href',
            '/api/portability/export/images'
        );
    });

    it('requires confirmation and posts the selected archive', async () => {
        render(<PortabilityCard />);
        const file = new File(['archive'], 'household.zip', {
            type: 'application/zip',
        });
        fireEvent.change(screen.getByLabelText('Restore full archive'), {
            target: { files: [file] },
        });
        fireEvent.click(
            screen.getByRole('button', { name: /restore archive/i })
        );

        await waitFor(() => expect(mocks.mutateAsync).toHaveBeenCalledOnce());
        expect(mocks.confirm).toHaveBeenCalledOnce();
        const form = mocks.mutateAsync.mock.calls[0][0];
        expect(form.get('archive')).toBe(file);
        expect(
            screen.getByText(/restored 9 records and 1 images/i)
        ).toBeInTheDocument();
    });
});
