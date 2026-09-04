import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import HelpPage from './HelpPage';

afterEach(cleanup);

function renderHelp() {
    return render(
        <MemoryRouter>
            <HelpPage />
        </MemoryRouter>
    );
}

describe('HelpPage', () => {
    it('explains the core household workflows', () => {
        renderHelp();

        expect(
            screen.getByRole('heading', {
                name: 'A calmer way to understand your household money.',
            })
        ).toBeInTheDocument();
        expect(
            screen.getByRole('heading', { name: 'Import and review activity' })
        ).toBeInTheDocument();
        expect(screen.getByText(/accepts OFX, QIF/i)).toBeInTheDocument();
        expect(
            screen.getByRole('heading', {
                name: 'When something does not look right',
            })
        ).toBeInTheDocument();
    });

    it('links users to the relevant Finova pages', () => {
        renderHelp();

        const settingsLinks = screen.getAllByRole('link', {
            name: /open settings/i,
        });
        expect(settingsLinks).toHaveLength(2);
        expect(settingsLinks[0]).toHaveAttribute('href', '/settings');
        expect(settingsLinks[1]).toHaveAttribute('href', '/settings');
        expect(
            screen.getByRole('link', { name: /open transactions/i })
        ).toHaveAttribute('href', '/transactions');
        expect(
            screen.getByRole('link', { name: /open plan/i })
        ).toHaveAttribute('href', '/plan');
        expect(
            screen.getByRole('link', { name: /open goals/i })
        ).toHaveAttribute('href', '/goals');
        expect(
            screen.getByRole('link', { name: /open reconciliation/i })
        ).toHaveAttribute('href', '/reconciliation');
    });
});
