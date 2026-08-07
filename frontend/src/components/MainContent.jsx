import React from 'react';
// import AccountPanel from './AccountPanel';
import TabContent from './TabContent';

export default function MainContent({ selectedTab }) {
    return (
        <div className="flex-1 p-6 space-y-6 bg-gray-50 overflow-y-auto">
            {/* <AccountPanel /> */}
            <TabContent selectedTab={selectedTab} />
        </div>
    );
}