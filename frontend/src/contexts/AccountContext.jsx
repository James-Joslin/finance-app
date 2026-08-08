// contexts/AccountContext.jsx
import React, { useState, useEffect } from 'react';
import axios from '../api/api';
import { AccountContext } from './accountContext';

export const AccountProvider = ({ children }) => {
    const [accounts, setAccounts] = useState([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(null);
    const [selectedAccountId, setSelectedAccountId] = useState(null);

    const fetchAccounts = async () => {
        setLoading(true);
        setError(null);
        try {
            const res = await axios.get('/uploads/getAccounts');
            const accountObjects = res.data.Rows.map((row) => ({
                id: row[0],
                name: row[1],
            }));
            setAccounts(accountObjects);
            return accountObjects;
        } catch (error) {
            console.error('Error fetching accounts:', error);
            setError(error.message);
            throw error;
        } finally {
            setLoading(false);
        }
    };

    // Fetch accounts on mount
    useEffect(() => {
        fetchAccounts();
    }, []);

    return (
        <AccountContext.Provider
            value={{
                accounts,
                fetchAccounts,
                loading,
                error,
                setAccounts,
                selectedAccountId,
                setSelectedAccountId,
            }}
        >
            {children}
        </AccountContext.Provider>
    );
};
