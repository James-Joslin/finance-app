import { describe, expect, it } from 'vitest';
import { overviewTrendRange } from './trendRange';

describe('overviewTrendRange', () => {
    it('anchors the chart to the latest actual transaction date', () => {
        expect(
            overviewTrendRange(
                [{ date: '2026-01-12' }, { date: '2026-01-29' }],
                new Date('2026-08-09T12:00:00Z')
            )
        ).toEqual({
            startDate: '2025-12-31',
            endDate: '2026-01-29',
        });
    });

    it('uses today when there is no transaction history', () => {
        expect(
            overviewTrendRange([], new Date('2026-08-09T12:00:00Z'))
        ).toEqual({
            startDate: '2026-07-11',
            endDate: '2026-08-09',
        });
    });

    it('ignores invalid and future transaction dates', () => {
        expect(
            overviewTrendRange(
                [
                    { date: 'invalid' },
                    { date: '2026-09-01' },
                    { date: '2026-08-05' },
                ],
                new Date('2026-08-09T12:00:00Z')
            ).endDate
        ).toBe('2026-08-05');
    });
});
