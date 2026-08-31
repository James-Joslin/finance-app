import { useEffect, useState } from 'react';
import { Field, Modal } from './ui';
import { apiError, money } from '../lib/format';
import { mutations, queryKeys, useFinovaMutation } from '../lib/queries';

export default function OccurrenceEditor({ occurrence, onClose }) {
    const [form, setForm] = useState({
        dueDate: '',
        expectedAmount: '',
        status: 'expected',
        note: '',
    });
    const save = useFinovaMutation(mutations.updateOccurrence, [
        queryKeys.occurrences,
        queryKeys.recurring,
        queryKeys.safety,
        queryKeys.dashboard,
        queryKeys.budgets,
    ]);
    useEffect(() => {
        if (!occurrence) return;
        setForm({
            dueDate: occurrence.dueDate,
            expectedAmount: String(occurrence.expectedAmount),
            status:
                occurrence.status === 'matched' ? 'paid' : occurrence.status,
            note: occurrence.note || '',
        });
    }, [occurrence]);
    const submit = async (event) => {
        event.preventDefault();
        await save.mutateAsync({
            id: occurrence.id,
            body: {
                dueDate: form.dueDate,
                expectedAmount: Number(form.expectedAmount),
                status: form.status,
                note: form.note.trim() || null,
            },
        });
        onClose();
    };
    return (
        <Modal
            open={Boolean(occurrence)}
            onClose={onClose}
            title="Edit this occurrence"
            copy={
                occurrence
                    ? `${occurrence.itemName} · currently ${money(occurrence.expectedAmount)}`
                    : ''
            }
        >
            <form className="form-grid" onSubmit={submit}>
                <Field label="Due date">
                    <input
                        required
                        type="date"
                        value={form.dueDate}
                        onChange={(event) =>
                            setForm({ ...form, dueDate: event.target.value })
                        }
                    />
                </Field>
                <Field label="Expected amount">
                    <input
                        required
                        min="0"
                        step="0.01"
                        type="number"
                        value={form.expectedAmount}
                        onChange={(event) =>
                            setForm({
                                ...form,
                                expectedAmount: event.target.value,
                            })
                        }
                    />
                </Field>
                <Field
                    label="Status"
                    hint="Paid manually is useful for cash or bills outside an import."
                >
                    <select
                        value={form.status}
                        onChange={(event) =>
                            setForm({ ...form, status: event.target.value })
                        }
                    >
                        <option value="expected">Expected</option>
                        <option value="paid">Paid manually</option>
                        <option value="skipped">Skip this occurrence</option>
                    </select>
                </Field>
                <Field label="Note">
                    <input
                        value={form.note}
                        onChange={(event) =>
                            setForm({ ...form, note: event.target.value })
                        }
                        placeholder="Optional household note"
                    />
                </Field>
                {save.error && (
                    <p className="form-error span-2">{apiError(save.error)}</p>
                )}
                <div className="modal-actions span-2">
                    <button
                        type="button"
                        className="button secondary"
                        onClick={onClose}
                    >
                        Cancel
                    </button>
                    <button className="button" disabled={save.isPending}>
                        {save.isPending ? 'Saving…' : 'Save occurrence'}
                    </button>
                </div>
            </form>
        </Modal>
    );
}
