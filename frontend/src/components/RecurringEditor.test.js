import { describe, expect, it } from 'vitest';
import { advanceRecurringDate } from './RecurringEditor.jsx';

describe('advanceRecurringDate', () => {
    it('preserves a normal monthly due day', () => {
        expect(advanceRecurringDate('2026-08-03', 'monthly')).toBe(
            '2026-09-03'
        );
    });

    it('clamps end-of-month bills without overflowing into another month', () => {
        expect(advanceRecurringDate('2026-08-31', 'monthly')).toBe(
            '2026-09-30'
        );
        expect(advanceRecurringDate('2024-02-29', 'yearly')).toBe('2025-02-28');
    });
});
