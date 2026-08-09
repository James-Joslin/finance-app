import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import api from '../api/api';

export const queryKeys = {
    settings: ['settings'],
    accounts: ['accounts'],
    categories: ['categories'],
    rules: ['category-rules'],
    dashboard: ['dashboard'],
    goals: ['goals'],
    recurring: ['recurring'],
    suggestions: ['recurring-suggestions'],
    occurrences: ['recurring-occurrences'],
    budgets: ['budgets'],
    safety: ['safety'],
    transactions: (params) => ['transactions', params],
    insights: (params) => ['insights', params],
};

const get = async (url, params) => (await api.get(url, { params })).data;
const post = async (url, body, config) => (await api.post(url, body, config)).data;
const put = async (url, body) => (await api.put(url, body)).data;
const patch = async (url, body) => (await api.patch(url, body)).data;
const del = async (url) => (await api.delete(url)).data;

export const useSettings = () => useQuery({ queryKey: queryKeys.settings, queryFn: () => get('/settings') });
export const useAccounts = (includeArchived = false) => useQuery({
    queryKey: [...queryKeys.accounts, includeArchived],
    queryFn: () => get('/accounts', { includeArchived }),
});
export const useCategories = () => useQuery({ queryKey: queryKeys.categories, queryFn: () => get('/categories') });
export const useTransactionRules = () => useQuery({ queryKey: queryKeys.rules, queryFn: () => get('/categories/rules') });
export const useDashboard = () => useQuery({ queryKey: queryKeys.dashboard, queryFn: () => get('/dashboard') });
export const useGoals = () => useQuery({ queryKey: queryKeys.goals, queryFn: () => get('/goals') });
export const useRecurring = () => useQuery({ queryKey: queryKeys.recurring, queryFn: () => get('/plan/recurring', { activeOnly: false }) });
export const useSuggestions = () => useQuery({ queryKey: queryKeys.suggestions, queryFn: () => get('/plan/suggestions') });
export const useOccurrences = (params) => useQuery({
    queryKey: [...queryKeys.occurrences, params || {}],
    queryFn: () => get('/plan/occurrences', params),
});
export const useBudgets = (month) => useQuery({
    queryKey: [...queryKeys.budgets, month || 'current'],
    queryFn: () => get('/plan/budgets', month ? { month } : undefined),
});
export const useSafety = () => useQuery({ queryKey: queryKeys.safety, queryFn: () => get('/plan/safety') });
export const useTransactions = (params) => useQuery({
    queryKey: queryKeys.transactions(params),
    queryFn: () => get('/transactions', params),
    placeholderData: (previous) => previous,
});
export const useInsights = (params) => useQuery({
    queryKey: queryKeys.insights(params),
    queryFn: () => get('/insights', params),
});

export const useFinovaMutation = (mutationFn, invalidate = []) => {
    const client = useQueryClient();
    return useMutation({
        mutationFn,
        onSuccess: async () => {
            await Promise.all(invalidate.map((key) => client.invalidateQueries({ queryKey: key })));
        },
    });
};

export const mutations = {
    saveSettings: (body) => put('/settings', body),
    createAccount: (body) => post('/accounts', body),
    updateAccount: ({ id, body }) => put('/accounts/' + id, body),
    updateTransactionCategory: ({ id, categoryId, saveRule }) =>
        patch('/transactions/' + id + '/category', { categoryId, saveRule }),
    deleteTransactionRule: (id) => del('/categories/rules/' + id),
    importTransactions: (form) => post('/transactions/import', form, { headers: { 'Content-Type': 'multipart/form-data' } }),
    createGoal: (body) => post('/goals', body),
    updateGoal: ({ id, body }) => put('/goals/' + id, body),
    reorderGoals: (orderedIds) => post('/goals/reorder', { orderedIds }),
    uploadGoalImage: (form) => post('/goals/images', form, { headers: { 'Content-Type': 'multipart/form-data' } }),
    createRecurring: (body) => post('/plan/recurring', body),
    markTransactionRecurring: ({ id, body }) => post('/transactions/' + id + '/recurring', body),
    updateRecurring: ({ id, body }) => put('/plan/recurring/' + id, body),
    deleteRecurring: (id) => del('/plan/recurring/' + id),
    updateOccurrence: ({ id, body }) => put('/plan/occurrences/' + id, body),
    saveBudget: (body) => put('/plan/budgets', body),
};

export const searchFinova = (query) => get('/search', { q: query });
