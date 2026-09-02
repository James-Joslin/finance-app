import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import PlanPage from './PlanPage';

const mockBudgetState = vi.hoisted(() => ({
    months: { currentMonth: '2026-08-01', months: [] },
    budgets: [],
    mutateAsync: vi.fn().mockResolvedValue({}),
}));

vi.mock('../components/RecurringEditor', () => ({ default: () => null }));

afterEach(() => {
    cleanup();
    mockBudgetState.months = { currentMonth: '2026-08-01', months: [] };
    mockBudgetState.budgets = [];
    mockBudgetState.mutateAsync.mockClear();
    vi.restoreAllMocks();
});
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
    useBudgetMonths: () => query(mockBudgetState.months),
    useBudgets: () => query(mockBudgetState.budgets),
    useCategories: () => query([]),
    useFinovaMutation: () => ({
        mutate: vi.fn(),
        mutateAsync: mockBudgetState.mutateAsync,
        isPending: false,
        error: null,
    }),
    useOccurrences: () =>
        query([
            {
                id: 1,
                status: 'expected',
                itemName: 'Mortgage',
                accountName: 'Current account',
                kind: 'expense',
                dueDate: '2026-08-15',
                expectedAmount: 1200,
            },
        ]),
    useRecurring: () =>
        query([
            {
                id: 2,
                name: 'Payday',
                accountName: 'Current account',
                kind: 'income',
                frequency: 'monthly',
                nextDate: '2026-08-28',
                amount: 2500,
                isActive: true,
                source: 'manual',
            },
        ]),
    useSafety: () => query([]),
    useSuggestions: () => query([]),
}));

describe('PlanPage collapsible sections', () => {
    it('starts upcoming items and recurring schedules collapsed and toggles each independently', () => {
        render(
            <MemoryRouter>
                <PlanPage />
            </MemoryRouter>
        );

        const upcoming = screen.getByRole('button', {
            name: /upcoming bills and paydays/i,
        });
        const schedules = screen.getByRole('button', {
            name: /flexible household schedules/i,
        });

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
    it('opens recurring schedules for a deep-linked plan', () => {
        render(
            <MemoryRouter initialEntries={['/plan?recurringId=2']}>
                <PlanPage />
            </MemoryRouter>
        );

        const schedules = screen.getByRole('button', {
            name: /flexible household schedules/i,
        });
        expect(schedules).toHaveAttribute('aria-expanded', 'true');
        expect(screen.getByText('Payday')).toBeVisible();
    });
});

describe('PlanPage budget history and controls', () => {
    it('selects a month and finalizes its budget snapshot', () => {
        mockBudgetState.months = {
            currentMonth: '2026-08-01',
            months: [
                { month: '2026-08-01', isClosed: false, budgetCount: 1 },
                { month: '2026-07-01', isClosed: true, budgetCount: 1 },
            ],
        };
        mockBudgetState.budgets = [
            {
                id: 7,
                categoryName: 'Groceries',
                colorKey: 'mint',
                monthlyAmount: 500,
                rolloverEnabled: true,
                rolloverIn: 25,
                availableAmount: 525,
                spentAmount: 200,
                scheduledAmount: 0,
                remainingAfterScheduled: 325,
                remainingAmount: 325,
                progressPercent: 38.1,
                isActive: true,
                isClosed: false,
            },
        ];
        vi.spyOn(window, 'confirm').mockReturnValue(true);

        render(
            <MemoryRouter>
                <PlanPage />
            </MemoryRouter>
        );

        expect(screen.getByText('Groceries')).toBeVisible();
        fireEvent.click(screen.getByRole('button', { name: 'Close month' }));
        expect(mockBudgetState.mutateAsync).toHaveBeenCalledWith({
            month: '2026-08-01',
        });
    });

    it('renders finalized history read-only for inactive budgets', () => {
        mockBudgetState.months = {
            currentMonth: '2026-08-01',
            months: [
                { month: '2026-08-01', isClosed: false, budgetCount: 1 },
                { month: '2026-07-01', isClosed: true, budgetCount: 2 },
            ],
        };
        mockBudgetState.budgets = [
            {
                id: 8,
                categoryName: 'Travel',
                monthlyAmount: 300,
                availableAmount: 300,
                spentAmount: 300,
                remainingAmount: 0,
                remainingAfterScheduled: 0,
                progressPercent: 100,
                isActive: false,
                isClosed: true,
            },
        ];

        render(
            <MemoryRouter>
                <PlanPage />
            </MemoryRouter>
        );

        fireEvent.change(
            screen.getByRole('combobox', { name: 'Budget month' }),
            {
                target: { value: '2026-07-01' },
            }
        );
        expect(
            screen.getByText(
                'July 2026 is finalized. Its snapshot and rollover boundary are permanent.'
            )
        ).toBeVisible();
        expect(
            screen.queryByRole('button', { name: /reactivate travel/i })
        ).not.toBeInTheDocument();
        expect(
            screen.queryByRole('button', { name: /edit travel/i })
        ).not.toBeInTheDocument();
    });
});
