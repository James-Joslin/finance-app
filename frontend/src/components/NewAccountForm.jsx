import React, { useState } from 'react';
import axios from '../api/api';
import { useAccounts } from '../contexts/useAccounts';

export default function NewAccountForm() {
    const { fetchAccounts } = useAccounts(); // Get fetchAccounts from context
    
    const [form, setForm] = useState({ 
        AccountName: '', 
        FirstName: '', 
        LastName: '', 
        StartingBalance: '', 
        StartingDate: '' 
    });
    const [status, setStatus] = useState('');

    const handleChange = (e) => setForm({ ...form, [e.target.name]: e.target.value });

    const handleSubmit = async (e) => {
        e.preventDefault();
        try {
            const res = await axios.post('/uploads/newAccount', {
                AccountName: form.AccountName,
                FirstName: form.FirstName,
                LastName: form.LastName,
                StartingBalance: parseFloat(form.StartingBalance),
                StartingDate: form.StartingDate,
            });
            
            // Refresh the accounts list
            await fetchAccounts();
            
            setStatus(`Account created with ID ${res.data.account_id}`);
            
            // Clear form after success
            setForm({ 
                AccountName: '', 
                FirstName: '', 
                LastName: '', 
                StartingBalance: '', 
                StartingDate: '' 
            });
        } catch (err) {
            setStatus(`Error: ${err.response?.data?.error || err.message}`);
        }
    };

    return (
        <form onSubmit={handleSubmit} className="space-y-2">
            <input 
                name="AccountName" 
                type="text" 
                placeholder="Account Name" 
                value={form.AccountName}
                onChange={handleChange} 
                className="border p-2 w-full" 
            />
            <div className="grid grid-cols-2 gap-1">
                <input 
                    name="FirstName" 
                    type="text" 
                    placeholder="First Name" 
                    value={form.FirstName}
                    onChange={handleChange} 
                    className="border p-1 w-full" 
                />
                <input 
                    name="LastName" 
                    type="text" 
                    placeholder="Last Name" 
                    value={form.LastName}
                    onChange={handleChange} 
                    className="border p-1 w-full" 
                />
            </div>
            <input 
                name="StartingBalance" 
                type="number" 
                step="0.01" 
                placeholder="Starting Balance" 
                value={form.StartingBalance}
                onChange={handleChange} 
                className="border p-2 w-full" 
            />
            <input 
                name="StartingDate" 
                placeholder="Starting Balance Date" 
                type="date" 
                value={form.StartingDate}
                onChange={handleChange} 
                className="border p-2 w-full" 
            />
            <button className="bg-blue-600 text-white px-4 py-2 rounded">Create Account</button>
            {status && <p className="text-sm">{status}</p>}
        </form>
    );
}