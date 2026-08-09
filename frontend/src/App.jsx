import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { LoaderCircle } from 'lucide-react';
import AppShell from './components/AppShell';

const GoalsPage = lazy(() => import('./pages/GoalsPage'));
const InsightsPage = lazy(() => import('./pages/InsightsPage'));
const OverviewPage = lazy(() => import('./pages/OverviewPage'));
const PlanPage = lazy(() => import('./pages/PlanPage'));
const SettingsPage = lazy(() => import('./pages/SettingsPage'));
const TransactionsPage = lazy(() => import('./pages/TransactionsPage'));

function PageFallback() {
    return <div className="page-state"><LoaderCircle className="spin" /><p>Opening Finova…</p></div>;
}

export default function App() {
    return (
        <Suspense fallback={<PageFallback />}>
        <Routes>
            <Route element={<AppShell />}>
                <Route index element={<OverviewPage />} />
                <Route path="transactions" element={<TransactionsPage />} />
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
