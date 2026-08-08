// contexts/useAccounts.js
import { useContext } from 'react';
import { AccountContext } from './accountContext';

export const useAccounts = () => {
    const context = useContext(AccountContext);
    if (!context) {
        throw new Error('useAccounts must be used within AccountProvider');
    }
    return context;
};
