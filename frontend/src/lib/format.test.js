import { describe, expect, it } from 'vitest';
import { compactMoney, money } from './format';

describe('currency formatters', () => {
    it('ignores callback metadata passed as a second argument', () => {
        expect(compactMoney(1250, 4)).toContain('£');
        expect(money(1250, { index: 4 })).toContain('£');
    });

    it('still supports an explicit ISO currency code', () => {
        expect(money(12.5, 'EUR')).toContain('€');
    });
});
