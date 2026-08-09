import { render, screen } from '@testing-library/react';
import { beforeAll, describe, expect, it, vi } from 'vitest';
import InsightsPage from './InsightsPage';

vi.mock('../lib/queries', () => ({
    useInsights: () => ({
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
    }),
}));

beforeAll(() => {
    globalThis.ResizeObserver = class {
        observe() {}
        unobserve() {}
        disconnect() {}
    };
});

describe('InsightsPage', () => {
    it('renders incomplete chart collections without crashing', () => {
        render(<InsightsPage />);

        expect(screen.getByText('Net balance trend')).toBeInTheDocument();
        expect(screen.getByText('Insights for you')).toBeInTheDocument();
        expect(screen.getAllByText('£0.00').length).toBeGreaterThan(0);
    });
});
