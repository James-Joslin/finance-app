import React from 'react';
import AccountPanel from './AccountPanel';

export default function Sidebar({ selected, onSelect }) {
  return (
    <div className="w-60 bg-gray-800 text-white h-full p-4 space-y-4">
      <div onClick={() => onSelect("transactions")} className={`cursor-pointer p-2 rounded ${selected === "transactions" ? "bg-gray-700" : ""}`}>
        Transactions
      </div>
      <div onClick={() => onSelect("reports")} className={`cursor-pointer p-2 rounded ${selected === "reports" ? "bg-gray-700" : ""}`}>
        Reports
      </div>
      {/* className="flex-1 p-6 space-y-6 bg-gray-50 overflow-y-auto"> */}
      <div className='text-black'>  
        <AccountPanel />
      </div>
    </div>
  );
}