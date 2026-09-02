import { useSearchParams } from 'react-router-dom';
import { createElement, useEffect, useMemo, useState } from 'react';
import {
    Archive,
    Building2,
    Moon,
    Pencil,
    Plus,
    ShieldCheck,
    Sun,
    Tags,
    Trash2,
    UserRound,
    Users,
} from 'lucide-react';
import { useTheme } from '../contexts/ThemeContext';
import {
    Card,
    Field,
    InlineError,
    Modal,
    PageState,
    Pill,
} from '../components/ui';
import { parseRecordId, useDeepLinkTarget } from '../utils/deepLink';
import { apiError, money, percent, todayIso } from '../lib/format';
import {
    mutations,
    queryKeys,
    useAccounts,
    useCategories,
    useEnrollmentStatus,
    useFinovaMutation,
    useSettings,
    useTransactionRules,
} from '../lib/queries';

export default function SettingsPage() {
    const [searchParams] = useSearchParams();
    const accountId = parseRecordId(searchParams.get('accountId'));
    const enrollment = useEnrollmentStatus();
    const settings = useSettings();
    const accounts = useAccounts(true);
    const categories = useCategories(true);
    const rules = useTransactionRules();
    const { preference, setPreference } = useTheme();
    const [profile, setProfile] = useState(null);
    const [household, setHousehold] = useState(null);
    const [accountEditor, setAccountEditor] = useState(false);
    const [categoryEditor, setCategoryEditor] = useState(false);
    const [ruleEditor, setRuleEditor] = useState(false);
    const saveProfile = useFinovaMutation(
        mutations.saveEnrollment,
        [queryKeys.enrollment, queryKeys.settings, queryKeys.dashboard],
        { successMessage: 'Profile saved.' }
    );
    const saveSettings = useFinovaMutation(
        mutations.saveSettings,
        [queryKeys.settings, queryKeys.dashboard],
        { successMessage: 'Household settings saved.' }
    );
    const deleteRule = useFinovaMutation(
        mutations.deleteTransactionRule,
        [queryKeys.rules],
        { successMessage: 'Automatic category rule removed.' }
    );
    const saveCategory = useFinovaMutation(
        (value) =>
            value.id
                ? mutations.updateCategory(value)
                : mutations.createCategory(value.body),
        [queryKeys.categories, queryKeys.rules, queryKeys.dashboard],
        {
            successMessage: (data, value) =>
                value.id ? 'Category updated.' : 'Category created.',
        }
    );
    const deleteCategory = useFinovaMutation(
        mutations.deleteCategory,
        [queryKeys.categories, queryKeys.dashboard],
        { successMessage: 'Category deleted.' }
    );
    const saveRule = useFinovaMutation(
        (value) =>
            value.id
                ? mutations.updateTransactionRule(value)
                : mutations.createTransactionRule(value.body),
        [queryKeys.rules, queryKeys.categories],
        {
            successMessage: (data, value) =>
                value.id
                    ? 'Automatic category rule updated.'
                    : 'Automatic category rule created.',
        }
    );
    const pageQueries = [enrollment, settings, accounts, categories, rules];
    useDeepLinkTarget(
        accountId,
        accounts.data,
        '[data-deep-link-type="account"]'
    );

    useEffect(() => {
        if (enrollment.data?.profile) setProfile(enrollment.data.profile);
    }, [enrollment.data]);
    useEffect(() => {
        if (settings.data) setHousehold(settings.data);
    }, [settings.data]);

    const submitProfile = async (event) => {
        event.preventDefault();
        try {
            await saveProfile.mutateAsync({
                ...profile,
                householdName: household.householdName,
            });
        } catch {
            // The mutation error remains visible in the form.
        }
    };

    const saveHousehold = async (event) => {
        event.preventDefault();
        try {
            await saveSettings.mutateAsync(household);
        } catch {
            // The mutation error remains visible in the form.
        }
    };

    return (
        <PageState
            loading={
                enrollment.isLoading ||
                settings.isLoading ||
                accounts.isLoading ||
                categories.isLoading ||
                rules.isLoading
            }
            error={
                (enrollment.error ||
                    settings.error ||
                    accounts.error ||
                    categories.error ||
                    rules.error) &&
                apiError(
                    enrollment.error ||
                        settings.error ||
                        accounts.error ||
                        categories.error ||
                        rules.error
                )
            }
            onRetry={() =>
                Promise.all(
                    pageQueries
                        .filter((query) => query.error)
                        .map((query) => query.refetch())
                )
            }
            retrying={pageQueries.some(
                (query) => query.error && query.isFetching
            )}
        >
            <div className="settings-layout">
                <div className="settings-main page-stack">
                    <Card>
                        <div className="settings-card-heading">
                            <div>
                                <span className="settings-icon">
                                    <UserRound />
                                </span>
                                <span>
                                    <h2>Profile</h2>
                                    <p>Your name and workspace identity.</p>
                                </span>
                            </div>
                        </div>
                        {profile && household && (
                            <form
                                className="form-grid settings-form"
                                onSubmit={submitProfile}
                            >
                                <Field label="First name">
                                    <input
                                        required
                                        autoComplete="given-name"
                                        maxLength="80"
                                        value={profile.firstName}
                                        onChange={(event) =>
                                            setProfile({
                                                ...profile,
                                                firstName: event.target.value,
                                            })
                                        }
                                    />
                                </Field>
                                <Field label="Last name">
                                    <input
                                        required
                                        autoComplete="family-name"
                                        maxLength="80"
                                        value={profile.lastName}
                                        onChange={(event) =>
                                            setProfile({
                                                ...profile,
                                                lastName: event.target.value,
                                            })
                                        }
                                    />
                                </Field>
                                <InlineError className="span-2">
                                    {saveProfile.error &&
                                        apiError(saveProfile.error)}
                                </InlineError>
                                <div className="modal-actions span-2">
                                    <button
                                        className="button"
                                        disabled={saveProfile.isPending}
                                    >
                                        {saveProfile.isPending
                                            ? 'Saving…'
                                            : 'Save profile'}
                                    </button>
                                </div>
                            </form>
                        )}
                    </Card>

                    <Card>
                        <div className="settings-card-heading">
                            <div>
                                <span className="settings-icon">
                                    <Users />
                                </span>
                                <span>
                                    <h2>Household</h2>
                                    <p>
                                        Shared display and regional preferences.
                                    </p>
                                </span>
                            </div>
                        </div>
                        {household && (
                            <form
                                className="form-grid settings-form"
                                onSubmit={saveHousehold}
                            >
                                <Field
                                    label="Household name"
                                    className="span-2"
                                >
                                    <input
                                        value={household.householdName}
                                        onChange={(event) =>
                                            setHousehold({
                                                ...household,
                                                householdName:
                                                    event.target.value,
                                            })
                                        }
                                    />
                                </Field>
                                <Field label="Currency">
                                    <select
                                        value={household.currencyCode}
                                        onChange={(event) =>
                                            setHousehold({
                                                ...household,
                                                currencyCode:
                                                    event.target.value,
                                            })
                                        }
                                    >
                                        <option value="GBP">
                                            GBP — Pound sterling
                                        </option>
                                        <option value="EUR">EUR — Euro</option>
                                        <option value="USD">
                                            USD — US dollar
                                        </option>
                                    </select>
                                </Field>
                                <Field label="Locale">
                                    <input
                                        value={household.locale}
                                        onChange={(event) =>
                                            setHousehold({
                                                ...household,
                                                locale: event.target.value,
                                            })
                                        }
                                    />
                                </Field>
                                <Field label="Timezone" className="span-2">
                                    <input
                                        value={household.timezone}
                                        onChange={(event) =>
                                            setHousehold({
                                                ...household,
                                                timezone: event.target.value,
                                            })
                                        }
                                    />
                                </Field>
                                <InlineError className="span-2">
                                    {saveSettings.error &&
                                        apiError(saveSettings.error)}
                                </InlineError>
                                <div className="modal-actions span-2">
                                    <button
                                        className="button"
                                        disabled={saveSettings.isPending}
                                    >
                                        {saveSettings.isPending
                                            ? 'Saving…'
                                            : 'Save household'}
                                    </button>
                                </div>
                            </form>
                        )}
                    </Card>

                    <Card>
                        <div className="settings-card-heading">
                            <div>
                                <span className="settings-icon">
                                    <Building2 />
                                </span>
                                <span>
                                    <h2>Accounts</h2>
                                    <p>
                                        Balances come from opening values and
                                        imported activity.
                                    </p>
                                </span>
                            </div>
                            <button
                                className="button"
                                onClick={() => setAccountEditor({})}
                            >
                                <Plus /> Add account
                            </button>
                        </div>
                        <div className="settings-account-list">
                            {(accounts.data || []).map((account) => (
                                <article
                                    key={account.id}
                                    className={
                                        account.isArchived ? 'archived' : ''
                                    }
                                    data-deep-link-type="account"
                                    data-deep-link-id={account.id}
                                >
                                    <span className="account-dot account-0">
                                        <Building2 />
                                    </span>
                                    <span>
                                        <strong>{account.name}</strong>
                                        <small>
                                            {account.institution ||
                                                (account.isShared
                                                    ? [
                                                          account.primaryHolderName,
                                                          account.secondaryHolderName,
                                                      ]
                                                          .filter(Boolean)
                                                          .join(' & ')
                                                    : account.primaryHolderName ||
                                                      account.ownerName)}{' '}
                                            · {account.accountType}
                                            {account.isShared ? ' · joint' : ''}
                                        </small>
                                    </span>
                                    <AccountPosition account={account} />
                                    {account.accountType === 'credit' ? (
                                        <Pill tone="warning">Debt</Pill>
                                    ) : account.includeInSafeToSpend ? (
                                        <Pill tone="success">Included</Pill>
                                    ) : (
                                        <Pill>Excluded</Pill>
                                    )}
                                    {account.isArchived && (
                                        <Pill tone="warning">Archived</Pill>
                                    )}
                                    <button
                                        className="icon-button"
                                        onClick={() =>
                                            setAccountEditor(account)
                                        }
                                        aria-label={'Edit ' + account.name}
                                    >
                                        <Pencil />
                                    </button>
                                </article>
                            ))}
                        </div>
                    </Card>

                    <Card>
                        <div className="settings-card-heading">
                            <div>
                                <span className="settings-icon">
                                    <Tags />
                                </span>
                                <span>
                                    <h2>Categories</h2>
                                    <p>
                                        Organise transactions and control how
                                        they affect your plans.
                                    </p>
                                </span>
                            </div>
                            <button
                                className="button"
                                onClick={() => setCategoryEditor({})}
                            >
                                <Plus /> Add category
                            </button>
                        </div>
                        <div className="rule-list category-list">
                            {(categories.data || []).map((category) => (
                                <article
                                    key={category.id}
                                    className={
                                        category.isArchived ? 'archived' : ''
                                    }
                                >
                                    <span
                                        className={
                                            'category-badge category-' +
                                            category.colorKey
                                        }
                                    >
                                        {category.name.slice(0, 1)}
                                    </span>
                                    <span>
                                        <strong>{category.name}</strong>
                                        <small>
                                            {category.kind}
                                            {category.isSystem
                                                ? ' · system'
                                                : ''}
                                        </small>
                                    </span>
                                    {category.isArchived && (
                                        <Pill tone="warning">Archived</Pill>
                                    )}
                                    <span className="row-actions">
                                        {!category.isSystem && (
                                            <>
                                                <button
                                                    className="icon-button"
                                                    onClick={() =>
                                                        setCategoryEditor(
                                                            category
                                                        )
                                                    }
                                                    aria-label={
                                                        'Edit ' + category.name
                                                    }
                                                >
                                                    <Pencil />
                                                </button>
                                                <button
                                                    className="icon-button"
                                                    onClick={() =>
                                                        saveCategory.mutate({
                                                            id: category.id,
                                                            body: {
                                                                ...category,
                                                                isArchived:
                                                                    !category.isArchived,
                                                            },
                                                        })
                                                    }
                                                    disabled={
                                                        saveCategory.isPending
                                                    }
                                                    aria-label={
                                                        (category.isArchived
                                                            ? 'Restore '
                                                            : 'Archive ') +
                                                        category.name
                                                    }
                                                >
                                                    <Archive />
                                                </button>
                                                <button
                                                    className="icon-button"
                                                    onClick={() =>
                                                        window.confirm(
                                                            'Delete ' +
                                                                category.name +
                                                                '? This is only possible when nothing uses it.'
                                                        ) &&
                                                        deleteCategory.mutate(
                                                            category.id
                                                        )
                                                    }
                                                    disabled={
                                                        deleteCategory.isPending
                                                    }
                                                    aria-label={
                                                        'Delete ' +
                                                        category.name
                                                    }
                                                >
                                                    <Trash2 />
                                                </button>
                                            </>
                                        )}
                                    </span>
                                </article>
                            ))}
                        </div>
                        <InlineError>
                            {(saveCategory.error || deleteCategory.error) &&
                                apiError(
                                    saveCategory.error || deleteCategory.error
                                )}
                        </InlineError>
                    </Card>

                    <Card>
                        <div className="settings-card-heading">
                            <div>
                                <span className="settings-icon">
                                    <Tags />
                                </span>
                                <span>
                                    <h2>Automatic categories</h2>
                                    <p>
                                        References Finova has learned when you
                                        categorise transactions.
                                    </p>
                                </span>
                            </div>
                            <button
                                className="button"
                                onClick={() => setRuleEditor({})}
                            >
                                <Plus /> Add rule
                            </button>
                        </div>
                        {(rules.data || []).length === 0 ? (
                            <p className="muted-copy">
                                Change a transaction category and Finova will
                                remember the reference for future imports.
                            </p>
                        ) : (
                            <div className="rule-list">
                                {(rules.data || []).map((rule) => (
                                    <article key={rule.id}>
                                        <span className="settings-icon">
                                            <Tags />
                                        </span>
                                        <span>
                                            <strong>
                                                {rule.referenceText}
                                            </strong>
                                            <small>
                                                {rule.direction === 'in'
                                                    ? 'Money in from this reference'
                                                    : rule.direction === 'out'
                                                      ? 'Money out to this reference'
                                                      : 'Money in or out with this reference'}
                                            </small>
                                        </span>
                                        <Pill
                                            tone={
                                                rule.isActive
                                                    ? 'info'
                                                    : 'warning'
                                            }
                                        >
                                            {rule.categoryName}
                                            {rule.isActive ? '' : ' · inactive'}
                                        </Pill>
                                        <button
                                            className="icon-button"
                                            onClick={() => setRuleEditor(rule)}
                                            aria-label={
                                                'Edit automatic category for ' +
                                                rule.referenceText
                                            }
                                        >
                                            <Pencil />
                                        </button>
                                        <button
                                            className="icon-button"
                                            disabled={deleteRule.isPending}
                                            onClick={() =>
                                                window.confirm(
                                                    'Forget automatic category for ' +
                                                        rule.referenceText +
                                                        '?'
                                                ) && deleteRule.mutate(rule.id)
                                            }
                                            aria-label={
                                                'Forget automatic category for ' +
                                                rule.referenceText
                                            }
                                        >
                                            <Trash2 />
                                        </button>
                                    </article>
                                ))}
                            </div>
                        )}
                        <InlineError>
                            {deleteRule.error && apiError(deleteRule.error)}
                        </InlineError>
                    </Card>
                </div>

                <aside className="settings-side page-stack">
                    <Card>
                        <div className="settings-card-heading">
                            <div>
                                <span className="settings-icon">
                                    <Sun />
                                </span>
                                <span>
                                    <h2>Appearance</h2>
                                    <p>
                                        Finova follows your preference on this
                                        device.
                                    </p>
                                </span>
                            </div>
                        </div>
                        <div className="theme-options">
                            {[
                                ['system', 'System', ShieldCheck],
                                ['light', 'Light', Sun],
                                ['dark', 'Dark', Moon],
                            ].map(([value, label, icon]) => (
                                <button
                                    key={value}
                                    className={
                                        preference === value ? 'selected' : ''
                                    }
                                    onClick={() => setPreference(value)}
                                >
                                    {createElement(icon)}
                                    <span>{label}</span>
                                </button>
                            ))}
                        </div>
                    </Card>
                    <Card className="privacy-card">
                        <ShieldCheck />
                        <h3>Private by design</h3>
                        <p>
                            This Finova workspace has no external sign-in or
                            bank connection. Keep the host network trusted and
                            back up PostgreSQL regularly.
                        </p>
                    </Card>
                </aside>

                <AccountEditor
                    open={Boolean(accountEditor)}
                    account={accountEditor?.id ? accountEditor : null}
                    onClose={() => setAccountEditor(false)}
                />
                <CategoryEditor
                    open={Boolean(categoryEditor)}
                    category={categoryEditor?.id ? categoryEditor : null}
                    onClose={() => setCategoryEditor(false)}
                    save={saveCategory}
                />
                <RuleEditor
                    open={Boolean(ruleEditor)}
                    rule={ruleEditor?.id ? ruleEditor : null}
                    categories={(categories.data || []).filter(
                        (category) => !category.isArchived
                    )}
                    onClose={() => setRuleEditor(false)}
                    save={saveRule}
                />
            </div>
        </PageState>
    );
}

function AccountPosition({ account }) {
    if (account.accountType !== 'credit') {
        return (
            <span>
                <strong>{money(account.balance)}</strong>
                <small>{money(account.safeZoneAmount)} protected</small>
            </span>
        );
    }

    const position =
        Number(account.debtBalance) > 0
            ? `${money(account.debtBalance)} owed`
            : Number(account.creditBalance) > 0
              ? `${money(account.creditBalance)} in credit`
              : 'Settled';
    return (
        <span>
            <strong>{position}</strong>
            <small>
                {account.creditLimit
                    ? `${money(account.availableCredit)} available · ${percent(account.creditUtilizationPercent)} used`
                    : 'Add a credit limit to track utilisation'}
            </small>
        </span>
    );
}

function AccountEditor({ open, account, onClose }) {
    const blank = useMemo(
        () => ({
            name: '',
            primaryHolderName: '',
            secondaryHolderName: '',
            isShared: false,
            accountType: 'current',
            institution: '',
            lastFour: '',
            openingBalance: 0,
            openingDate: todayIso(),
            creditLimit: '',
            safeZoneAmount: 0,
            includeInSafeToSpend: true,
            isArchived: false,
        }),
        []
    );
    const [form, setForm] = useState(blank);
    useEffect(() => {
        setForm(
            account
                ? {
                      name: account.name,
                      isShared: account.isShared,
                      accountType: account.accountType,
                      primaryHolderName:
                          account.primaryHolderName || account.ownerName || '',
                      secondaryHolderName: account.secondaryHolderName || '',
                      institution: account.institution || '',
                      lastFour: account.lastFour || '',
                      creditLimit: account.creditLimit ?? '',
                      safeZoneAmount: account.safeZoneAmount,
                      includeInSafeToSpend: account.includeInSafeToSpend,
                      isArchived: account.isArchived,
                  }
                : blank
        );
    }, [account, open, blank]);
    const save = useFinovaMutation(
        account ? mutations.updateAccount : mutations.createAccount,
        [
            queryKeys.accounts,
            queryKeys.dashboard,
            queryKeys.safety,
            queryKeys.goals,
        ],
        {
            successMessage: account ? 'Account updated.' : 'Account created.',
        }
    );
    const submit = async (event) => {
        event.preventDefault();
        const body = {
            ...form,
            openingBalance: Number(form.openingBalance || 0),
            creditLimit:
                form.creditLimit === '' ? null : Number(form.creditLimit),
            safeZoneAmount:
                form.accountType === 'credit'
                    ? 0
                    : Number(form.safeZoneAmount || 0),
            secondaryHolderName: form.isShared
                ? form.secondaryHolderName
                : null,
            includeInSafeToSpend:
                form.accountType === 'credit'
                    ? false
                    : form.includeInSafeToSpend,
        };
        try {
            await save.mutateAsync(account ? { id: account.id, body } : body);
            onClose();
        } catch {
            // The mutation error remains visible in the open form.
        }
    };
    return (
        <Modal
            open={open}
            onClose={onClose}
            title={account ? 'Edit account' : 'Add an account'}
            copy="Safe-zone floors are protected before Finova calculates available money."
        >
            <form className="form-grid" onSubmit={submit}>
                <Field label="Account name" className="span-2">
                    <input
                        required
                        value={form.name || ''}
                        onChange={(event) =>
                            setForm({ ...form, name: event.target.value })
                        }
                    />
                </Field>
                <Field label="Account ownership" className="span-2">
                    <select
                        value={form.isShared ? 'joint' : 'personal'}
                        onChange={(event) =>
                            setForm({
                                ...form,
                                isShared: event.target.value === 'joint',
                            })
                        }
                    >
                        <option value="personal">Personal account</option>
                        <option value="joint">Joint account</option>
                    </select>
                </Field>
                {form.isShared ? (
                    <>
                        <Field label="First account holder">
                            <input
                                required
                                autoComplete="name"
                                value={form.primaryHolderName || ''}
                                onChange={(event) =>
                                    setForm({
                                        ...form,
                                        primaryHolderName: event.target.value,
                                    })
                                }
                            />
                        </Field>
                        <Field label="Second account holder">
                            <input
                                required
                                autoComplete="name"
                                value={form.secondaryHolderName || ''}
                                onChange={(event) =>
                                    setForm({
                                        ...form,
                                        secondaryHolderName: event.target.value,
                                    })
                                }
                            />
                        </Field>
                    </>
                ) : (
                    <Field label="Account holder name" className="span-2">
                        <input
                            required
                            autoComplete="name"
                            value={form.primaryHolderName || ''}
                            onChange={(event) =>
                                setForm({
                                    ...form,
                                    primaryHolderName: event.target.value,
                                })
                            }
                        />
                    </Field>
                )}
                <Field label="Account type">
                    <select
                        value={form.accountType}
                        onChange={(event) =>
                            setForm({
                                ...form,
                                accountType: event.target.value,
                                safeZoneAmount:
                                    event.target.value === 'credit'
                                        ? 0
                                        : form.safeZoneAmount,
                                includeInSafeToSpend: ![
                                    'savings',
                                    'credit',
                                ].includes(event.target.value),
                            })
                        }
                    >
                        <option value="current">Current</option>
                        <option value="savings">Savings</option>
                        <option value="credit">Credit card</option>
                        <option value="cash">Cash</option>
                        <option value="investment">Investment</option>
                    </select>
                </Field>
                <Field label="Institution">
                    <input
                        value={form.institution || ''}
                        onChange={(event) =>
                            setForm({
                                ...form,
                                institution: event.target.value,
                            })
                        }
                    />
                </Field>
                <Field label="Last four digits">
                    <input
                        maxLength="4"
                        inputMode="numeric"
                        value={form.lastFour || ''}
                        onChange={(event) =>
                            setForm({
                                ...form,
                                lastFour: event.target.value.replace(/\D/g, ''),
                            })
                        }
                    />
                </Field>
                {form.accountType === 'credit' ? (
                    <Field label="Credit limit">
                        <input
                            type="number"
                            min="0"
                            step="0.01"
                            value={form.creditLimit ?? ''}
                            placeholder="Optional"
                            onChange={(event) =>
                                setForm({
                                    ...form,
                                    creditLimit: event.target.value,
                                })
                            }
                        />
                    </Field>
                ) : (
                    <Field label="Safe-zone floor">
                        <input
                            type="number"
                            min="0"
                            step="0.01"
                            value={form.safeZoneAmount || 0}
                            onChange={(event) =>
                                setForm({
                                    ...form,
                                    safeZoneAmount: event.target.value,
                                })
                            }
                        />
                    </Field>
                )}
                {!account && (
                    <>
                        <Field
                            label={
                                form.accountType === 'credit'
                                    ? 'Current amount owed'
                                    : 'Opening balance'
                            }
                        >
                            <input
                                type="number"
                                min={
                                    form.accountType === 'credit'
                                        ? '0'
                                        : undefined
                                }
                                step="0.01"
                                value={form.openingBalance}
                                onChange={(event) =>
                                    setForm({
                                        ...form,
                                        openingBalance: event.target.value,
                                    })
                                }
                            />
                        </Field>
                        <Field label="Opening date">
                            <input
                                required
                                type="date"
                                value={form.openingDate}
                                onChange={(event) =>
                                    setForm({
                                        ...form,
                                        openingDate: event.target.value,
                                    })
                                }
                            />
                        </Field>
                    </>
                )}
                {form.accountType === 'credit' ? (
                    <div className="check-row span-2">
                        <ShieldCheck />
                        <span>
                            <strong>Tracked as household debt</strong>
                            <small>
                                Credit cards never increase safe-to-spend or
                                fund savings goals. Purchases increase the
                                amount owed; repayments reduce it.
                            </small>
                        </span>
                    </div>
                ) : (
                    <label className="check-row span-2">
                        <input
                            type="checkbox"
                            checked={form.includeInSafeToSpend || false}
                            onChange={(event) =>
                                setForm({
                                    ...form,
                                    includeInSafeToSpend: event.target.checked,
                                })
                            }
                        />
                        <span>
                            <strong>Include in safe to spend</strong>
                            <small>
                                Savings accounts are normally excluded.
                            </small>
                        </span>
                    </label>
                )}
                {account && (
                    <label className="check-row danger-check span-2">
                        <input
                            type="checkbox"
                            checked={form.isArchived || false}
                            onChange={(event) =>
                                setForm({
                                    ...form,
                                    isArchived: event.target.checked,
                                })
                            }
                        />
                        <span>
                            <strong>
                                <Archive /> Archive account
                            </strong>
                            <small>
                                History remains intact but the account leaves
                                active totals.
                            </small>
                        </span>
                    </label>
                )}
                <InlineError className="span-2">
                    {save.error && apiError(save.error)}
                </InlineError>
                <div className="modal-actions span-2">
                    <button
                        type="button"
                        className="button secondary"
                        onClick={onClose}
                    >
                        Cancel
                    </button>
                    <button className="button" disabled={save.isPending}>
                        {save.isPending ? 'Saving…' : 'Save account'}
                    </button>
                </div>
            </form>
        </Modal>
    );
}

function CategoryEditor({ open, category, onClose, save }) {
    const blank = useMemo(
        () => ({
            name: '',
            kind: 'expense',
            iconKey: 'tag',
            colorKey: 'blue',
            isArchived: false,
        }),
        []
    );
    const [form, setForm] = useState(blank);
    useEffect(() => {
        setForm(category ? { ...category } : blank);
    }, [category, open, blank]);
    const submit = async (event) => {
        event.preventDefault();
        try {
            await save.mutateAsync(
                category ? { id: category.id, body: form } : { body: form }
            );
            onClose();
        } catch {
            // Keep the modal open so the inline error remains visible.
        }
    };
    return (
        <Modal
            open={open}
            onClose={onClose}
            title={category ? 'Edit category' : 'Add a category'}
            copy="Categories keep transaction history meaningful and drive budgets."
        >
            <form className="form-grid" onSubmit={submit}>
                <Field label="Name" className="span-2">
                    <input
                        required
                        maxLength="120"
                        value={form.name}
                        onChange={(event) =>
                            setForm({ ...form, name: event.target.value })
                        }
                    />
                </Field>
                <Field label="Kind">
                    <select
                        value={form.kind}
                        onChange={(event) =>
                            setForm({ ...form, kind: event.target.value })
                        }
                    >
                        <option value="expense">Expense</option>
                        <option value="income">Income</option>
                        <option value="transfer">Transfer</option>
                    </select>
                </Field>
                <Field label="Colour">
                    <select
                        value={form.colorKey}
                        onChange={(event) =>
                            setForm({ ...form, colorKey: event.target.value })
                        }
                    >
                        {[
                            'blue',
                            'cyan',
                            'mint',
                            'violet',
                            'coral',
                            'amber',
                            'rose',
                            'slate',
                        ].map((value) => (
                            <option key={value} value={value}>
                                {value[0].toUpperCase() + value.slice(1)}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field
                    label="Icon key"
                    className="span-2"
                    hint="Use a short icon name such as tag, house, or receipt."
                >
                    <input
                        required
                        maxLength="40"
                        value={form.iconKey}
                        onChange={(event) =>
                            setForm({ ...form, iconKey: event.target.value })
                        }
                    />
                </Field>
                {category && (
                    <label className="check-row danger-check span-2">
                        <input
                            type="checkbox"
                            checked={form.isArchived || false}
                            onChange={(event) =>
                                setForm({
                                    ...form,
                                    isArchived: event.target.checked,
                                })
                            }
                        />
                        <span>
                            <strong>
                                <Archive /> Archive category
                            </strong>
                            <small>
                                Archived categories remain on history but cannot
                                be newly selected.
                            </small>
                        </span>
                    </label>
                )}
                <InlineError className="span-2">
                    {save.error && apiError(save.error)}
                </InlineError>
                <div className="modal-actions span-2">
                    <button
                        type="button"
                        className="button secondary"
                        onClick={onClose}
                    >
                        Cancel
                    </button>
                    <button className="button" disabled={save.isPending}>
                        {save.isPending ? 'Saving…' : 'Save category'}
                    </button>
                </div>
            </form>
        </Modal>
    );
}

function RuleEditor({ open, rule, categories, onClose, save }) {
    const blank = useMemo(
        () => ({
            matchText: '',
            direction: 'out',
            categoryId: '',
            priority: 100,
            isActive: true,
        }),
        []
    );
    const [form, setForm] = useState(blank);
    useEffect(() => {
        setForm(
            rule ? { ...rule, categoryId: String(rule.categoryId) } : blank
        );
    }, [rule, open, blank]);
    const submit = async (event) => {
        event.preventDefault();
        try {
            const body = {
                ...form,
                categoryId: Number(form.categoryId),
                priority: Number(form.priority),
            };
            await save.mutateAsync(rule ? { id: rule.id, body } : { body });
            onClose();
        } catch {
            // Keep the modal open so the inline error remains visible.
        }
    };
    return (
        <Modal
            open={open}
            onClose={onClose}
            title={
                rule
                    ? 'Edit automatic category rule'
                    : 'Add an automatic category rule'
            }
            copy="Rules affect future imports only. Lower priority numbers run first when references overlap."
        >
            <form className="form-grid" onSubmit={submit}>
                <Field label="Reference" className="span-2">
                    <input
                        required
                        maxLength="200"
                        value={form.matchText}
                        onChange={(event) =>
                            setForm({ ...form, matchText: event.target.value })
                        }
                        placeholder="Merchant or bank reference"
                    />
                </Field>
                <Field label="Direction">
                    <select
                        value={form.direction}
                        onChange={(event) =>
                            setForm({ ...form, direction: event.target.value })
                        }
                    >
                        <option value="out">Money out</option>
                        <option value="in">Money in</option>
                        <option value="any">Money in or out</option>
                    </select>
                </Field>
                <Field label="Priority">
                    <input
                        required
                        type="number"
                        min="1"
                        max="100000"
                        value={form.priority}
                        onChange={(event) =>
                            setForm({ ...form, priority: event.target.value })
                        }
                    />
                </Field>
                <Field label="Category" className="span-2">
                    <select
                        required
                        value={form.categoryId}
                        onChange={(event) =>
                            setForm({ ...form, categoryId: event.target.value })
                        }
                    >
                        <option value="">Choose category</option>
                        {categories.map((category) => (
                            <option key={category.id} value={category.id}>
                                {category.name}
                            </option>
                        ))}
                    </select>
                </Field>
                <label className="check-row span-2">
                    <input
                        type="checkbox"
                        checked={form.isActive}
                        onChange={(event) =>
                            setForm({ ...form, isActive: event.target.checked })
                        }
                    />
                    <span>
                        <strong>Rule active</strong>
                        <small>
                            Inactive rules remain saved but are ignored during
                            imports.
                        </small>
                    </span>
                </label>
                <InlineError className="span-2">
                    {save.error && apiError(save.error)}
                </InlineError>
                <div className="modal-actions span-2">
                    <button
                        type="button"
                        className="button secondary"
                        onClick={onClose}
                    >
                        Cancel
                    </button>
                    <button className="button" disabled={save.isPending}>
                        {save.isPending ? 'Saving…' : 'Save rule'}
                    </button>
                </div>
            </form>
        </Modal>
    );
}
