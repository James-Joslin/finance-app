import React, { useState, useEffect, useMemo } from 'react';
import axios from '../api/api';
import { buildReportTransactions } from '../utils/reportTransactions';
import { useAccounts } from '../contexts/useAccounts'; // Update this import path based on your solution

export default function TransactionReviewForm() {
    const { selectedAccountId, accounts } = useAccounts();
    const [transactions, setTransactions] = useState([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(null);

    // Filtering state
    const [filters, setFilters] = useState({
        startDate: '',
        endDate: '',
        minAmount: '',
        maxAmount: '',
        searchText: '',
        transactionType: 'all', // 'all', 'credits', 'debits'
    });
    const [isFilterOpen, setIsFilterOpen] = useState(false);

    // Pagination state
    const [currentPage, setCurrentPage] = useState(1);
    const [itemsPerPage, setItemsPerPage] = useState(10);

    const [summary, setSummary] = useState({
        totalDebits: 0,
        totalCredits: 0,
        balance: 0,
        transactionCount: 0,
    });

    // Fetch transactions when selectedAccountId changes
    useEffect(() => {
        if (selectedAccountId) {
            fetchTransactions(selectedAccountId);
            // Reset pagination when account changes
            setCurrentPage(1);
        } else {
            setTransactions([]);
            setSummary({
                totalDebits: 0,
                totalCredits: 0,
                balance: 0,
                transactionCount: 0,
            });
        }
        // Legacy compatibility component; the routed Finova table owns new transaction fetching.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [selectedAccountId]);

    const fetchTransactions = async (accountId) => {
        setLoading(true);
        setError(null);

        try {
            const res = await axios.post('/reports/getAccountTable', {
                accountId: parseInt(accountId),
            });

            // Extract headers and rows from the response
            const responseHeaders = res.data.Headers[0] || [];
            const responseRows = res.data.Rows || [];

            const sortedTransactions = buildReportTransactions(
                responseHeaders,
                responseRows
            );

            setTransactions(sortedTransactions);
            calculateSummary(sortedTransactions);
        } catch (err) {
            console.error('Error fetching transactions:', err);
            setError(
                err.response?.data?.error || 'Failed to fetch transactions'
            );
            setTransactions([]);
        } finally {
            setLoading(false);
        }
    };

    // Filter transactions based on current filters
    const filteredTransactions = useMemo(() => {
        return transactions.filter((transaction) => {
            // Date range filter
            if (filters.startDate) {
                const startDate = new Date(filters.startDate + 'T00:00:00');
                if (transaction.dateObj < startDate) return false;
            }
            if (filters.endDate) {
                const endDate = new Date(filters.endDate + 'T23:59:59.999');
                if (transaction.dateObj > endDate) return false;
            }

            // Amount range filter
            if (
                filters.minAmount !== '' &&
                Math.abs(transaction.amount) < parseFloat(filters.minAmount)
            ) {
                return false;
            }
            if (
                filters.maxAmount !== '' &&
                Math.abs(transaction.amount) > parseFloat(filters.maxAmount)
            ) {
                return false;
            }

            // Transaction type filter
            if (filters.transactionType === 'credits' && transaction.amount < 0)
                return false;
            if (filters.transactionType === 'debits' && transaction.amount > 0)
                return false;

            // Search text filter (searches in payee and memo)
            if (filters.searchText) {
                const searchLower = filters.searchText.toLowerCase();
                const payee = (transaction.payee || '').toLowerCase();
                const memo = (transaction.memo || '').toLowerCase();
                if (
                    !payee.includes(searchLower) &&
                    !memo.includes(searchLower)
                ) {
                    return false;
                }
            }

            return true;
        });
    }, [transactions, filters]);

    // Paginated transactions
    const paginatedTransactions = useMemo(() => {
        const startIndex = (currentPage - 1) * itemsPerPage;
        const endIndex = startIndex + itemsPerPage;
        return filteredTransactions.slice(startIndex, endIndex);
    }, [filteredTransactions, currentPage, itemsPerPage]);

    // Calculate total pages
    const totalPages = Math.ceil(filteredTransactions.length / itemsPerPage);

    // Update summary when filtered transactions change
    useEffect(() => {
        calculateSummary(filteredTransactions);
    }, [filteredTransactions]);

    // Reset to page 1 when filters change
    useEffect(() => {
        setCurrentPage(1);
    }, [filters]);

    const calculateSummary = (transactionList) => {
        const summary = transactionList.reduce(
            (acc, transaction) => {
                if (transaction.amount < 0) {
                    acc.totalDebits += Math.abs(transaction.amount);
                } else {
                    acc.totalCredits += transaction.amount;
                }
                acc.transactionCount++;
                return acc;
            },
            { totalDebits: 0, totalCredits: 0, transactionCount: 0 }
        );

        summary.balance = summary.totalCredits - summary.totalDebits;
        setSummary(summary);
    };

    const clearFilters = () => {
        setFilters({
            startDate: '',
            endDate: '',
            minAmount: '',
            maxAmount: '',
            searchText: '',
            transactionType: 'all',
        });
    };

    const formatCurrency = (amount) => {
        return new Intl.NumberFormat('en-GB', {
            style: 'currency',
            currency: 'GBP',
            minimumFractionDigits: 2,
        }).format(amount);
    };

    const formatDate = (dateString) => {
        if (!dateString) return '';
        const date = new Date(dateString);
        return date.toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
        });
    };

    const getSelectedAccountName = () => {
        const account = accounts.find((acc) => acc.id === selectedAccountId);
        return account ? account.name : '';
    };

    // Pagination helpers
    const goToPage = (page) => {
        setCurrentPage(Math.max(1, Math.min(page, totalPages)));
    };

    const getPaginationRange = () => {
        const range = [];
        const maxButtons = 5;
        let start = Math.max(1, currentPage - Math.floor(maxButtons / 2));
        let end = Math.min(totalPages, start + maxButtons - 1);

        if (end - start < maxButtons - 1) {
            start = Math.max(1, end - maxButtons + 1);
        }

        for (let i = start; i <= end; i++) {
            range.push(i);
        }
        return range;
    };

    // If no account is selected
    if (!selectedAccountId) {
        return (
            <div className="p-4 bg-white rounded-lg shadow">
                <h2 className="text-xl font-bold mb-4 text-gray-800">
                    Transaction Review
                </h2>
                <p className="text-gray-500">
                    Please select an account to view transactions
                </p>
            </div>
        );
    }

    // Loading state
    if (loading) {
        return (
            <div className="p-4 bg-white rounded-lg shadow">
                <h2 className="text-xl font-bold mb-4 text-gray-800">
                    Transaction Review - {getSelectedAccountName()}
                </h2>
                <div className="flex justify-center items-center py-8">
                    <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
                    <span className="ml-3 text-gray-600">
                        Loading transactions...
                    </span>
                </div>
            </div>
        );
    }

    // Error state
    if (error) {
        return (
            <div className="p-4 bg-white rounded-lg shadow">
                <h2 className="text-xl font-bold mb-4 text-gray-800">
                    Transaction Review - {getSelectedAccountName()}
                </h2>
                <div className="bg-red-50 border border-red-300 text-red-700 p-3 rounded">
                    Error: {error}
                </div>
                <button
                    onClick={() => fetchTransactions(selectedAccountId)}
                    className="mt-3 bg-blue-600 hover:bg-blue-700 text-white px-4 py-2 rounded"
                >
                    Retry
                </button>
            </div>
        );
    }

    return (
        <div className="p-4 bg-white rounded-lg shadow">
            <div className="flex justify-between items-center mb-4">
                <h2 className="text-xl font-bold text-gray-800">
                    Transaction Review - {getSelectedAccountName()}
                </h2>
                <div className="flex gap-2">
                    <button
                        onClick={() => setIsFilterOpen(!isFilterOpen)}
                        className={`px-3 py-1 rounded text-sm ${
                            isFilterOpen ||
                            Object.values(filters).some(
                                (v) => v !== '' && v !== 'all'
                            )
                                ? 'bg-blue-600 text-white'
                                : 'bg-gray-100 hover:bg-gray-200 text-gray-700'
                        }`}
                    >
                        {isFilterOpen ? 'Hide Filters' : 'Show Filters'}
                        {Object.values(filters).some(
                            (v) => v !== '' && v !== 'all'
                        ) && ' (Active)'}
                    </button>
                    <button
                        onClick={() => fetchTransactions(selectedAccountId)}
                        className="bg-gray-100 hover:bg-gray-200 text-gray-700 px-3 py-1 rounded text-sm"
                    >
                        Refresh
                    </button>
                </div>
            </div>

            {/* Filter Panel */}
            {isFilterOpen && (
                <div className="mb-4 p-4 bg-gray-50 rounded-lg border border-gray-200">
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                        {/* Date Range */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                Start Date
                            </label>
                            <input
                                type="date"
                                value={filters.startDate}
                                onChange={(e) =>
                                    setFilters({
                                        ...filters,
                                        startDate: e.target.value,
                                    })
                                }
                                className="w-full border border-gray-300 rounded px-2 py-1 text-sm"
                            />
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                End Date
                            </label>
                            <input
                                type="date"
                                value={filters.endDate}
                                onChange={(e) =>
                                    setFilters({
                                        ...filters,
                                        endDate: e.target.value,
                                    })
                                }
                                className="w-full border border-gray-300 rounded px-2 py-1 text-sm"
                            />
                        </div>

                        {/* Transaction Type */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                Type
                            </label>
                            <select
                                value={filters.transactionType}
                                onChange={(e) =>
                                    setFilters({
                                        ...filters,
                                        transactionType: e.target.value,
                                    })
                                }
                                className="w-full border border-gray-300 rounded px-2 py-1 text-sm"
                            >
                                <option value="all">All Transactions</option>
                                <option value="credits">Credits Only</option>
                                <option value="debits">Debits Only</option>
                            </select>
                        </div>

                        {/* Amount Range */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                Min Amount
                            </label>
                            <input
                                type="number"
                                step="0.01"
                                value={filters.minAmount}
                                onChange={(e) =>
                                    setFilters({
                                        ...filters,
                                        minAmount: e.target.value,
                                    })
                                }
                                placeholder="0.00"
                                className="w-full border border-gray-300 rounded px-2 py-1 text-sm"
                            />
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                Max Amount
                            </label>
                            <input
                                type="number"
                                step="0.01"
                                value={filters.maxAmount}
                                onChange={(e) =>
                                    setFilters({
                                        ...filters,
                                        maxAmount: e.target.value,
                                    })
                                }
                                placeholder="0.00"
                                className="w-full border border-gray-300 rounded px-2 py-1 text-sm"
                            />
                        </div>

                        {/* Search */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                Search
                            </label>
                            <input
                                type="text"
                                value={filters.searchText}
                                onChange={(e) =>
                                    setFilters({
                                        ...filters,
                                        searchText: e.target.value,
                                    })
                                }
                                placeholder="Search payee or memo..."
                                className="w-full border border-gray-300 rounded px-2 py-1 text-sm"
                            />
                        </div>
                    </div>

                    <div className="mt-3 flex justify-end">
                        <button
                            onClick={clearFilters}
                            className="bg-gray-500 hover:bg-gray-600 text-white px-3 py-1 rounded text-sm"
                        >
                            Clear Filters
                        </button>
                    </div>
                </div>
            )}

            {/* Summary Cards */}
            <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-6">
                <div className="bg-blue-50 p-3 rounded-lg">
                    <p className="text-sm text-blue-600 font-medium">
                        Total Transactions
                    </p>
                    <p className="text-2xl font-bold text-blue-800">
                        {summary.transactionCount}
                    </p>
                    {(filters.startDate ||
                        filters.endDate ||
                        filters.minAmount ||
                        filters.maxAmount ||
                        filters.searchText ||
                        filters.transactionType !== 'all') && (
                        <p className="text-xs text-blue-600 mt-1">Filtered</p>
                    )}
                </div>
                <div className="bg-green-50 p-3 rounded-lg">
                    <p className="text-sm text-green-600 font-medium">
                        Total Credits
                    </p>
                    <p className="text-2xl font-bold text-green-800">
                        {formatCurrency(summary.totalCredits)}
                    </p>
                </div>
                <div className="bg-red-50 p-3 rounded-lg">
                    <p className="text-sm text-red-600 font-medium">
                        Total Debits
                    </p>
                    <p className="text-2xl font-bold text-red-800">
                        {formatCurrency(summary.totalDebits)}
                    </p>
                </div>
                <div className="bg-purple-50 p-3 rounded-lg">
                    <p className="text-sm text-purple-600 font-medium">
                        Net Balance
                    </p>
                    <p
                        className={`text-2xl font-bold ${summary.balance >= 0 ? 'text-green-800' : 'text-red-800'}`}
                    >
                        {formatCurrency(summary.balance)}
                    </p>
                </div>
            </div>

            {/* Pagination Controls - Top */}
            {filteredTransactions.length > itemsPerPage && (
                <div className="mb-4 flex justify-between items-center">
                    <div className="text-sm text-gray-600">
                        Showing {(currentPage - 1) * itemsPerPage + 1} to{' '}
                        {Math.min(
                            currentPage * itemsPerPage,
                            filteredTransactions.length
                        )}{' '}
                        of {filteredTransactions.length} transactions
                    </div>
                    <div className="flex items-center gap-2">
                        <label className="text-sm text-gray-600">
                            Per page:
                        </label>
                        <select
                            value={itemsPerPage}
                            onChange={(e) => {
                                setItemsPerPage(Number(e.target.value));
                                setCurrentPage(1);
                            }}
                            className="border border-gray-300 rounded px-2 py-1 text-sm"
                        >
                            <option value={10}>10</option>
                            <option value={25}>25</option>
                            <option value={50}>50</option>
                            <option value={100}>100</option>
                        </select>
                    </div>
                </div>
            )}

            {/* Transactions Table */}
            {paginatedTransactions.length > 0 ? (
                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                            <tr>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Date
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Payee
                                </th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Memo
                                </th>
                                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Amount
                                </th>
                                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Balance
                                </th>
                            </tr>
                        </thead>
                        <tbody className="bg-white divide-y divide-gray-200">
                            {paginatedTransactions.map((transaction) => (
                                <tr
                                    key={transaction.id}
                                    className="hover:bg-gray-50"
                                >
                                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-900">
                                        {formatDate(
                                            transaction.transaction_date
                                        )}
                                    </td>
                                    <td className="px-4 py-3 text-sm text-gray-900">
                                        {transaction.payee || (
                                            <span className="text-gray-400 italic">
                                                No payee
                                            </span>
                                        )}
                                    </td>
                                    <td className="px-4 py-3 text-sm text-gray-500">
                                        {transaction.memo || (
                                            <span className="text-gray-400 italic">
                                                No memo
                                            </span>
                                        )}
                                    </td>
                                    <td
                                        className={`px-4 py-3 whitespace-nowrap text-sm text-right font-medium ${
                                            transaction.isDebit
                                                ? 'text-red-600'
                                                : 'text-green-600'
                                        }`}
                                    >
                                        {transaction.isDebit ? '-' : '+'}
                                        {formatCurrency(
                                            transaction.displayAmount
                                        )}
                                    </td>
                                    <td
                                        className={`px-4 py-3 whitespace-nowrap text-sm text-right font-medium ${
                                            transaction.runningBalance >= 0
                                                ? 'text-gray-900'
                                                : 'text-red-600'
                                        }`}
                                    >
                                        {formatCurrency(
                                            transaction.runningBalance
                                        )}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            ) : (
                <div className="text-center py-8 text-gray-500">
                    {filters.startDate ||
                    filters.endDate ||
                    filters.minAmount ||
                    filters.maxAmount ||
                    filters.searchText ||
                    filters.transactionType !== 'all'
                        ? 'No transactions match the current filters'
                        : 'No transactions found for this account'}
                </div>
            )}

            {/* Pagination Controls - Bottom */}
            {totalPages > 1 && (
                <div className="mt-4 flex justify-center items-center gap-2">
                    <button
                        onClick={() => goToPage(1)}
                        disabled={currentPage === 1}
                        className="px-3 py-1 rounded text-sm bg-gray-100 hover:bg-gray-200 disabled:bg-gray-50 disabled:text-gray-400"
                    >
                        First
                    </button>
                    <button
                        onClick={() => goToPage(currentPage - 1)}
                        disabled={currentPage === 1}
                        className="px-3 py-1 rounded text-sm bg-gray-100 hover:bg-gray-200 disabled:bg-gray-50 disabled:text-gray-400"
                    >
                        Previous
                    </button>

                    {getPaginationRange().map((page) => (
                        <button
                            key={page}
                            onClick={() => goToPage(page)}
                            className={`px-3 py-1 rounded text-sm ${
                                currentPage === page
                                    ? 'bg-blue-600 text-white'
                                    : 'bg-gray-100 hover:bg-gray-200'
                            }`}
                        >
                            {page}
                        </button>
                    ))}

                    <button
                        onClick={() => goToPage(currentPage + 1)}
                        disabled={currentPage === totalPages}
                        className="px-3 py-1 rounded text-sm bg-gray-100 hover:bg-gray-200 disabled:bg-gray-50 disabled:text-gray-400"
                    >
                        Next
                    </button>
                    <button
                        onClick={() => goToPage(totalPages)}
                        disabled={currentPage === totalPages}
                        className="px-3 py-1 rounded text-sm bg-gray-100 hover:bg-gray-200 disabled:bg-gray-50 disabled:text-gray-400"
                    >
                        Last
                    </button>
                </div>
            )}
        </div>
    );
}
