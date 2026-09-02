import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { AlertCircle, LoaderCircle } from 'lucide-react';
import AppShell from './components/AppShell';
import EnrollmentPage from './pages/EnrollmentPage';
import { apiError } from './lib/format';
import { useEnrollmentStatus } from './lib/queries';

const GoalsPage = lazy(() => import('./pages/GoalsPage'));
const InsightsPage = lazy(() => import('./pages/InsightsPage'));
const OverviewPage = lazy(() => import('./pages/OverviewPage'));
const PlanPage = lazy(() => import('./pages/PlanPage'));
const SettingsPage = lazy(() => import('./pages/SettingsPage'));
const TransactionsPage = lazy(() => import('./pages/TransactionsPage'));
const ReconciliationPage = lazy(() => import('./pages/ReconciliationPage'));

function PageFallback() {
    return (
        <div className="page-state">
            <LoaderCircle className="spin" />
            <p>Opening Finova…</p>
        </div>
    );
}

export default function App() {
    const enrollment = useEnrollmentStatus();
    if (enrollment.isLoading) return <PageFallback />;
    if (enrollment.error) {
        return (
            <div className="enrollment-state" role="alert">
                <AlertCircle />
                <h1>Finova could not start</h1>
                <p>{apiError(enrollment.error)}</p>
                <button
                    className="button"
                    onClick={() => enrollment.refetch()}
                    disabled={enrollment.isFetching}
                >
                    {enrollment.isFetching ? 'Trying again…' : 'Try again'}
                </button>
            </div>
        );
    }
    if (!enrollment.data?.isEnrolled) return <EnrollmentPage />;

    return (
        <Suspense fallback={<PageFallback />}>
            <Routes>
                <Route element={<AppShell />}>
                    <Route index element={<OverviewPage />} />
                    <Route path="transactions" element={<TransactionsPage />} />
                    <Route
                        path="reconciliation"
                        element={<ReconciliationPage />}
                    />
                    <Route path="plan" element={<PlanPage />} />
                    <Route path="goals" element={<GoalsPage />} />
                    <Route path="insights" element={<InsightsPage />} />
                    <Route path="settings" element={<SettingsPage />} />
                    <Route path="*" element={<Navigate to="/" replace />} />
                </Route>
            </Routes>
        </Suspense>
    );
}
