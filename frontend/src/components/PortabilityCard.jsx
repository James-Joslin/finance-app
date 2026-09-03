import { useState } from 'react';
import { Archive, Download, FileUp } from 'lucide-react';
import { apiError } from '../lib/format';
import { mutations, queryKeys, useFinovaMutation } from '../lib/queries';
import { Card, Field, InlineError } from './ui';

const entities = [
    'settings',
    'accounts',
    'rules',
    'goals',
    'recurring',
    'budgets',
    'transactions',
    'images',
];

export default function PortabilityCard() {
    const [file, setFile] = useState(null);
    const [message, setMessage] = useState(null);
    const importArchive = useFinovaMutation(
        mutations.importPortableArchive,
        [
            queryKeys.enrollment,
            queryKeys.settings,
            queryKeys.accounts,
            queryKeys.categories,
            queryKeys.rules,
            queryKeys.dashboard,
            queryKeys.goals,
            queryKeys.recurring,
            queryKeys.occurrences,
            queryKeys.budgets,
            queryKeys.safety,
            queryKeys.transactionsRoot,
            queryKeys.importsRoot,
            queryKeys.insightsRoot,
        ],
        { successMessage: 'Household archive restored.' }
    );

    const submit = async (event) => {
        event.preventDefault();
        if (!file) return;
        if (
            !window.confirm(
                'Importing this archive will replace all current household data. Continue?'
            )
        )
            return;
        setMessage(null);
        const form = new FormData();
        form.append('archive', file);
        try {
            const result = await importArchive.mutateAsync(form);
            const count = Object.values(result.records || {}).reduce(
                (sum, value) => sum + value,
                0
            );
            setMessage(
                `Restored ${count} records and ${result.images} images.`
            );
            setFile(null);
            event.target.reset();
        } catch {
            // The mutation error remains visible below.
        }
    };

    return (
        <Card>
            <div className="settings-card-heading">
                <div>
                    <span className="settings-icon">
                        <Archive />
                    </span>
                    <span>
                        <h2>Data portability</h2>
                        <p>
                            Export a lossless copy of this household or restore
                            one.
                        </p>
                    </span>
                </div>
            </div>
            <div className="form-grid settings-form">
                <div className="span-2">
                    <p>Full archive</p>
                    <a
                        className="button"
                        href="/api/portability/export/archive"
                    >
                        <Download /> Export full archive
                    </a>
                </div>
                <div className="span-2">
                    <p>Individual lossless exports</p>
                    <div className="modal-actions portability-links">
                        {entities.map((entity) => (
                            <a
                                key={entity}
                                className="button secondary"
                                href={`/api/portability/export/${entity}`}
                            >
                                <Download />{' '}
                                {entity[0].toUpperCase() + entity.slice(1)}
                            </a>
                        ))}
                    </div>
                </div>
                <form className="span-2" onSubmit={submit}>
                    <Field label="Restore full archive">
                        <input
                            type="file"
                            accept=".zip,application/zip"
                            onChange={(event) =>
                                setFile(event.target.files?.[0] || null)
                            }
                        />
                    </Field>
                    <p className="form-hint">
                        Current data is replaced only after integrity and
                        relationship checks pass.
                    </p>
                    <InlineError>
                        {importArchive.error && apiError(importArchive.error)}
                    </InlineError>
                    {message && <p className="form-hint">{message}</p>}
                    <button
                        className="button"
                        disabled={!file || importArchive.isPending}
                    >
                        <FileUp />{' '}
                        {importArchive.isPending
                            ? 'Restoring…'
                            : 'Restore archive'}
                    </button>
                </form>
            </div>
        </Card>
    );
}
