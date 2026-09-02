import { useEffect } from 'react';

export function parseRecordId(value) {
    const id = Number(value);
    return Number.isInteger(id) && id > 0 ? id : null;
}
const HIGHLIGHT_CLASS = 'deep-link-highlight';

export function useDeepLinkTarget(targetId, ready, selector) {
    useEffect(() => {
        if (!targetId || !ready) return undefined;

        const targets = Array.from(document.querySelectorAll(selector)).filter(
            (element) => element.dataset.deepLinkId === String(targetId)
        );
        if (targets.length === 0) return undefined;

        const target =
            targets.find(
                (element) =>
                    !element.closest('[hidden]') &&
                    window.getComputedStyle(element).display !== 'none'
            ) || targets[0];
        targets.forEach((element) => element.classList.add(HIGHLIGHT_CLASS));
        target.scrollIntoView?.({ behavior: 'smooth', block: 'center' });

        const timeout = window.setTimeout(() => {
            targets.forEach((element) =>
                element.classList.remove(HIGHLIGHT_CLASS)
            );
        }, 1800);

        return () => {
            window.clearTimeout(timeout);
            targets.forEach((element) =>
                element.classList.remove(HIGHLIGHT_CLASS)
            );
        };
    }, [targetId, ready, selector]);
}
