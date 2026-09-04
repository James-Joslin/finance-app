const isoDate = (value) => {
    if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value))
        return null;
    const parsed = new Date(value + 'T00:00:00Z');
    return Number.isNaN(parsed.getTime()) ? null : value;
};

export function currentMonthRange(now = new Date()) {
    const today = now.toISOString().slice(0, 10);
    return { startDate: today.slice(0, 7) + '-01', endDate: today };
}

export function overviewTrendRange(transactions = [], now = new Date()) {
    const today = now.toISOString().slice(0, 10);
    const latestActualDate = transactions
        .map((transaction) => isoDate(transaction.date))
        .filter((date) => date && date <= today)
        .sort()
        .at(-1);
    const endDate = latestActualDate || today;
    const start = new Date(endDate + 'T00:00:00Z');
    start.setUTCDate(start.getUTCDate() - 29);
    return { startDate: start.toISOString().slice(0, 10), endDate };
}
