import {
    cleanup,
    fireEvent,
    render,
    screen,
    waitFor,
    within,
} from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import SettingsPage from './SettingsPage';

const state = vi.hoisted(() => ({
    enrollmentData: { profile: { firstName: 'Taylor', lastName: 'Household' } },
    settingsData: {
        householdName: 'Taylor Household',
        currencyCode: 'GBP',
        locale: 'en-GB',
        timezone: 'Europe/London',
    },
    categories: [],
    rules: [],
    mutation: {
        mutate: vi.fn(),
        mutateAsync: vi.fn().mockResolvedValue({}),
        isPending: false,
        error: null,
    },
}));

vi.mock('../components/PortabilityCard', () => ({ default: () => null }));
vi.mock('../contexts/ThemeContext', () => ({
    useTheme: () => ({ preference: 'system', setPreference: vi.fn() }),
}));
vi.mock('../lib/queries', () => ({
    mutations: {
        saveEnrollment: vi.fn(),
        saveSettings: vi.fn(),
        deleteTransactionRule: vi.fn(),
        updateCategory: vi.fn(),
        createCategory: vi.fn(),
        deleteCategory: vi.fn(),
        updateTransactionRule: vi.fn(),
        createTransactionRule: vi.fn(),
    },
    queryKeys: {
        enrollment: ['enrollment'],
        settings: ['settings'],
        accounts: ['accounts'],
        categories: ['categories'],
        rules: ['category-rules'],
        dashboard: ['dashboard'],
    },
    useAccounts: () => ({ data: [], isLoading: false, error: null }),
    useCategories: () => ({
        data: state.categories,
        isLoading: false,
        error: null,
    }),
    useEnrollmentStatus: () => ({
        data: state.enrollmentData,
        isLoading: false,
        error: null,
    }),
    useFinovaMutation: () => state.mutation,
    useSettings: () => ({
        data: state.settingsData,
        isLoading: false,
        error: null,
    }),
    useTransactionRules: () => ({
        data: state.rules,
        isLoading: false,
        error: null,
    }),
}));

function resetState() {
    state.categories = [
        {
            id: 1,
            name: 'Income',
            kind: 'income',
            iconKey: 'wallet-cards',
            colorKey: 'mint',
            isSystem: true,
            isArchived: false,
        },
        {
            id: 2,
            name: 'Groceries',
            kind: 'expense',
            iconKey: 'shopping-basket',
            colorKey: 'amber',
            isSystem: false,
            isArchived: false,
        },
        {
            id: 3,
            name: 'Archived category',
            kind: 'expense',
            iconKey: 'tag',
            colorKey: 'slate',
            isSystem: false,
            isArchived: true,
        },
    ];
    state.rules = [
        {
            id: 7,
            referenceText: 'Netflix',
            direction: 'out',
            categoryId: 2,
            categoryName: 'Groceries',
            priority: 100,
            isActive: true,
        },
    ];
    state.mutation.mutate.mockReset();
    state.mutation.mutateAsync.mockReset().mockResolvedValue({});
}

function renderSettings() {
    return render(
        <MemoryRouter>
            <SettingsPage />
        </MemoryRouter>
    );
}

beforeEach(resetState);
afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
});

describe('Settings category management', () => {
    it('creates a category and protects system categories', async () => {
        renderSettings();
        const system = screen.getByText('Income').closest('article');
        expect(within(system).queryByRole('button')).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: 'Add category' }));
        const dialog = screen.getByRole('dialog');
        fireEvent.change(screen.getByLabelText('Name'), {
            target: { value: 'Travel' },
        });
        fireEvent.change(screen.getByLabelText('Colour'), {
            target: { value: 'cyan' },
        });
        fireEvent.change(screen.getByLabelText(/Icon key/), {
            target: { value: 'plane' },
        });
        fireEvent.click(
            within(dialog).getByRole('button', { name: 'Save category' })
        );

        await waitFor(() =>
            expect(state.mutation.mutateAsync).toHaveBeenCalledWith({
                body: {
                    name: 'Travel',
                    kind: 'expense',
                    iconKey: 'plane',
                    colorKey: 'cyan',
                    isArchived: false,
                },
            })
        );
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('edits, archives, restores, and deletes a custom category', async () => {
        const { rerender } = renderSettings();
        fireEvent.click(screen.getByRole('button', { name: 'Edit Groceries' }));
        const dialog = screen.getByRole('dialog');
        fireEvent.change(screen.getByLabelText('Name'), {
            target: { value: 'Food' },
        });
        fireEvent.click(screen.getByLabelText(/Archive category/));
        fireEvent.click(
            within(dialog).getByRole('button', { name: 'Save category' })
        );
        await waitFor(() =>
            expect(state.mutation.mutateAsync).toHaveBeenCalledWith({
                id: 2,
                body: expect.objectContaining({
                    name: 'Food',
                    isArchived: true,
                }),
            })
        );

        state.categories = state.categories.map((category) =>
            category.id === 2
                ? { ...category, name: 'Food', isArchived: true }
                : category
        );
        rerender(
            <MemoryRouter>
                <SettingsPage />
            </MemoryRouter>
        );
        fireEvent.click(screen.getByRole('button', { name: 'Restore Food' }));
        expect(state.mutation.mutate).toHaveBeenCalledWith({
            id: 2,
            body: expect.objectContaining({ name: 'Food', isArchived: false }),
        });
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        fireEvent.click(screen.getByRole('button', { name: 'Delete Food' }));
        expect(state.mutation.mutate).toHaveBeenCalledWith(2);
    });
});

describe('Settings automatic category rules', () => {
    it('creates a rule with direction, priority, and category controls', async () => {
        renderSettings();
        fireEvent.click(screen.getByRole('button', { name: 'Add rule' }));
        const dialog = screen.getByRole('dialog');
        fireEvent.change(screen.getByLabelText('Reference'), {
            target: { value: 'Acme' },
        });
        fireEvent.change(screen.getByLabelText('Direction'), {
            target: { value: 'in' },
        });
        fireEvent.change(screen.getByLabelText('Priority'), {
            target: { value: '12' },
        });
        fireEvent.change(screen.getByLabelText('Category'), {
            target: { value: '2' },
        });
        fireEvent.click(
            within(dialog).getByRole('button', { name: 'Save rule' })
        );
        await waitFor(() =>
            expect(state.mutation.mutateAsync).toHaveBeenCalledWith({
                body: {
                    matchText: 'Acme',
                    direction: 'in',
                    categoryId: 2,
                    priority: 12,
                    isActive: true,
                },
            })
        );
    });

    it('edits all rule controls, filters archived categories, and deletes rules', async () => {
        renderSettings();
        fireEvent.click(
            screen.getByRole('button', {
                name: 'Edit automatic category for Netflix',
            })
        );
        const dialog = screen.getByRole('dialog');
        fireEvent.change(screen.getByLabelText('Direction'), {
            target: { value: 'in' },
        });
        fireEvent.change(screen.getByLabelText('Priority'), {
            target: { value: '4' },
        });
        fireEvent.change(screen.getByLabelText('Category'), {
            target: { value: '1' },
        });
        fireEvent.click(screen.getByLabelText(/Rule active/));
        fireEvent.click(
            within(dialog).getByRole('button', { name: 'Save rule' })
        );
        await waitFor(() =>
            expect(state.mutation.mutateAsync).toHaveBeenCalledWith({
                id: 7,
                body: {
                    matchText: 'Netflix',
                    direction: 'in',
                    categoryId: 1,
                    priority: 4,
                    isActive: false,
                },
            })
        );

        fireEvent.click(screen.getByRole('button', { name: 'Add rule' }));
        expect(
            within(screen.getByRole('dialog')).queryByRole('option', {
                name: 'Archived category',
            })
        ).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Close dialog' }));
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        fireEvent.click(
            screen.getByRole('button', {
                name: 'Forget automatic category for Netflix',
            })
        );
        expect(state.mutation.mutate).toHaveBeenCalledWith(7);
    });
});
