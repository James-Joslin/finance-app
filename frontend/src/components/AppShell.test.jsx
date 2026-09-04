import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import AppShell from './AppShell';

vi.mock('../contexts/ThemeContext', () => ({
    useTheme: () => ({ resolved: 'light', setPreference: vi.fn() }),
}));
vi.mock('../lib/queries', () => ({
    searchFinova: vi.fn(),
    useDashboard: () => ({
        data: { householdName: 'Test Household', alerts: [] },
    }),
    useEnrollmentStatus: () => ({
        data: { profile: { firstName: 'Taylor', lastName: 'Household' } },
    }),
    useSettings: () => ({
        data: {
            currencyCode: 'GBP',
            locale: 'en-GB',
            timezone: 'Europe/London',
        },
    }),
}));

afterEach(cleanup);

describe('AppShell support navigation', () => {
    it('routes Help & support into Finova instead of generic GitHub', () => {
        render(
            <MemoryRouter initialEntries={['/help']}>
                <Routes>
                    <Route element={<AppShell />}>
                        <Route path="*" element={<div />} />
                    </Route>
                </Routes>
            </MemoryRouter>
        );

        const helpLink = screen.getByRole('link', { name: 'Help & support' });
        expect(helpLink).toHaveAttribute('href', '/help');
        expect(helpLink).not.toHaveAttribute('target');
    });
});
