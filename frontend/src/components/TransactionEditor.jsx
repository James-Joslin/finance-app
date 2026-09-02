import { useEffect, useMemo, useState } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import { Field, InlineError, Modal } from './ui';
import { apiError, money } from '../lib/format';
import {
    mutations,
    queryKeys,
    useFinovaMutation,
    useTransaction,
} from '../lib/queries';

const today = () => new Date().toISOString().slice(0, 10);
const blankLine = () => ({ categoryId: '', amount: '', memo: '' });
const blank = () => ({
    date: today(),
    accountId: '',
    direction: 'expense',
    amount: '',
    payee: '',
    memo: '',
    categoryId: '',
    isSplit: false,
    splits: [blankLine(), blankLine()],
});

const invalidations = [
    ['transactions'],
    queryKeys.accounts,
    queryKeys.dashboard,
    ['insights'],
    queryKeys.goals,
    queryKeys.budgets,
    queryKeys.safety,
    queryKeys.recurring,
    queryKeys.occurrences,
    queryKeys.suggestions,
    ['transfer-candidates'],
];

export default function TransactionEditor({
    open,
    transaction = null,
    accounts,
    categories,
    onClose,
}) {
    const detail = useTransaction(transaction?.id);
    const [form, setForm] = useState(blank);
    const create = useFinovaMutation(
        mutations.createTransaction,
        invalidations,
        { successMessage: 'Transaction created.' }
    );
    const update = useFinovaMutation(
        mutations.updateTransaction,
        invalidations,
        { successMessage: 'Transaction updated.' }
    );
    const remove = useFinovaMutation(
        mutations.deleteTransaction,
        invalidations,
        { successMessage: 'Transaction deleted.' }
    );
    const pending = create.isPending || update.isPending || remove.isPending;
    const error =
        create.error || update.error || remove.error || detail.error || null;

    useEffect(() => {
        if (!open) return;
        if (!transaction) {
            setForm(blank());
            return;
        }
        const item = detail.data?.transaction || transaction;
        const splitRows = detail.data?.splits || [];
        setForm({
            date: item.date || today(),
            accountId: String(item.accountId || ''),
            direction: Number(item.amount) >= 0 ? 'income' : 'expense',
            amount: String(Math.abs(Number(item.amount))),
            payee: item.payee || '',
            memo: item.memo || '',
            categoryId: item.categoryId ? String(item.categoryId) : '',
            isSplit: Boolean(item.isSplit || splitRows.length),
            splits:
                splitRows.length > 0
                    ? splitRows.map((line) => ({
                          categoryId: String(line.categoryId),
                          amount: String(line.amount),
                          memo: line.memo || '',
                      }))
                    : [blankLine(), blankLine()],
        });
    }, [open, transaction, detail.data]);

    const set = (name, value) =>
        setForm((current) => ({ ...current, [name]: value }));
    const splitTotal = useMemo(
        () =>
            form.splits.reduce(
                (total, line) => total + (Number(line.amount) || 0),
                0
            ),
        [form.splits]
    );
    const splitDifference = Number(form.amount || 0) - splitTotal;
    const validSplit =
        !form.isSplit ||
        (form.splits.length >= 2 &&
            form.splits.every(
                (line) =>
                    line.categoryId &&
                    Number(line.amount) > 0 &&
                    Number.isFinite(Number(line.amount))
            ) &&
            Math.abs(splitDifference) < 0.005);
    const visibleCategories = categories.filter(
        (category) => category.kind === form.direction
    );

    const updateLine = (index, name, value) => {
        setForm((current) => ({
            ...current,
            splits: current.splits.map((line, lineIndex) =>
                lineIndex === index ? { ...line, [name]: value } : line
            ),
        }));
    };
    const submit = async (event) => {
        event.preventDefault();
        const body = {
            date: form.date,
            accountId: Number(form.accountId),
            direction: form.direction,
            amount: Number(form.amount),
            payee: form.payee.trim() || null,
            memo: form.memo.trim() || null,
            categoryId: form.isSplit ? null : Number(form.categoryId),
            splits: form.isSplit
                ? form.splits.map((line) => ({
                      categoryId: Number(line.categoryId),
                      amount: Number(line.amount),
                      memo: line.memo.trim() || null,
                  }))
                : [],
        };
        try {
            if (transaction) {
                await update.mutateAsync({ id: transaction.id, body });
            } else {
                await create.mutateAsync(body);
            }
            onClose();
        } catch {
            // Keep the editor open so the mutation error remains visible.
        }
    };
    const deleteTransaction = async () => {
        if (
            !transaction ||
            !window.confirm(
                'Delete this manual transaction? Its balance, reports, and any split lines will be recalculated.'
            )
        )
            return;
        try {
            await remove.mutateAsync(transaction.id);
            onClose();
        } catch {
            // Keep the editor open so the delete error remains visible.
        }
    };

    const title = transaction ? 'Edit manual transaction' : 'Add transaction';
    const copy = transaction
        ? 'Imported transactions cannot be edited here. Split lines share the parent transaction date, account, and payee.'
        : 'Record money in or out of one of your accounts.';

    return (
        <Modal open={open} onClose={onClose} title={title} copy={copy} wide>
            <form className="form-grid transaction-editor" onSubmit={submit}>
                <Field label="Date">
                    <input
                        required
                        type="date"
                        value={form.date}
                        onChange={(event) => set('date', event.target.value)}
                    />
                </Field>
                <Field label="Account">
                    <select
                        required
                        value={form.accountId}
                        onChange={(event) =>
                            set('accountId', event.target.value)
                        }
                    >
                        <option value="">Choose account</option>
                        {accounts
                            .filter((account) => !account.isArchived)
                            .map((account) => (
                                <option key={account.id} value={account.id}>
                                    {account.name}
                                </option>
                            ))}
                    </select>
                </Field>
                <Field label="Direction">
                    <select
                        value={form.direction}
                        onChange={(event) => {
                            set('direction', event.target.value);
                            set('categoryId', '');
                        }}
                    >
                        <option value="expense">Expense / money out</option>
                        <option value="income">Income / money in</option>
                    </select>
                </Field>
                <Field label="Amount">
                    <input
                        required
                        min="0.01"
                        step="0.01"
                        type="number"
                        value={form.amount}
                        onChange={(event) => set('amount', event.target.value)}
                    />
                </Field>
                <Field label="Payee" className="span-2">
                    <input
                        value={form.payee}
                        onChange={(event) => set('payee', event.target.value)}
                        placeholder="Who was this with?"
                    />
                </Field>
                <Field label="Memo" className="span-2">
                    <textarea
                        value={form.memo}
                        onChange={(event) => set('memo', event.target.value)}
                        placeholder="Optional transaction note"
                        rows="2"
                    />
                </Field>
                <label className="check-row span-2">
                    <input
                        type="checkbox"
                        checked={form.isSplit}
                        onChange={(event) =>
                            setForm((current) => ({
                                ...current,
                                isSplit: event.target.checked,
                                categoryId: event.target.checked
                                    ? ''
                                    : current.categoryId,
                                splits:
                                    current.splits.length >= 2
                                        ? current.splits
                                        : [blankLine(), blankLine()],
                            }))
                        }
                    />
                    <span>
                        <strong>Split this transaction</strong>
                        <small>
                            Allocate the total across multiple categories.
                        </small>
                    </span>
                </label>
                {!form.isSplit ? (
                    <Field label="Category" className="span-2">
                        <select
                            required
                            value={form.categoryId}
                            onChange={(event) =>
                                set('categoryId', event.target.value)
                            }
                        >
                            <option value="">Choose category</option>
                            {visibleCategories.map((category) => (
                                <option key={category.id} value={category.id}>
                                    {category.name}
                                </option>
                            ))}
                        </select>
                    </Field>
                ) : (
                    <div className="transaction-splits span-2">
                        <div className="split-header">
                            <strong>Split lines</strong>
                            <span>
                                {money(splitTotal)} of{' '}
                                {money(Number(form.amount) || 0)}
                            </span>
                        </div>
                        {form.splits.map((line, index) => (
                            <div className="split-line" key={index}>
                                <select
                                    required
                                    aria-label={
                                        'Split ' + (index + 1) + ' category'
                                    }
                                    value={line.categoryId}
                                    onChange={(event) =>
                                        updateLine(
                                            index,
                                            'categoryId',
                                            event.target.value
                                        )
                                    }
                                >
                                    <option value="">Choose category</option>
                                    {visibleCategories.map((category) => (
                                        <option
                                            key={category.id}
                                            value={category.id}
                                        >
                                            {category.name}
                                        </option>
                                    ))}
                                </select>
                                <input
                                    required
                                    min="0.01"
                                    step="0.01"
                                    type="number"
                                    aria-label={
                                        'Split ' + (index + 1) + ' amount'
                                    }
                                    placeholder="Amount"
                                    value={line.amount}
                                    onChange={(event) =>
                                        updateLine(
                                            index,
                                            'amount',
                                            event.target.value
                                        )
                                    }
                                />
                                <input
                                    aria-label={
                                        'Split ' + (index + 1) + ' memo'
                                    }
                                    placeholder="Line memo"
                                    value={line.memo}
                                    onChange={(event) =>
                                        updateLine(
                                            index,
                                            'memo',
                                            event.target.value
                                        )
                                    }
                                />
                                <button
                                    type="button"
                                    className="icon-button"
                                    disabled={form.splits.length <= 2}
                                    onClick={() =>
                                        set(
                                            'splits',
                                            form.splits.filter(
                                                (_, lineIndex) =>
                                                    lineIndex !== index
                                            )
                                        )
                                    }
                                    aria-label={'Remove split ' + (index + 1)}
                                >
                                    <Trash2 />
                                </button>
                            </div>
                        ))}
                        <button
                            type="button"
                            className="button secondary"
                            onClick={() =>
                                set('splits', [...form.splits, blankLine()])
                            }
                        >
                            <Plus /> Add split line
                        </button>
                        {!validSplit && (
                            <p className="form-error" role="alert">
                                Add at least two categorized lines whose amounts
                                equal the transaction total.
                            </p>
                        )}
                    </div>
                )}
                <InlineError className="span-2">
                    {error && apiError(error)}
                </InlineError>
                <div className="modal-actions span-2">
                    {transaction?.isEditable && (
                        <button
                            type="button"
                            className="button danger ghost"
                            disabled={pending}
                            onClick={deleteTransaction}
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
                    <button
                        className="button"
                        disabled={
                            pending ||
                            !form.accountId ||
                            !form.date ||
                            !form.amount ||
                            (!form.categoryId && !form.isSplit) ||
                            !validSplit ||
                            (transaction && detail.isLoading)
                        }
                    >
                        {pending
                            ? 'Saving…'
                            : transaction
                              ? 'Save changes'
                              : 'Add transaction'}
                    </button>
                </div>
            </form>
        </Modal>
    );
}
