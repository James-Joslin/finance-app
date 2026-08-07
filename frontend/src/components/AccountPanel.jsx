import React from 'react';
import NewAccountForm from './NewAccountForm';

export default function AccountPanel() {
    return (
        <div className="bg-white p-4 rounded shadow">
            <h2 className="text-lg font-semibold mb-2">Add New Account</h2>
            <NewAccountForm />
        </div>
    );
}