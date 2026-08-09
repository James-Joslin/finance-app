export const money = (value, currency = 'GBP') =>
    new Intl.NumberFormat('en-GB', {
        style: 'currency',
        currency: currencyCode(currency),
        maximumFractionDigits: 2,
    }).format(Number(value || 0));

export const compactMoney = (value, currency = 'GBP') =>
    new Intl.NumberFormat('en-GB', {
        style: 'currency',
        currency: currencyCode(currency),
        notation: 'compact',
        maximumFractionDigits: 1,
    }).format(Number(value || 0));

const currencyCode = (value) =>
    typeof value === 'string' && /^[a-z]{3}$/i.test(value) ? value.toUpperCase() : 'GBP';

export const shortDate = (value) => {
    if (!value) return 'Not set';
    return new Intl.DateTimeFormat('en-GB', {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
    }).format(new Date(value + 'T12:00:00'));
};

export const relativeDate = (value) => {
    if (!value) return '';
    const date = new Date(value + 'T12:00:00');
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const difference = Math.round((date - today) / 86400000);
    if (difference === 0) return 'Today';
    if (difference === 1) return 'Tomorrow';
    if (difference === -1) return 'Yesterday';
    if (difference > 1 && difference < 7) return 'In ' + difference + ' days';
    return shortDate(value);
};

export const percent = (value) => Number(value || 0).toFixed(1) + '%';

export const apiError = (error) =>
    error?.response?.data?.error || error?.message || 'Something went wrong.';
