import { useEffect, useState } from 'react';
import { Trash2 } from 'lucide-react';
import { Field, InlineError, Modal } from './ui';
import { apiError } from '../lib/format';
import { mutations, queryKeys, useFinovaMutation } from '../lib/queries';

const blank = {
    name: '',
    kind: 'bill',
    accountId: '',
    categoryId: '',
    amount: '',
    frequency: 'monthly',
    nextDate: '',
    matchText: '',
    amountTolerance: '5',
    dateWindowDays: '5',
    isActive: true,
};

export default function RecurringEditor({
    open,
    onClose,
    accounts,
    categories,
    item = null,
    transaction = null,
}) {
    const [form, setForm] = useState(blank);
    const invalidate = [
        queryKeys.recurring,
        queryKeys.occurrences,
        queryKeys.safety,
        queryKeys.dashboard,
        queryKeys.suggestions,
        ['transactions'],
        queryKeys.budgets,
    ];
    const create = useFinovaMutation(mutations.createRecurring, invalidate, {
        successMessage: transaction
            ? 'Transaction added to the recurring plan.'
            : 'Recurring item created.',
    });
    const update = useFinovaMutation(mutations.updateRecurring, invalidate, {
        successMessage: 'Recurring item updated.',
    });
    const mark = useFinovaMutation(
        mutations.markTransactionRecurring,
        invalidate,
        { successMessage: 'Transaction added to the recurring plan.' }
    );
    const remove = useFinovaMutation(
        mutations.deleteRecurring,
        invalidate,
        { successMessage: 'Recurring item deleted.' }
    );
    const pending =
        create.isPending ||
        update.isPending ||
        mark.isPending ||
        remove.isPending;
    const error = create.error || update.error || mark.error || remove.error;

    useEffect(() => {
        if (!open) return;
        if (transaction) {
            const credit = transaction.accountType === 'credit';
            setForm({
                ...blank,
                name:
                    transaction.payee ||
                    transaction.memo ||
                    'Recurring transaction',
                kind:
                    Number(transaction.amount) < 0 || credit
                        ? 'bill'
                        : 'income',
                accountId: String(transaction.accountId),
                categoryId: transaction.categoryId
                    ? String(transaction.categoryId)
                    : '',
                amount: String(Math.abs(Number(transaction.amount))),
                nextDate: advanceRecurringDate(transaction.date, 'monthly'),
                matchText: transaction.payee || transaction.memo || '',
            });
        } else if (item) {
            setForm({
                name: item.name,
                kind: item.kind,
                accountId: String(item.accountId),
                categoryId: item.categoryId ? String(item.categoryId) : '',
                amount: String(item.amount),
                frequency: item.frequency,
                nextDate: item.nextDate,
                matchText: item.matchText || item.name,
                amountTolerance: String(item.amountTolerance ?? 5),
                dateWindowDays: String(item.dateWindowDays ?? 5),
                isActive: item.isActive,
            });
        } else {
            setForm(blank);
        }
    }, [open, item, transaction]);

    const set = (name, value) =>
        setForm((current) => ({ ...current, [name]: value }));
    const body = () => ({
        name: form.name.trim(),
        kind: form.kind,
        accountId: Number(form.accountId),
        categoryId: form.categoryId ? Number(form.categoryId) : null,
        amount: Number(form.amount),
        frequency: form.frequency,
        nextDate: form.nextDate,
        source: item?.source || 'manual',
        isActive: form.isActive,
        matchText: form.matchText.trim() || null,
        amountTolerance: Number(form.amountTolerance),
        dateWindowDays: Number(form.dateWindowDays),
    });
    const submit = async (event) => {
        event.preventDefault();
        if (transaction) {
            const value = body();
            await mark.mutateAsync({
                id: transaction.id,
                body: {
                    name: value.name,
                    categoryId: value.categoryId,
                    amount: value.amount,
                    frequency: value.frequency,
                    nextDate: value.nextDate,
                    amountTolerance: value.amountTolerance,
                    dateWindowDays: value.dateWindowDays,
                },
            });
        } else if (item) {
            await update.mutateAsync({ id: item.id, body: body() });
        } else {
            await create.mutateAsync(body());
        }
        onClose();
    };
    const deleteItem = async () => {
        if (
            !item ||
            !window.confirm(
                `Delete the recurring plan for ${item.name}? Matched transactions will remain unchanged.`
            )
        )
            return;
        await remove.mutateAsync(item.id);
        onClose();
    };

    const title = transaction
        ? 'Mark transaction as recurring'
        : item
          ? 'Edit recurring item'
          : 'Add a recurring item';
    const copy = transaction
        ? 'Confirm the schedule. This transaction will be linked as the completed occurrence, not counted again.'
        : 'Confirmed future occurrences are protected in safe to spend until matched, paid, or skipped.';

    return (
        <Modal open={open} onClose={onClose} title={title} copy={copy}>
            <form className="form-grid" onSubmit={submit}>
                <Field label="Name" className="span-2">
                    <input
                        required
                        value={form.name}
                        onChange={(event) => set('name', event.target.value)}
                        placeholder="Mortgage, electricity…"
                    />
                </Field>
                <Field label="Type">
                    <select
                        disabled={Boolean(transaction)}
                        value={form.kind}
                        onChange={(event) => set('kind', event.target.value)}
                    >
                        <option value="bill">Bill</option>
                        <option value="income">Income / payday</option>
                    </select>
                </Field>
                <Field label="Expected amount">
                    <input
                        required
                        min="0.01"
                        step="0.01"
                        type="number"
                        value={form.amount}
                        onChange={(event) => set('amount', event.target.value)}
                    />
                </Field>
                <Field label="Account">
                    <select
                        disabled={Boolean(transaction)}
                        required
                        value={form.accountId}
                        onChange={(event) =>
                            set('accountId', event.target.value)
                        }
                    >
                        <option value="">Choose account</option>
                        {accounts
                            .filter(
                                (account) =>
                                    form.kind === 'bill' ||
                                    account.accountType !== 'credit'
                            )
                            .map((account) => (
                                <option key={account.id} value={account.id}>
                                    {account.name}
                                </option>
                            ))}
                    </select>
                </Field>
                <Field label="Category">
                    <select
                        value={form.categoryId}
                        onChange={(event) =>
                            set('categoryId', event.target.value)
                        }
                    >
                        <option value="">No category</option>
                        {categories
                            .filter(
                                (category) =>
                                    category.kind !== 'income' ||
                                    form.kind === 'income'
                            )
                            .map((category) => (
                                <option key={category.id} value={category.id}>
                                    {category.name}
                                </option>
                            ))}
                    </select>
                </Field>
                <Field label="Frequency">
                    <select
                        value={form.frequency}
                        onChange={(event) =>
                            set('frequency', event.target.value)
                        }
                    >
                        <option value="weekly">Weekly</option>
                        <option value="fortnightly">Fortnightly</option>
                        <option value="monthly">Monthly</option>
                        <option value="quarterly">Quarterly</option>
                        <option value="yearly">Yearly</option>
                    </select>
                </Field>
                <Field label="Next expected date">
                    <input
                        required
                        type="date"
                        value={form.nextDate}
                        onChange={(event) =>
                            set('nextDate', event.target.value)
                        }
                    />
                </Field>
                {!transaction && (
                    <Field
                        label="Matching reference"
                        hint="Payee text Finova should recognise."
                        className="span-2"
                    >
                        <input
                            value={form.matchText}
                            onChange={(event) =>
                                set('matchText', event.target.value)
                            }
                            placeholder="E.ON NEXT LTD"
                        />
                    </Field>
                )}
                <Field
                    label="Amount tolerance"
                    hint="Allows variable utility bills."
                >
                    <input
                        min="0"
                        step="0.01"
                        type="number"
                        value={form.amountTolerance}
                        onChange={(event) =>
                            set('amountTolerance', event.target.value)
                        }
                    />
                </Field>
                <Field
                    label="Date window"
                    hint="Days before or after the due date."
                >
                    <input
                        min="0"
                        max="31"
                        step="1"
                        type="number"
                        value={form.dateWindowDays}
                        onChange={(event) =>
                            set('dateWindowDays', event.target.value)
                        }
                    />
                </Field>
                {item && (
                    <label className="check-row span-2">
                        <input
                            type="checkbox"
                            checked={form.isActive}
                            onChange={(event) =>
                                set('isActive', event.target.checked)
                            }
                        />
                        <span>
                            <strong>Active plan</strong>
                            <small>
                                Pause this to stop generating and protecting
                                future occurrences.
                            </small>
                        </span>
                    </label>
                )}
                <InlineError className="span-2">
                    {error && apiError(error)}
                </InlineError>
                <div className="modal-actions span-2 recurring-editor-actions">
                    {item && (
                        <button
                            type="button"
                            className="button danger ghost"
                            disabled={pending}
                            onClick={deleteItem}
                        >
                            <Trash2 /> Delete
                        </button>
                    )}
                    <span />
                    <button
                        type="button"
                        className="button secondary"
                        onClick={onClose}
                    >
                        Cancel
                    </button>
                    <button className="button" disabled={pending}>
                        {pending
                            ? 'Saving…'
                            : transaction
                              ? 'Add to plan'
                              : 'Save plan'}
                    </button>
                </div>
            </form>
        </Modal>
    );
}

export function advanceRecurringDate(value, frequency) {
    const [year, month, day] = String(value).split('-').map(Number);
    if (!year || !month || !day) return '';
    const date = new Date(Date.UTC(year, month - 1, day));
    if (frequency === 'weekly') date.setUTCDate(date.getUTCDate() + 7);
    else if (frequency === 'fortnightly')
        date.setUTCDate(date.getUTCDate() + 14);
    else {
        const months =
            frequency === 'quarterly' ? 3 : frequency === 'yearly' ? 12 : 1;
        date.setUTCDate(1);
        date.setUTCMonth(date.getUTCMonth() + months);
        const lastDay = new Date(
            Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0)
        ).getUTCDate();
        date.setUTCDate(Math.min(day, lastDay));
    }
    return date.toISOString().slice(0, 10);
}
