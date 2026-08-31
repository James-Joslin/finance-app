import { afterEach, describe, expect, it } from 'vitest';
import {
    compactMoney,
    money,
    setFormatPreferences,
    shortDate,
    todayIso,
} from './format';

afterEach(() =>
    setFormatPreferences({
        currencyCode: 'GBP',
        locale: 'en-GB',
        timezone: 'Europe/London',
    })
);

describe('currency formatters', () => {
    it('ignores callback metadata passed as a second argument', () => {
        expect(compactMoney(1250, 4)).toContain('£');
        expect(money(1250, { index: 4 })).toContain('£');
    });

    it('still supports an explicit ISO currency code', () => {
        expect(money(12.5, 'EUR')).toContain('€');
    });

    it('uses saved regional preferences by default', () => {
        setFormatPreferences({
            currencyCode: 'EUR',
            locale: 'de-DE',
            timezone: 'Europe/Berlin',
        });
        expect(money(12.5)).toContain('€');
        expect(shortDate('2026-08-30')).toContain('Aug.');
    });

    it('calculates today in the saved household timezone', () => {
        setFormatPreferences({
            currencyCode: 'USD',
            locale: 'en-US',
            timezone: 'America/New_York',
        });
        expect(todayIso(new Date('2026-08-30T00:30:00Z'))).toBe('2026-08-29');
    });
});
