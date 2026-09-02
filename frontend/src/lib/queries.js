import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import api from '../api/api';
import { useFeedback } from '../components/ui';

export const queryKeys = {
    enrollment: ['enrollment'],
    settings: ['settings'],
    accounts: ['accounts'],
    categories: ['categories'],
    rules: ['category-rules'],
    dashboard: ['dashboard'],
    goals: ['goals'],
    recurring: ['recurring'],
    suggestions: ['recurring-suggestions'],
    transaction: (id) => ['transaction', id],
    occurrences: ['recurring-occurrences'],
    budgets: ['budgets'],
    safety: ['safety'],
    transactions: (params) => ['transactions', params],
    imports: (params) => ['transaction-imports', params],
    importRows: (batchId, params) => [
        'transaction-import-rows',
        batchId,
        params,
    ],
    insights: (params) => ['insights', params],
};

const get = async (url, params) => (await api.get(url, { params })).data;
const post = async (url, body, config) =>
    (await api.post(url, body, config)).data;
const put = async (url, body) => (await api.put(url, body)).data;
const patch = async (url, body) => (await api.patch(url, body)).data;
const del = async (url) => (await api.delete(url)).data;

export const useEnrollmentStatus = () =>
    useQuery({
        queryKey: queryKeys.enrollment,
        queryFn: () => get('/enrollment'),
    });
export const useSettings = () =>
    useQuery({ queryKey: queryKeys.settings, queryFn: () => get('/settings') });
export const useAccounts = (includeArchived = false) =>
    useQuery({
        queryKey: [...queryKeys.accounts, includeArchived],
        queryFn: () => get('/accounts', { includeArchived }),
    });
export const useCategories = (includeArchived = false) =>
    useQuery({
        queryKey: [...queryKeys.categories, includeArchived],
        queryFn: () => get('/categories', { includeArchived }),
    });
export const useTransactionRules = () =>
    useQuery({
        queryKey: queryKeys.rules,
        queryFn: () => get('/categories/rules'),
    });
export const useDashboard = () =>
    useQuery({
        queryKey: queryKeys.dashboard,
        queryFn: () => get('/dashboard'),
    });
export const useGoals = () =>
    useQuery({ queryKey: queryKeys.goals, queryFn: () => get('/goals') });
export const useRecurring = () =>
    useQuery({
        queryKey: queryKeys.recurring,
        queryFn: () => get('/plan/recurring', { activeOnly: false }),
    });
export const useSuggestions = () =>
    useQuery({
        queryKey: queryKeys.suggestions,
        queryFn: () => get('/plan/suggestions'),
    });
export const useOccurrences = (params) =>
    useQuery({
        queryKey: [...queryKeys.occurrences, params || {}],
        queryFn: () => get('/plan/occurrences', params),
    });
export const useBudgets = (month) =>
    useQuery({
        queryKey: [...queryKeys.budgets, month || 'current'],
        queryFn: () => get('/plan/budgets', month ? { month } : undefined),
    });
export const useSafety = () =>
    useQuery({
        queryKey: queryKeys.safety,
        queryFn: () => get('/plan/safety'),
    });
export const useTransferCandidates = (transactionId) =>
    useQuery({
        queryKey: ['transfer-candidates', transactionId],
        queryFn: () =>
            get('/transactions/' + transactionId + '/transfer-candidates'),
        enabled: Boolean(transactionId),
    });
export const useTransactions = (params) =>
    useQuery({
        queryKey: queryKeys.transactions(params),
        queryFn: () => get('/transactions', params),
        placeholderData: (previous) => previous,
    });
export const useTransaction = (id) =>
    useQuery({
        queryKey: queryKeys.transaction(id),
        queryFn: () => get('/transactions/' + id),
        enabled: Boolean(id),
    });
export const useInsights = (params) =>
    useQuery({
        queryKey: queryKeys.insights(params),
        queryFn: () => get('/insights', params),
    });
export const useImports = (params, enabled = true) =>
    useQuery({
        queryKey: queryKeys.imports(params),
        queryFn: () => get('/transactions/imports', params),
        enabled,
    });
export const useImportRows = (batchId, params, enabled = true) =>
    useQuery({
        queryKey: queryKeys.importRows(batchId, params),
        queryFn: () =>
            get('/transactions/imports/' + batchId + '/rows', params),
        enabled: enabled && Boolean(batchId),
    });

export const useFinovaMutation = (
    mutationFn,
    invalidate = [],
    { successMessage } = {}
) => {
    const client = useQueryClient();
    const { notifySuccess } = useFeedback();
    return useMutation({
        mutationFn,
        retry: false,
        onSuccess: async (data, variables) => {
            await Promise.all(
                invalidate.map((key) =>
                    client.invalidateQueries({ queryKey: key })
                )
            );
            const message =
                typeof successMessage === 'function'
                    ? successMessage(data, variables)
                    : successMessage;
            if (message) notifySuccess(message);
        },
    });
};

export const mutations = {
    saveEnrollment: (body) => put('/enrollment', body),
    saveSettings: (body) => put('/settings', body),
    createAccount: (body) => post('/accounts', body),
    updateAccount: ({ id, body }) => put('/accounts/' + id, body),
    createTransaction: (body) => post('/transactions', body),
    updateTransaction: ({ id, body }) => put('/transactions/' + id, body),
    deleteTransaction: (id) => del('/transactions/' + id),
    updateTransactionCategory: ({ id, categoryId, saveRule }) =>
        patch('/transactions/' + id + '/category', { categoryId, saveRule }),
    createCategory: (body) => post('/categories', body),
    updateCategory: ({ id, body }) => put('/categories/' + id, body),
    deleteCategory: (id) => del('/categories/' + id),
    createTransactionRule: (body) => post('/categories/rules', body),
    updateTransactionRule: ({ id, body }) =>
        put('/categories/rules/' + id, body),
    deleteTransactionRule: (id) => del('/categories/rules/' + id),
    getTransferCandidates: (id) =>
        get('/transactions/' + id + '/transfer-candidates'),
    pairTransfer: ({ id, pairedTransactionId }) =>
        post('/transactions/' + id + '/transfer-pair', { pairedTransactionId }),
    unpairTransfer: (id) => del('/transactions/' + id + '/transfer-pair'),
    importTransactions: (form) =>
        post('/transactions/import', form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        }),
    previewImport: (form) =>
        post('/transactions/import/preview', form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        }),
    commitImport: (id) => post('/transactions/imports/' + id + '/commit'),
    undoImport: (id) => post('/transactions/imports/' + id + '/undo'),
    createGoal: (body) => post('/goals', body),
    updateGoal: ({ id, body }) => put('/goals/' + id, body),
    reorderGoals: (orderedIds) => post('/goals/reorder', { orderedIds }),
    uploadGoalImage: (form) =>
        post('/goals/images', form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        }),
    createRecurring: (body) => post('/plan/recurring', body),
    markTransactionRecurring: ({ id, body }) =>
        post('/transactions/' + id + '/recurring', body),
    updateRecurring: ({ id, body }) => put('/plan/recurring/' + id, body),
    deleteRecurring: (id) => del('/plan/recurring/' + id),
    updateOccurrence: ({ id, body }) => put('/plan/occurrences/' + id, body),
    saveBudget: (body) => put('/plan/budgets', body),
};

export const searchFinova = (query) => get('/search', { q: query });
