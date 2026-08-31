const preferences = {
    currencyCode: 'GBP',
    locale: 'en-GB',
    timezone: 'Europe/London',
};

export function setFormatPreferences(settings = {}) {
    preferences.currencyCode = currencyCode(
        settings.currencyCode || preferences.currencyCode
    );
    preferences.locale =
        typeof settings.locale === 'string' && settings.locale.trim()
            ? settings.locale.trim()
            : 'en-GB';
    preferences.timezone =
        typeof settings.timezone === 'string' && settings.timezone.trim()
            ? settings.timezone.trim()
            : 'Europe/London';
}

export const money = (value, currency) =>
    new Intl.NumberFormat(preferences.locale, {
        style: 'currency',
        currency: currencyCode(
            typeof currency === 'string' ? currency : preferences.currencyCode
        ),
        maximumFractionDigits: 2,
    }).format(Number(value || 0));

export const compactMoney = (value, currency) =>
    new Intl.NumberFormat(preferences.locale, {
        style: 'currency',
        currency: currencyCode(
            typeof currency === 'string' ? currency : preferences.currencyCode
        ),
        notation: 'compact',
        maximumFractionDigits: 1,
    }).format(Number(value || 0));

const currencyCode = (value) =>
    typeof value === 'string' && /^[a-z]{3}$/i.test(value)
        ? value.toUpperCase()
        : 'GBP';

export const shortDate = (value) => {
    if (!value) return 'Not set';
    return new Intl.DateTimeFormat(preferences.locale, {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
        timeZone: 'UTC',
    }).format(new Date(value + 'T00:00:00Z'));
};

export const relativeDate = (value) => {
    if (!value) return '';
    const date = new Date(value + 'T00:00:00Z');
    const today = new Date(todayIso() + 'T00:00:00Z');
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

export function todayIso(now = new Date()) {
    const parts = new Intl.DateTimeFormat('en-CA', {
        timeZone: preferences.timezone,
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
    })
        .formatToParts(now)
        .reduce((result, part) => ({ ...result, [part.type]: part.value }), {});
    return `${parts.year}-${parts.month}-${parts.day}`;
}
