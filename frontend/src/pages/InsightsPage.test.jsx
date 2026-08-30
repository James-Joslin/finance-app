import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import InsightsPage from './InsightsPage';

const { useInsightsMock } = vi.hoisted(() => ({ useInsightsMock: vi.fn() }));

vi.mock('../lib/queries', () => ({
    useInsights: (params) => useInsightsMock(params),
}));

const insightsResult = {
        data: {
            startDate: '2026-08-01',
            endDate: '2026-08-08',
            totalBalance: 0,
            income: 0,
            spending: 0,
            netSavings: 0,
            savingsRate: 0,
            balanceTrend: null,
            categorySpending: null,
            incomeTrend: null,
            spendingTrend: null,
            goalProgressPercent: 0,
            uncategorisedSpending: 0,
        },
        isLoading: false,
        error: null,
    };

beforeAll(() => {
    globalThis.ResizeObserver = class {
        observe() {}
        unobserve() {}
        disconnect() {}
    };
});

beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-08-30T12:00:00Z'));
    useInsightsMock.mockReset();
    useInsightsMock.mockReturnValue(insightsResult);
});

afterEach(() => {
    cleanup();
    vi.useRealTimers();
});

describe('InsightsPage', () => {
    it('renders incomplete chart collections without crashing', () => {
        render(<InsightsPage />);

        expect(screen.getByText('Net balance trend')).toBeInTheDocument();
        expect(screen.getByText('Insights for you')).toBeInTheDocument();
        expect(screen.getAllByText('£0.00').length).toBeGreaterThan(0);
    });

    it('supports rolling, previous-year, and all-time presets', () => {
        render(<InsightsPage />);

        fireEvent.click(screen.getByRole('button', { name: 'Last 30 days' }));
        expect(useInsightsMock).toHaveBeenLastCalledWith({ startDate: '2026-08-01', endDate: '2026-08-30' });

        fireEvent.click(screen.getByRole('button', { name: 'Last 90 days' }));
        expect(useInsightsMock).toHaveBeenLastCalledWith({ startDate: '2026-06-02', endDate: '2026-08-30' });

        fireEvent.click(screen.getByRole('button', { name: 'Previous year' }));
        expect(useInsightsMock).toHaveBeenLastCalledWith({ startDate: '2025-01-01', endDate: '2025-12-31' });

        fireEvent.click(screen.getByRole('button', { name: 'All time' }));
        expect(useInsightsMock).toHaveBeenLastCalledWith({ allTime: true, endDate: '2026-08-30' });
    });

    it('applies a validated custom date range', () => {
        render(<InsightsPage />);
        fireEvent.click(screen.getByRole('button', { name: 'Custom' }));
        fireEvent.change(screen.getByLabelText('Start date'), { target: { value: '2026-04-10' } });
        fireEvent.change(screen.getByLabelText('End date'), { target: { value: '2026-05-20' } });
        fireEvent.click(screen.getByRole('button', { name: 'Apply range' }));

        expect(useInsightsMock).toHaveBeenLastCalledWith({ startDate: '2026-04-10', endDate: '2026-05-20' });
    });
});
