import { useState } from 'react';
import {
    ArrowRight,
    Moon,
    ShieldCheck,
    Sparkles,
    Sun,
    UserRound,
    WalletCards,
} from 'lucide-react';
import FinovaLogo from '../components/FinovaLogo';
import { Field } from '../components/ui';
import { useTheme } from '../contexts/ThemeContext';
import { apiError } from '../lib/format';
import { mutations, queryKeys, useFinovaMutation } from '../lib/queries';

export default function EnrollmentPage() {
    const { resolved, setPreference } = useTheme();
    const [form, setForm] = useState({
        firstName: '',
        lastName: '',
        householdName: '',
    });
    const save = useFinovaMutation(mutations.saveEnrollment, [
        queryKeys.enrollment,
        queryKeys.settings,
        queryKeys.dashboard,
    ]);

    const setLastName = (lastName) => {
        const previousSuggestion = form.lastName.trim()
            ? form.lastName.trim() + ' Household'
            : '';
        const householdName =
            !form.householdName.trim() ||
            form.householdName === previousSuggestion
                ? lastName.trim()
                    ? lastName.trim() + ' Household'
                    : ''
                : form.householdName;
        setForm({ ...form, lastName, householdName });
    };

    const submit = async (event) => {
        event.preventDefault();
        await save.mutateAsync({
            firstName: form.firstName.trim(),
            lastName: form.lastName.trim(),
            householdName: form.householdName.trim(),
        });
    };

    return (
        <main className="enrollment-page">
            <div className="enrollment-glow enrollment-glow-one" />
            <div className="enrollment-glow enrollment-glow-two" />
            <header className="enrollment-header">
                <FinovaLogo />
                <button
                    className="icon-button enrollment-theme"
                    type="button"
                    onClick={() =>
                        setPreference(resolved === 'dark' ? 'light' : 'dark')
                    }
                    aria-label={
                        'Switch to ' +
                        (resolved === 'dark' ? 'light' : 'dark') +
                        ' mode'
                    }
                >
                    {resolved === 'dark' ? <Sun /> : <Moon />}
                </button>
            </header>

            <section className="enrollment-layout">
                <div className="enrollment-intro">
                    <span className="enrollment-eyebrow">
                        <Sparkles /> A calmer way to plan
                    </span>
                    <h1>Welcome to Finova.</h1>
                    <p>
                        Start with the basics. Your profile stays inside this
                        private household workspace and helps personalise the
                        experience.
                    </p>
                    <div className="enrollment-promises">
                        <span>
                            <ShieldCheck />
                            <strong>Private workspace</strong>
                            <small>
                                No bank connection or external account required.
                            </small>
                        </span>
                        <span>
                            <WalletCards />
                            <strong>Your existing data stays put</strong>
                            <small>
                                Enrollment never changes accounts or
                                transactions.
                            </small>
                        </span>
                    </div>
                </div>

                <section
                    className="enrollment-card"
                    aria-labelledby="enrollment-title"
                >
                    <div className="enrollment-card-heading">
                        <span>
                            <UserRound />
                        </span>
                        <div>
                            <h2 id="enrollment-title">Create your profile</h2>
                            <p>
                                You can change these details later in Settings.
                            </p>
                        </div>
                    </div>
                    <form
                        className="form-grid enrollment-form"
                        onSubmit={submit}
                    >
                        <Field label="First name">
                            <input
                                required
                                autoFocus
                                autoComplete="given-name"
                                maxLength="80"
                                value={form.firstName}
                                onChange={(event) =>
                                    setForm({
                                        ...form,
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
                                value={form.lastName}
                                onChange={(event) =>
                                    setLastName(event.target.value)
                                }
                            />
                        </Field>
                        <Field
                            label="Household name"
                            hint="This appears throughout Finova."
                            className="span-2"
                        >
                            <input
                                required
                                maxLength="120"
                                value={form.householdName}
                                placeholder="For example, The Smith Household"
                                onChange={(event) =>
                                    setForm({
                                        ...form,
                                        householdName: event.target.value,
                                    })
                                }
                            />
                        </Field>
                        {save.error && (
                            <p className="form-error span-2">
                                {apiError(save.error)}
                            </p>
                        )}
                        <button
                            className="button enrollment-submit span-2"
                            disabled={save.isPending}
                        >
                            {save.isPending ? (
                                'Creating your workspace…'
                            ) : (
                                <>
                                    Continue to Finova <ArrowRight />
                                </>
                            )}
                        </button>
                    </form>
                    <p className="enrollment-note">
                        <ShieldCheck /> Stored only in your Finova PostgreSQL
                        database.
                    </p>
                </section>
            </section>
        </main>
    );
}
