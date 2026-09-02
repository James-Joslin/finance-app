import { useSearchParams } from 'react-router-dom';
import { useEffect, useMemo, useState } from 'react';
import {
    ArrowDown,
    ArrowUp,
    CalendarDays,
    CheckCircle2,
    ImagePlus,
    Pencil,
    Plus,
    Sparkles,
    Target,
} from 'lucide-react';
import {
    GoalIconPicker,
    GoalVisual,
    goalColors,
} from '../components/GoalVisual';
import {
    Card,
    Field,
    InlineError,
    Modal,
    PageState,
    Pill,
    Progress,
} from '../components/ui';
import { useTheme } from '../contexts/ThemeContext';
import { apiError, money, percent, shortDate } from '../lib/format';
import {
    mutations,
    queryKeys,
    useAccounts,
    useFinovaMutation,
    useGoals,
} from '../lib/queries';
import { parseRecordId, useDeepLinkTarget } from '../utils/deepLink';
import { staticAssetUrl } from '../lib/staticAssets';

export default function GoalsPage() {
    const [searchParams] = useSearchParams();
    const goalId = parseRecordId(searchParams.get('goalId'));
    const goals = useGoals();
    const accounts = useAccounts();
    const [editor, setEditor] = useState(false);
    const { resolved } = useTheme();
    const active = goals.data?.items || [];
    const featured =
        active.find((goal) => goal.status === 'active' && !goal.isFunded) ||
        active[0];
    const others = active.filter((goal) => goal.id !== featured?.id);

    useDeepLinkTarget(goalId, goals.data, '[data-deep-link-type="goal"]');
    const night = resolved === 'dark' ? '_night' : '';
    const artwork = {
        cloud: staticAssetUrl(`decor/decor_cloud${night}.png`),
        circles: staticAssetUrl(`decor/decor_circles${night}.png`),
        tree: staticAssetUrl(`decor/decor_tree${night}.png`),
        wave: staticAssetUrl(`decor/decor_wave_02${night}.png`),
    };
    const reorder = useFinovaMutation(
        mutations.reorderGoals,
        [queryKeys.goals, queryKeys.dashboard],
        { successMessage: 'Goal priority updated.' }
    );
    const pageQueries = [goals, accounts];
    const move = (goalId, direction) => {
        const ids = active.map((goal) => goal.id);
        const index = ids.indexOf(goalId);
        const nextIndex = index + direction;
        if (nextIndex < 0 || nextIndex >= ids.length) return;
        [ids[index], ids[nextIndex]] = [ids[nextIndex], ids[index]];
        reorder.mutate(ids);
    };

    return (
        <PageState
            loading={goals.isLoading || accounts.isLoading}
            error={
                (goals.error || accounts.error) &&
                apiError(goals.error || accounts.error)
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
            <div className="page-stack">
                <div className="goals-summary-bar">
                    <div>
                        <span className="eyebrow">Household progress</span>
                        <strong>
                            {money(goals.data?.allocatedTotal)}{' '}
                            <small>of {money(goals.data?.targetTotal)}</small>
                        </strong>
                    </div>
                    <div>
                        <span>{percent(goals.data?.progressPercent)}</span>
                        <Progress value={goals.data?.progressPercent} />
                    </div>
                    <img
                        className="goals-summary-art"
                        src={artwork.wave}
                        alt=""
                        aria-hidden="true"
                    />
                    <button className="button" onClick={() => setEditor({})}>
                        <Plus /> Add goal
                    </button>
                </div>
                <InlineError>
                    {reorder.error && apiError(reorder.error)}
                </InlineError>

                {featured ? (
                    <FeaturedGoal
                        goal={featured}
                        artwork={artwork.cloud}
                        onEdit={() => setEditor(featured)}
                    />
                ) : (
                    <Card className="goal-onboarding">
                        <img
                            className="goal-onboarding-art"
                            src={artwork.tree}
                            alt=""
                            aria-hidden="true"
                        />
                        <GoalVisual iconKey="general_target" colorKey="blue" />
                        <div>
                            <span className="eyebrow">
                                <Sparkles /> Start with what matters most
                            </span>
                            <h2>Give your savings a destination</h2>
                            <p>
                                Choose an account, amount, and date. Finova will
                                calculate progress without moving your money.
                            </p>
                            <button
                                className="button"
                                onClick={() => setEditor({})}
                            >
                                <Plus /> Create your first goal
                            </button>
                        </div>
                    </Card>
                )}

                {others.length > 0 && (
                    <>
                        <section className="section-heading">
                            <div>
                                <span className="eyebrow">Your roadmap</span>
                                <h2>Other goals</h2>
                                <p>
                                    Priority determines how each account balance
                                    flows through its goals.
                                </p>
                            </div>
                        </section>
                        <div
                            className="goals-grid"
                            style={{
                                '--goal-card-art': `url("${artwork.circles}")`,
                            }}
                        >
                            {others.map((goal) => {
                                const position = active.findIndex(
                                    (item) => item.id === goal.id
                                );
                                return (
                                    <GoalCard
                                        key={goal.id}
                                        goal={goal}
                                        onEdit={() => setEditor(goal)}
                                        onUp={() => move(goal.id, -1)}
                                        onDown={() => move(goal.id, 1)}
                                        first={position === 0}
                                        last={position === active.length - 1}
                                        pending={reorder.isPending}
                                    />
                                );
                            })}
                        </div>
                    </>
                )}

                <GoalEditor
                    open={Boolean(editor)}
                    goal={editor?.id ? editor : null}
                    accounts={accounts.data || []}
                    nextPriority={active.length + 1}
                    onClose={() => setEditor(false)}
                />
            </div>
        </PageState>
    );
}

function FeaturedGoal({ goal, artwork, onEdit }) {
    return (
        <Card
            className="featured-goal"
            data-deep-link-type="goal"
            data-deep-link-id={goal.id}
        >
            <img
                className="featured-goal-art"
                src={artwork}
                alt=""
                aria-hidden="true"
            />
            <div className="featured-goal-copy">
                <div className="card-heading">
                    <div>
                        <Pill tone="info">Priority 1</Pill>
                        <h2>{goal.name}</h2>
                        <p>
                            {goal.description ||
                                'Your highest-priority savings target.'}
                        </p>
                    </div>
                    <button
                        className="icon-button"
                        onClick={onEdit}
                        aria-label={'Edit ' + goal.name}
                    >
                        <Pencil />
                    </button>
                </div>
                <div className="featured-progress">
                    <strong>
                        {money(goal.allocatedAmount)}{' '}
                        <small>/ {money(goal.targetAmount)}</small>
                    </strong>
                    <b>{percent(goal.progressPercent)}</b>
                </div>
                <Progress value={goal.progressPercent} />
                <div className="featured-meta">
                    <span>
                        <small>Still to go</small>
                        <strong>{money(goal.remainingAmount)}</strong>
                    </span>
                    <span>
                        <small>Target date</small>
                        <strong>
                            {goal.targetDate
                                ? shortDate(goal.targetDate)
                                : 'No date'}
                        </strong>
                    </span>
                    <span>
                        <small>Suggested pace</small>
                        <strong>
                            {goal.requiredMonthly
                                ? money(goal.requiredMonthly) + '/mo'
                                : 'Flexible'}
                        </strong>
                    </span>
                </div>
            </div>
            <GoalVisual
                iconKey={goal.iconKey}
                colorKey={goal.colorKey}
                imageUrl={goal.imageUrl}
                label={goal.name}
            />
            <div className="encouragement">
                <Sparkles />
                <p>
                    <strong>
                        {goal.isFunded
                            ? 'This goal is funded!'
                            : 'Keep the momentum going'}
                    </strong>
                    <br />
                    {goal.daysRemaining == null
                        ? 'Add a target date for a suggested monthly pace.'
                        : goal.daysRemaining < 0
                          ? 'The target date has passed—choose a fresh date when ready.'
                          : goal.daysRemaining + ' days remain.'}
                </p>
            </div>
        </Card>
    );
}

function GoalCard({ goal, onEdit, onUp, onDown, first, last, pending }) {
    return (
        <Card
            className="goal-card"
            data-deep-link-type="goal"
            data-deep-link-id={goal.id}
        >
            <GoalVisual
                iconKey={goal.iconKey}
                colorKey={goal.colorKey}
                imageUrl={goal.imageUrl}
                size="compact"
                label={goal.name}
            />
            <div className="goal-card-body">
                <div className="card-heading">
                    <div>
                        <strong>{goal.name}</strong>
                        <small>{goal.accountName}</small>
                    </div>
                    {goal.isFunded && <CheckCircle2 className="positive" />}
                </div>
                <div className="goal-card-title">
                    <span>
                        <strong>{money(goal.allocatedAmount)}</strong> /{' '}
                        {money(goal.targetAmount)}
                    </span>
                    <b>{percent(goal.progressPercent)}</b>
                </div>
                <Progress
                    value={goal.progressPercent}
                    tone={goal.isFunded ? 'success' : 'brand'}
                />
                <div className="goal-meta">
                    <span>{money(goal.remainingAmount)} to go</span>
                    <span>
                        {goal.targetDate
                            ? shortDate(goal.targetDate)
                            : 'No target date'}
                    </span>
                </div>
            </div>
            <div className="goal-actions">
                <button
                    className="icon-button"
                    disabled={first || pending}
                    onClick={onUp}
                    aria-label="Move goal up"
                >
                    <ArrowUp />
                </button>
                <button
                    className="icon-button"
                    disabled={last || pending}
                    onClick={onDown}
                    aria-label="Move goal down"
                >
                    <ArrowDown />
                </button>
                <button
                    className="icon-button"
                    onClick={onEdit}
                    aria-label={'Edit ' + goal.name}
                >
                    <Pencil />
                </button>
            </div>
        </Card>
    );
}

function GoalEditor({ open, goal, accounts, nextPriority, onClose }) {
    const defaults = useMemo(
        () => ({
            name: goal?.name || '',
            description: goal?.description || '',
            targetAmount: goal?.targetAmount || '',
            targetDate: goal?.targetDate || '',
            accountId: goal?.accountId || '',
            priorityOrder: goal?.priorityOrder || nextPriority,
            iconKey: goal?.iconKey || 'general_target',
            colorKey: goal?.colorKey || 'blue',
            imageId: goal?.imageId || null,
            status: goal?.status || 'active',
        }),
        [goal, nextPriority]
    );
    const [form, setForm] = useState(defaults);
    const [image, setImage] = useState(null);
    useEffect(() => {
        setForm(defaults);
        setImage(null);
    }, [defaults, open]);

    const save = useFinovaMutation(
        goal ? mutations.updateGoal : mutations.createGoal,
        [queryKeys.goals, queryKeys.dashboard],
        { successMessage: goal ? 'Goal updated.' : 'Goal created.' }
    );
    const upload = useFinovaMutation(mutations.uploadGoalImage);

    const submit = async (event) => {
        event.preventDefault();
        try {
            let imageId = form.imageId;
            if (image) {
                const body = new FormData();
                body.append('image', image);
                imageId = (await upload.mutateAsync(body)).id;
            }
            const body = {
                ...form,
                targetAmount: Number(form.targetAmount),
                targetDate: form.targetDate || null,
                accountId: Number(form.accountId),
                priorityOrder: Number(form.priorityOrder),
                imageId,
            };
            await save.mutateAsync(goal ? { id: goal.id, body } : body);
            onClose();
        } catch {
            // The upload or save error remains visible in the open form.
        }
    };

    return (
        <Modal
            open={open}
            onClose={onClose}
            title={goal ? 'Edit savings goal' : 'Create a savings goal'}
            copy="Goal allocations are virtual. Finova never moves money between your accounts."
            wide
        >
            <form className="goal-editor" onSubmit={submit}>
                <div className="goal-form-fields">
                    <Field label="Goal name" className="span-2">
                        <input
                            required
                            value={form.name}
                            onChange={(event) =>
                                setForm({ ...form, name: event.target.value })
                            }
                            placeholder="House deposit"
                        />
                    </Field>
                    <Field label="Target amount">
                        <input
                            required
                            min="0.01"
                            step="0.01"
                            type="number"
                            value={form.targetAmount}
                            onChange={(event) =>
                                setForm({
                                    ...form,
                                    targetAmount: event.target.value,
                                })
                            }
                        />
                    </Field>
                    <Field label="Target date">
                        <input
                            type="date"
                            value={form.targetDate}
                            onChange={(event) =>
                                setForm({
                                    ...form,
                                    targetDate: event.target.value,
                                })
                            }
                        />
                    </Field>
                    <Field label="Designated account" className="span-2">
                        <select
                            required
                            value={form.accountId}
                            onChange={(event) =>
                                setForm({
                                    ...form,
                                    accountId: event.target.value,
                                })
                            }
                        >
                            <option value="">Choose a savings account</option>
                            {accounts
                                .filter(
                                    (item) =>
                                        !item.isArchived &&
                                        item.accountType !== 'credit'
                                )
                                .map((item) => (
                                    <option key={item.id} value={item.id}>
                                        {item.name} · {money(item.balance)}
                                    </option>
                                ))}
                        </select>
                    </Field>
                    <Field label="Description" className="span-2">
                        <textarea
                            rows="2"
                            value={form.description}
                            onChange={(event) =>
                                setForm({
                                    ...form,
                                    description: event.target.value,
                                })
                            }
                            placeholder="A short reminder of why this matters…"
                        />
                    </Field>
                    <Field label="Status">
                        <select
                            value={form.status}
                            onChange={(event) =>
                                setForm({ ...form, status: event.target.value })
                            }
                        >
                            <option value="active">Active</option>
                            <option value="completed">Completed</option>
                            <option value="archived">Archived</option>
                        </select>
                    </Field>
                    <Field label="Accent colour">
                        <div className="color-picker">
                            {goalColors.map((color) => (
                                <button
                                    key={color}
                                    type="button"
                                    className={
                                        'color-choice color-' +
                                        color +
                                        (form.colorKey === color
                                            ? ' selected'
                                            : '')
                                    }
                                    onClick={() =>
                                        setForm({ ...form, colorKey: color })
                                    }
                                    aria-label={color}
                                />
                            ))}
                        </div>
                    </Field>
                </div>
                <div className="goal-visual-fields">
                    <GoalVisual
                        iconKey={form.iconKey}
                        colorKey={form.colorKey}
                        imageUrl={
                            image
                                ? URL.createObjectURL(image)
                                : form.imageId
                                  ? goal?.imageUrl
                                  : null
                        }
                        label={form.name || 'Goal preview'}
                    />
                    <span className="eyebrow">Choose an icon</span>
                    <GoalIconPicker
                        value={form.iconKey}
                        onChange={(iconKey) => {
                            setForm({ ...form, iconKey, imageId: null });
                            setImage(null);
                        }}
                    />
                    <label className="image-upload">
                        <ImagePlus />
                        <span>
                            <strong>Use your own image</strong>
                            <small>PNG, JPEG, or WebP · maximum 2 MB</small>
                        </span>
                        <input
                            type="file"
                            accept="image/png,image/jpeg,image/webp"
                            onChange={(event) =>
                                setImage(event.target.files[0])
                            }
                        />
                    </label>
                </div>
                <InlineError className="span-2">
                    {(save.error || upload.error) &&
                        apiError(save.error || upload.error)}
                </InlineError>
                <div className="modal-actions goal-editor-actions">
                    <button
                        type="button"
                        className="button secondary"
                        onClick={onClose}
                    >
                        Cancel
                    </button>
                    <button
                        className="button"
                        disabled={save.isPending || upload.isPending}
                    >
                        {save.isPending || upload.isPending
                            ? 'Saving…'
                            : goal
                              ? 'Save changes'
                              : 'Create goal'}
                    </button>
                </div>
            </form>
        </Modal>
    );
}
