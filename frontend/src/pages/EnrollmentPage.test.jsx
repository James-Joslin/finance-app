import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import EnrollmentPage from './EnrollmentPage';

const mocks = vi.hoisted(() => ({
    mutateAsync: vi.fn(),
    setPreference: vi.fn(),
}));

vi.mock('../contexts/ThemeContext', () => ({
    useTheme: () => ({ resolved: 'light', setPreference: mocks.setPreference }),
}));

vi.mock('../lib/queries', () => ({
    mutations: { saveEnrollment: vi.fn() },
    queryKeys: {
        enrollment: ['enrollment'],
        settings: ['settings'],
        dashboard: ['dashboard'],
    },
    useFinovaMutation: () => ({
        mutateAsync: mocks.mutateAsync,
        isPending: false,
        error: null,
    }),
}));

describe('EnrollmentPage', () => {
    beforeEach(() => {
        mocks.mutateAsync.mockReset();
        mocks.mutateAsync.mockResolvedValue({ isEnrolled: true });
    });

    it('suggests a household name and submits trimmed profile details', async () => {
        render(<EnrollmentPage />);

        fireEvent.change(screen.getByLabelText('First name'), {
            target: { value: '  Alex  ' },
        });
        fireEvent.change(screen.getByLabelText('Last name'), {
            target: { value: 'Taylor' },
        });

        expect(screen.getByLabelText(/Household name/)).toHaveValue(
            'Taylor Household'
        );
        fireEvent.click(
            screen.getByRole('button', { name: /continue to finova/i })
        );

        await waitFor(() =>
            expect(mocks.mutateAsync).toHaveBeenCalledWith({
                firstName: 'Alex',
                lastName: 'Taylor',
                householdName: 'Taylor Household',
            })
        );
    });
});
