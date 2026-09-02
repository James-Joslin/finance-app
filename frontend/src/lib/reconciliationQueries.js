import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import api from '../api/api';

export const reconciliationKeys = {
    sessions: (accountId) => ['statement-reconciliation', accountId || 'all'],
    session: (id) => ['statement-reconciliation-session', id],
};

const get = async (url, params) => (await api.get(url, { params })).data;
const post = async (url, body) => (await api.post(url, body)).data;
const patch = async (url, body) => (await api.patch(url, body)).data;
const del = async (url) => (await api.delete(url)).data;

export const useReconciliationSessions = (accountId) =>
    useQuery({
        queryKey: reconciliationKeys.sessions(accountId),
        queryFn: () =>
            get('/reconciliation', accountId ? { accountId } : undefined),
    });

export const useStatementSession = (id) =>
    useQuery({
        queryKey: reconciliationKeys.session(id),
        queryFn: () => get('/reconciliation/' + id),
        enabled: Boolean(id),
    });

export const reconciliationMutations = {
    create: (body) => post('/reconciliation', body),
    setCleared: ({ sessionId, transactionId, cleared }) =>
        patch(
            `/reconciliation/${sessionId}/transactions/${transactionId}/cleared`,
            {
                cleared,
            }
        ),
    adjustment: (sessionId) => post(`/reconciliation/${sessionId}/adjustment`),
    deleteAdjustment: (sessionId) =>
        del(`/reconciliation/${sessionId}/adjustment`),
    close: (sessionId) => post(`/reconciliation/${sessionId}/close`),
};

export const useReconciliationMutation = (mutationFn, accountId, sessionId) => {
    const client = useQueryClient();
    return useMutation({
        mutationFn,
        retry: false,
        onSuccess: async (data) => {
            await Promise.all([
                client.invalidateQueries({
                    queryKey: reconciliationKeys.sessions(accountId),
                }),
                client.invalidateQueries({
                    queryKey: reconciliationKeys.session(
                        sessionId || data?.session?.id
                    ),
                }),
                client.invalidateQueries({ queryKey: ['accounts'] }),
                client.invalidateQueries({ queryKey: ['dashboard'] }),
                client.invalidateQueries({ queryKey: ['insights'] }),
            ]);
        },
    });
};
