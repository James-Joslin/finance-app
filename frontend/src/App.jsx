import React, { useState } from 'react';
import Sidebar from './components/Sidebar';
import MainContent from './components/MainContent';
import { AccountProvider } from './contexts/AccountContext';

export default function App() {
    const [selectedTab, setSelectedTab] = useState('transactions');

    return (
        <AccountProvider>
            <div className="flex h-screen">
                <Sidebar selected={selectedTab} onSelect={setSelectedTab} />
                <MainContent selectedTab={selectedTab} />
            </div>
        </AccountProvider>
    );
}
