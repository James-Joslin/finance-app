// utils/accountUtils.js
import axios from '../api/api';

export const fetchAccounts = async () => {
    try {
        const res = await axios.get('/uploads/getAccounts');
        // Transform Rows array into objects
        const accountObjects = res.data.Rows.map((row) => ({
            id: row[0],
            name: row[1],
        }));
        return accountObjects;
    } catch (error) {
        console.error('Error fetching accounts:', error);
        throw error;
    }
};
