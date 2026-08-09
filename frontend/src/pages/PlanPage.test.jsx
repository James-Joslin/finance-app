import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import PlanPage from './PlanPage';

vi.mock('../components/RecurringEditor', () => ({ default: () => null }));
vi.mock('../components/OccurrenceEditor', () => ({ default: () => null }));

const query = (data) => ({ data, isLoading: false, error: null });

vi.mock('../lib/queries', () => ({
    mutations: {
        createRecurring: vi.fn(),
        saveBudget: vi.fn(),
    },
    queryKeys: {
        recurring: ['recurring'],
        suggestions: ['recurring-suggestions'],
        safety: ['safety'],
        dashboard: ['dashboard'],
        budgets: ['budgets'],
    },
    useAccounts: () => query([]),
    useBudgets: () => query([]),
    useCategories: () => query([]),
    useFinovaMutation: () => ({
        mutate: vi.fn(),
        mutateAsync: vi.fn(),
        isPending: false,
        error: null,
    }),
    useOccurrences: () => query([{
        id: 1,
        status: 'expected',
        itemName: 'Mortgage',
        accountName: 'Current account',
        kind: 'expense',
        dueDate: '2026-08-15',
        expectedAmount: 1200,
    }]),
    useRecurring: () => query([{
        id: 2,
        name: 'Payday',
        accountName: 'Current account',
        kind: 'income',
        frequency: 'monthly',
        nextDate: '2026-08-28',
        amount: 2500,
        isActive: true,
        source: 'manual',
    }]),
    useSafety: () => query([]),
    useSuggestions: () => query([]),
}));

describe('PlanPage collapsible sections', () => {
    it('starts upcoming items and recurring schedules collapsed and toggles each independently', () => {
        render(<PlanPage />);

        const upcoming = screen.getByRole('button', { name: /upcoming bills and paydays/i });
        const schedules = screen.getByRole('button', { name: /flexible household schedules/i });

        expect(upcoming).toHaveAttribute('aria-expanded', 'false');
        expect(schedules).toHaveAttribute('aria-expanded', 'false');
        expect(screen.getByText('Mortgage')).not.toBeVisible();
        expect(screen.getByText('Payday')).not.toBeVisible();

        fireEvent.click(upcoming);
        expect(upcoming).toHaveAttribute('aria-expanded', 'true');
        expect(screen.getByText('Mortgage')).toBeVisible();
        expect(screen.getByText('Payday')).not.toBeVisible();

        fireEvent.click(schedules);
        expect(schedules).toHaveAttribute('aria-expanded', 'true');
        expect(screen.getByText('Payday')).toBeVisible();
    });
});
