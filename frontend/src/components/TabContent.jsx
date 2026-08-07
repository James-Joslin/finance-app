import React from 'react';
import TransactionPanel from './TransactionPanel';
import ReportPanel from './ReportPanel';

export default function TabContent({ selectedTab }) {
    return (
        <div className="bg-white p-4 rounded shadow">
            {selectedTab === "transactions" && <TransactionPanel />}
            {selectedTab === "reports" && <ReportPanel />}
        </div>
    );
}