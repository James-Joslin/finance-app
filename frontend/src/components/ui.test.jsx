import { createRef, useState } from 'react';
import {
    act,
    cleanup,
    fireEvent,
    render,
    screen,
} from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
    FeedbackProvider,
    InlineError,
    Modal,
    PageState,
    useFeedback,
} from './ui';

afterEach(cleanup);

function ModalHarness({ initialFocusRef }) {
    const [open, setOpen] = useState(false);
    return (
        <>
            <button type="button" onClick={() => setOpen(true)}>
                Open editor
            </button>
            <Modal
                open={open}
                onClose={() => setOpen(false)}
                title="Edit account"
                copy="Update the account details."
                initialFocusRef={initialFocusRef}
            >
                <input
                    ref={initialFocusRef}
                    aria-label="Account name"
                    defaultValue=""
                />
                <button type="button">Save account</button>
            </Modal>
        </>
    );
}

function ToastTrigger() {
    const { notifySuccess } = useFeedback();
    return (
        <button type="button" onClick={() => notifySuccess('Profile saved.')}>
            Save
        </button>
    );
}

describe('Modal', () => {
    it('moves focus inside, traps Tab in both directions, and restores focus', () => {
        render(<ModalHarness />);
        const opener = screen.getByRole('button', { name: 'Open editor' });
        opener.focus();
        fireEvent.click(opener);

        const close = screen.getByRole('button', { name: 'Close dialog' });
        const save = screen.getByRole('button', { name: 'Save account' });
        expect(close).toHaveFocus();

        save.focus();
        fireEvent.keyDown(document, { key: 'Tab' });
        expect(close).toHaveFocus();

        fireEvent.keyDown(document, { key: 'Tab', shiftKey: true });
        expect(save).toHaveFocus();

        fireEvent.keyDown(document, { key: 'Escape' });
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        expect(opener).toHaveFocus();
    });

    it('honours an initial focus ref and exposes its title and description', () => {
        const initialFocusRef = createRef();
        render(<ModalHarness initialFocusRef={initialFocusRef} />);
        fireEvent.click(screen.getByRole('button', { name: 'Open editor' }));

        const dialog = screen.getByRole('dialog', {
            name: 'Edit account',
            description: 'Update the account details.',
        });
        expect(initialFocusRef.current).toHaveFocus();
        expect(dialog).toHaveAttribute('aria-modal', 'true');
    });

    it('uses unique accessible labels for multiple dialogs', () => {
        render(
            <>
                <Modal open title="First dialog" onClose={() => {}}>
                    First
                </Modal>
                <Modal open title="Second dialog" onClose={() => {}}>
                    Second
                </Modal>
            </>
        );
        const dialogs = screen.getAllByRole('dialog');
        expect(dialogs[0].getAttribute('aria-labelledby')).not.toBe(
            dialogs[1].getAttribute('aria-labelledby')
        );
    });
});

describe('feedback primitives', () => {
    afterEach(() => {
        vi.useRealTimers();
    });

    it('announces and dismisses success messages', () => {
        render(
            <FeedbackProvider>
                <ToastTrigger />
            </FeedbackProvider>
        );
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));
        expect(screen.getByRole('status')).toHaveTextContent('Profile saved.');

        fireEvent.click(
            screen.getByRole('button', { name: 'Dismiss notification' })
        );
        expect(screen.queryByRole('status')).not.toBeInTheDocument();
    });

    it('removes success messages after six seconds', () => {
        vi.useFakeTimers();
        render(
            <FeedbackProvider>
                <ToastTrigger />
            </FeedbackProvider>
        );
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        act(() => vi.advanceTimersByTime(6000));
        expect(screen.queryByRole('status')).not.toBeInTheDocument();
    });

    it('renders persistent errors as alerts', () => {
        render(<InlineError>Saving failed. Try again.</InlineError>);
        expect(screen.getByRole('alert')).toHaveTextContent(
            'Saving failed. Try again.'
        );
    });

    it('retries page failures and exposes the retrying state', () => {
        const retry = vi.fn();
        const { rerender } = render(
            <PageState error="Unable to load." onRetry={retry} />
        );
        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
        expect(retry).toHaveBeenCalledOnce();

        rerender(
            <PageState error="Unable to load." onRetry={retry} retrying />
        );
        expect(
            screen.getByRole('button', { name: 'Trying again…' })
        ).toBeDisabled();
    });
});
