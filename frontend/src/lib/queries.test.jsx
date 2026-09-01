import { useState } from 'react';
import {
    act,
    cleanup,
    fireEvent,
    render,
    screen,
    waitFor,
} from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FeedbackProvider, InlineError } from '../components/ui';
import { useFinovaMutation } from './queries';

afterEach(cleanup);

function MutationHarness({ mutationFn }) {
    const [closed, setClosed] = useState(false);
    const mutation = useFinovaMutation(mutationFn, [['household']], {
        successMessage: 'Household saved.',
    });
    if (closed) return <span>Closed</span>;
    return (
        <form
            onSubmit={async (event) => {
                event.preventDefault();
                try {
                    await mutation.mutateAsync({
                        name: 'Taylor Household',
                    });
                    setClosed(true);
                } catch {
                    // The mutation owns and renders the error state.
                }
            }}
        >
            <InlineError>
                {mutation.error && mutation.error.message}
            </InlineError>
            <button disabled={mutation.isPending}>
                {mutation.isPending ? 'Saving…' : 'Save household'}
            </button>
        </form>
    );
}

function renderMutation(mutationFn) {
    const client = new QueryClient({
        defaultOptions: { mutations: { retry: false } },
    });
    const invalidate = vi
        .spyOn(client, 'invalidateQueries')
        .mockResolvedValue(undefined);
    render(
        <QueryClientProvider client={client}>
            <FeedbackProvider>
                <MutationHarness mutationFn={mutationFn} />
            </FeedbackProvider>
        </QueryClientProvider>
    );
    return { invalidate };
}

describe('useFinovaMutation feedback', () => {
    it('invalidates data, closes the form, and announces success', async () => {
        const mutationFn = vi.fn().mockResolvedValue({ ok: true });
        const { invalidate } = renderMutation(mutationFn);
        fireEvent.click(screen.getByRole('button', { name: 'Save household' }));

        expect(await screen.findByText('Household saved.')).toBeInTheDocument();
        expect(screen.getByText('Closed')).toBeInTheDocument();
        expect(invalidate).toHaveBeenCalledWith({
            queryKey: ['household'],
        });
    });

    it('blocks repeat submission while pending', async () => {
        let resolve;
        const mutationFn = vi.fn(() => new Promise((done) => (resolve = done)));
        renderMutation(mutationFn);
        fireEvent.click(screen.getByRole('button', { name: 'Save household' }));

        const pending = await screen.findByRole('button', {
            name: 'Saving…',
        });
        expect(pending).toBeDisabled();
        fireEvent.click(pending);
        expect(mutationFn).toHaveBeenCalledOnce();
        await act(async () => resolve({ ok: true }));
        await screen.findByText('Closed');
    });

    it('keeps a failed form open with an inline error', async () => {
        renderMutation(vi.fn().mockRejectedValue(new Error('Save failed.')));
        fireEvent.click(screen.getByRole('button', { name: 'Save household' }));

        expect(await screen.findByRole('alert')).toHaveTextContent(
            'Save failed.'
        );
        expect(
            screen.getByRole('button', { name: 'Save household' })
        ).toBeEnabled();
        expect(screen.queryByText('Closed')).not.toBeInTheDocument();
        await waitFor(() =>
            expect(screen.queryByRole('status')).not.toBeInTheDocument()
        );
    });
});
