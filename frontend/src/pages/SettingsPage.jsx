import { createElement, useEffect, useMemo, useState } from 'react';
import { Archive, Building2, Moon, Pencil, Plus, ShieldCheck, Sun, Users } from 'lucide-react';
import { useTheme } from '../contexts/ThemeContext';
import { Card, Field, Modal, PageState, Pill } from '../components/ui';
import { apiError, money } from '../lib/format';
import { mutations, queryKeys, useAccounts, useFinovaMutation, useSettings } from '../lib/queries';

export default function SettingsPage() {
    const settings = useSettings();
    const accounts = useAccounts(true);
    const { preference, setPreference } = useTheme();
    const [household, setHousehold] = useState(null);
    const [accountEditor, setAccountEditor] = useState(false);
    const saveSettings = useFinovaMutation(mutations.saveSettings, [queryKeys.settings, queryKeys.dashboard]);

    useEffect(() => { if (settings.data) setHousehold(settings.data); }, [settings.data]);

    const saveHousehold = async (event) => {
        event.preventDefault();
        await saveSettings.mutateAsync(household);
    };

    return (
        <PageState loading={settings.isLoading || accounts.isLoading} error={(settings.error || accounts.error) && apiError(settings.error || accounts.error)}>
            <div className="settings-layout">
                <div className="settings-main page-stack">
                    <Card>
                        <div className="settings-card-heading"><div><span className="settings-icon"><Users /></span><span><h2>Household</h2><p>Shared display and regional preferences.</p></span></div></div>
                        {household && <form className="form-grid settings-form" onSubmit={saveHousehold}>
                            <Field label="Household name" className="span-2"><input value={household.householdName} onChange={(event) => setHousehold({ ...household, householdName: event.target.value })} /></Field>
                            <Field label="Currency"><select value={household.currencyCode} onChange={(event) => setHousehold({ ...household, currencyCode: event.target.value })}><option value="GBP">GBP — Pound sterling</option><option value="EUR">EUR — Euro</option><option value="USD">USD — US dollar</option></select></Field>
                            <Field label="Locale"><input value={household.locale} onChange={(event) => setHousehold({ ...household, locale: event.target.value })} /></Field>
                            <Field label="Timezone" className="span-2"><input value={household.timezone} onChange={(event) => setHousehold({ ...household, timezone: event.target.value })} /></Field>
                            <div className="modal-actions span-2"><button className="button" disabled={saveSettings.isPending}>{saveSettings.isPending ? 'Saving…' : 'Save household'}</button></div>
                        </form>}
                    </Card>

                    <Card>
                        <div className="settings-card-heading"><div><span className="settings-icon"><Building2 /></span><span><h2>Accounts</h2><p>Balances come from opening values and imported activity.</p></span></div><button className="button" onClick={() => setAccountEditor({})}><Plus /> Add account</button></div>
                        <div className="settings-account-list">
                            {(accounts.data || []).map((account) => (
                                <article key={account.id} className={account.isArchived ? 'archived' : ''}>
                                    <span className="account-dot account-0"><Building2 /></span>
                                    <span><strong>{account.name}</strong><small>{account.institution || (account.isShared ? [account.primaryHolderName, account.secondaryHolderName].filter(Boolean).join(' & ') : account.primaryHolderName || account.ownerName)} · {account.accountType}{account.isShared ? ' · joint' : ''}</small></span>
                                    <span><strong>{money(account.balance)}</strong><small>{money(account.safeZoneAmount)} protected</small></span>
                                    {account.includeInSafeToSpend ? <Pill tone="success">Included</Pill> : <Pill>Excluded</Pill>}
                                    {account.isArchived && <Pill tone="warning">Archived</Pill>}
                                    <button className="icon-button" onClick={() => setAccountEditor(account)} aria-label={'Edit ' + account.name}><Pencil /></button>
                                </article>
                            ))}
                        </div>
                    </Card>
                </div>

                <aside className="settings-side page-stack">
                    <Card>
                        <div className="settings-card-heading"><div><span className="settings-icon"><Sun /></span><span><h2>Appearance</h2><p>Finova follows your preference on this device.</p></span></div></div>
                        <div className="theme-options">
                            {[
                                ['system', 'System', ShieldCheck],
                                ['light', 'Light', Sun],
                                ['dark', 'Dark', Moon],
                            ].map(([value, label, icon]) => <button key={value} className={preference === value ? 'selected' : ''} onClick={() => setPreference(value)}>{createElement(icon)}<span>{label}</span></button>)}
                        </div>
                    </Card>
                    <Card className="privacy-card"><ShieldCheck /><h3>Private by design</h3><p>This Finova workspace has no external sign-in or bank connection. Keep the host network trusted and back up PostgreSQL regularly.</p></Card>
                </aside>

                <AccountEditor open={Boolean(accountEditor)} account={accountEditor?.id ? accountEditor : null} onClose={() => setAccountEditor(false)} />
            </div>
        </PageState>
    );
}

function AccountEditor({ open, account, onClose }) {
    const blank = useMemo(() => ({
        name: '', primaryHolderName: '', secondaryHolderName: '', isShared: false, accountType: 'current',
        institution: '', lastFour: '', openingBalance: 0, openingDate: new Date().toISOString().slice(0, 10),
        safeZoneAmount: 0, includeInSafeToSpend: true, isArchived: false,
    }), []);
    const [form, setForm] = useState(blank);
    useEffect(() => {
        setForm(account ? {
            name: account.name, isShared: account.isShared, accountType: account.accountType,
            primaryHolderName: account.primaryHolderName || account.ownerName || '', secondaryHolderName: account.secondaryHolderName || '',
            institution: account.institution || '', lastFour: account.lastFour || '', safeZoneAmount: account.safeZoneAmount,
            includeInSafeToSpend: account.includeInSafeToSpend, isArchived: account.isArchived,
        } : blank);
    }, [account, open, blank]);
    const save = useFinovaMutation(account ? mutations.updateAccount : mutations.createAccount, [
        queryKeys.accounts, queryKeys.dashboard, queryKeys.safety, queryKeys.goals,
    ]);
    const submit = async (event) => {
        event.preventDefault();
        const body = {
            ...form, openingBalance: Number(form.openingBalance || 0), safeZoneAmount: Number(form.safeZoneAmount || 0),
            secondaryHolderName: form.isShared ? form.secondaryHolderName : null,
            includeInSafeToSpend: form.accountType === 'savings' ? form.includeInSafeToSpend : form.includeInSafeToSpend,
        };
        await save.mutateAsync(account ? { id: account.id, body } : body);
        onClose();
    };
    return (
        <Modal open={open} onClose={onClose} title={account ? 'Edit account' : 'Add an account'} copy="Safe-zone floors are protected before Finova calculates available money.">
            <form className="form-grid" onSubmit={submit}>
                <Field label="Account name" className="span-2"><input required value={form.name || ''} onChange={(event) => setForm({ ...form, name: event.target.value })} /></Field>
                <Field label="Account ownership" className="span-2"><select value={form.isShared ? 'joint' : 'personal'} onChange={(event) => setForm({ ...form, isShared: event.target.value === 'joint' })}><option value="personal">Personal account</option><option value="joint">Joint account</option></select></Field>
                {form.isShared ? <>
                    <Field label="First account holder"><input required autoComplete="name" value={form.primaryHolderName || ''} onChange={(event) => setForm({ ...form, primaryHolderName: event.target.value })} /></Field>
                    <Field label="Second account holder"><input required autoComplete="name" value={form.secondaryHolderName || ''} onChange={(event) => setForm({ ...form, secondaryHolderName: event.target.value })} /></Field>
                </> : <Field label="Account holder name" className="span-2"><input required autoComplete="name" value={form.primaryHolderName || ''} onChange={(event) => setForm({ ...form, primaryHolderName: event.target.value })} /></Field>}
                <Field label="Account type"><select value={form.accountType} onChange={(event) => setForm({ ...form, accountType: event.target.value, includeInSafeToSpend: event.target.value !== 'savings' })}><option value="current">Current</option><option value="savings">Savings</option><option value="credit">Credit</option><option value="cash">Cash</option><option value="investment">Investment</option></select></Field>
                <Field label="Institution"><input value={form.institution || ''} onChange={(event) => setForm({ ...form, institution: event.target.value })} /></Field>
                <Field label="Last four digits"><input maxLength="4" inputMode="numeric" value={form.lastFour || ''} onChange={(event) => setForm({ ...form, lastFour: event.target.value.replace(/\D/g, '') })} /></Field>
                <Field label="Safe-zone floor"><input type="number" min="0" step="0.01" value={form.safeZoneAmount || 0} onChange={(event) => setForm({ ...form, safeZoneAmount: event.target.value })} /></Field>
                {!account && <><Field label="Opening balance"><input type="number" step="0.01" value={form.openingBalance} onChange={(event) => setForm({ ...form, openingBalance: event.target.value })} /></Field><Field label="Opening date"><input required type="date" value={form.openingDate} onChange={(event) => setForm({ ...form, openingDate: event.target.value })} /></Field></>}
                <label className="check-row span-2"><input type="checkbox" checked={form.includeInSafeToSpend || false} onChange={(event) => setForm({ ...form, includeInSafeToSpend: event.target.checked })} /><span><strong>Include in safe to spend</strong><small>Savings accounts are normally excluded.</small></span></label>
                {account && <label className="check-row danger-check span-2"><input type="checkbox" checked={form.isArchived || false} onChange={(event) => setForm({ ...form, isArchived: event.target.checked })} /><span><strong><Archive /> Archive account</strong><small>History remains intact but the account leaves active totals.</small></span></label>}
                {save.error && <p className="form-error span-2">{apiError(save.error)}</p>}
                <div className="modal-actions span-2"><button type="button" className="button secondary" onClick={onClose}>Cancel</button><button className="button" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save account'}</button></div>
            </form>
        </Modal>
    );
}
